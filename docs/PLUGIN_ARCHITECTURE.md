# AnimeGoNet C# 插件架构

## 1. 结论

AnimeGoNet 完全移除 Python 运行时和 Python 插件执行能力。C# 插件分为：

- **编译期插件**：官方功能和内置插件，显式注册后随 AnimeGoNet 一起 NativeAOT 发布。
- **外部进程插件**：第三方动态插件，是独立的 C# 可执行程序，通过版本化 JSON Lines 协议通信。

NativeAOT 主程序不扫描或加载插件 DLL，不使用 `Assembly.Load*`、`Reflection.Emit`、MEF 或运行时代理。

## 2. 编译期插件契约

`AnimeGo.Plugin.Abstractions` 只包含稳定 DTO、接口和错误码，不引用 Web、SQLite、下载客户端等基础设施。

```csharp
public interface IAnimeGoPlugin
{
    PluginDescriptor Descriptor { get; }
}

public interface IInputSourceAdapter : IAnimeGoPlugin
{
    ValueTask<SourceIngestResult> NormalizeAsync(SourceIngestContext context, CancellationToken cancellationToken);
}

public interface IFeedPlugin : IAnimeGoPlugin
{
    ValueTask<FeedResult> FetchAsync(FeedContext context, CancellationToken cancellationToken);
}

public interface ITitleParserPlugin : IAnimeGoPlugin
{
    ValueTask<TitleParseResult> ParseAsync(TitleParseContext context, CancellationToken cancellationToken);
}

public interface IFeedFilterPlugin : IAnimeGoPlugin
{
    ValueTask<FilterResult> FilterAsync(FilterContext context, CancellationToken cancellationToken);
}

public interface IRenamePlugin : IAnimeGoPlugin
{
    ValueTask<RenameResult> RenameAsync(RenameContext context, CancellationToken cancellationToken);
}

public interface IScheduledPlugin : IAnimeGoPlugin
{
    ValueTask ExecuteAsync(ScheduledContext context, CancellationToken cancellationToken);
}
```

内置实现通过普通 C# 引用和显式注册加入：

```csharp
services.AddSingleton<IInputSourceAdapter, MikanSourceAdapter>();
services.AddSingleton<IInputSourceAdapter, U2SourceAdapter>();
services.AddSingleton<IInputSourceAdapter, TtgSourceAdapter>();
services.AddSingleton<IFeedPlugin, MikanRssFeed>();
services.AddSingleton<ITitleParserPlugin, AnimeTitleParser>();
services.AddSingleton<IFeedFilterPlugin, MikanToolFilter>();
services.AddSingleton<IRenamePlugin, AnimeLibraryRename>();
services.AddSingleton<IScheduledPlugin, MetadataRefreshTask>();
```

`PluginCatalog` 根据稳定 ID 和配置顺序建立执行链；禁止通过反射扫描程序集寻找实现。

当前宿主已经落地这一边界：目录构造函数只接受显式实例，验证插件只实现一个类别契约、描述类别一致、ID 为小写稳定格式且全局不重复，并按 `order`、`id` 确定性排序。Mikan、U2、TTG source adapter 在 `BuiltInPluginCatalog.Create()` 中逐项构造；统一导入和来源路由预览都从同一个目录解析 adapter。插件返回的来源 ID、HTTP(S) Torrent URL、标题与 SHA-256 指纹还会由宿主再次验证，不能仅凭插件声明进入 staging。

其余内置实现同样不是 marker 或展示对象：

- `mikan-rss` 委托有界、安全 URL 的 `RssFeedReader`，旧 `/api/rss` 从目录执行它。
- `mikan-tool` 委托持久化的五级兼容过滤器，返回逐项 outcome/audit metadata；宿主校验数量、索引、状态和 revision 后才能继续。
- `mikan-title` 委托 Mikan RSS Episode parser，普通、小数与特别篇类型保持分离；批次 planner 消费插件结果。
- `anime-library` 委托 `MediaPathPlanner`，媒体整理在文件操作落库前消费插件目标并再次执行根目录边界检查。
- `staged-torrent-dispatch` 委托真实 dispatcher，后台 worker 使用插件的结果与下一次建议延迟。

需要固定 Cron 的 schedule 插件由 `PluginScheduleCoordinator` 显式注册，不从插件文件读取或扫描。Cron 是含秒的六字段格式，支持 `?`、列表、范围、步长、英文月份/星期和标准 descriptor；`StartRun=true` 在注册完成后立即执行一次，`NextTime` 始终由同一已验证表达式与指定时区计算。失败沿用上游三次、每次间隔三秒的重试约定，并在每次调用参数中写入 `__retry_count__=0/1/2`。任务快照只记录稳定插件 ID、Cron、运行数、下一次/最近执行时间和安全失败码。

默认目录不注册 Python 名称，不读取或执行 `.py` 文件；旧 `filter/mikan_tool.py` 只保留为 API 配置别名并映射到 SQLite/C# 实现。

`TitleParserManager` 保留上游 `cmd/animego/main.go` 的选择语义：未指定 ID 时使用目录顺序中的第一个 parser；显式 ID 只执行该 parser。一次返回无匹配或错误不会隐式尝试下一个 parser，避免改变解析优先级。

`OrderedFeedFilterManager` 保留上游 `filter.Manager.Update` 的串联语义：每个 filter 只接收前一层 accepted items，并按照显式 ID 列表或目录确定性顺序执行；任一插件返回错误就终止，不执行后续插件。宿主额外验证决定数量、原始 item index 全覆盖/不重复、outcome/reason 非空；无效结果以 `filter_result_invalid` 终止。显式空链用于明确跳过所有插件，和“未提供链时使用目录顺序”严格区分。

## 3. 外部 C# 插件包

外部插件按 RID 发布为自包含可执行程序，推荐启用 NativeAOT。一个安装目录示例：

```text
data/plugins/com.example.animego.filter-resolution/
  plugin.json
  config.json
  config.schema.json
  AnimeGo.Plugin.FilterResolution.exe   # Windows
  # AnimeGo.Plugin.FilterResolution     # Linux/macOS
```

每个发布包只包含一个 RID，避免在运行时猜测平台。`plugin.json` 最小格式：

```json
{
  "id": "com.example.animego.filter-resolution",
  "name": "Resolution Filter",
  "version": "1.0.0",
  "apiVersion": 1,
  "type": "filter",
  "rid": "linux-arm64",
  "entryPoint": "AnimeGo.Plugin.FilterResolution",
  "configSchema": "config.schema.json",
  "capabilities": []
}
```

稳定 ID 使用反向域名格式。宿主启动前验证 manifest、当前 RID、协议 major、入口路径必须位于插件目录内，并拒绝符号链接逃逸和可写权限异常。
`type` 只允许 `source`、`feed`、`parser`、`filter`、`rename`、`schedule`；source 插件只能规范化输入和返回稳定 DTO，不能自行选择未授权下载器或直接获得客户端凭据。

## 4. 进程协议

主程序以 stdio 启动插件。stdin/stdout 每行一个 UTF-8 JSON 对象，stdout 只用于协议，日志写 stderr。

必须支持四个操作：

- `initialize`：交换宿主版本、插件版本、协议版本和能力。
- `execute`：执行对应插件类型的操作。
- `health`：检查进程是否可继续使用。
- `shutdown`：正常退出。

请求示例：

```json
{"apiVersion":1,"requestId":"01J...","method":"execute","operation":"filter.all","payload":{"items":[]},"config":{}}
```

响应示例：

```json
{"apiVersion":1,"requestId":"01J...","ok":true,"result":{"acceptedIndexes":[]}}
```

错误响应必须使用稳定错误码，不向宿主发送运行时类型名或堆栈作为协议字段：

```json
{"apiVersion":1,"requestId":"01J...","ok":false,"error":{"code":"invalid_config","message":"resolution is required"}}
```

`AnimeGo.Plugin.Sdk` 提供协议循环和 `System.Text.Json` source-generation context，插件作者只实现强类型处理器。

## 5. 生命周期与限制

- 默认一个插件一个长期进程；崩溃后指数退避，达到阈值自动禁用并告警。
- 每次调用支持取消、执行超时、最大输入/输出大小和最大并发数。
- stdout 出现非 JSON、request ID 不匹配或协议版本不兼容时终止进程。
- stderr 按插件 ID 写入结构化日志并限制速率。
- 外部插件不直接获得 AnimeGoNet 的数据库连接、DI 容器或下载器对象；只接收完成任务所需 DTO。
- 外部可执行程序不是安全沙箱。只运行用户信任的插件；首版 Web UI 不提供上传可执行文件，只负责发现、启停、配置和显示校验结果。
- Docker 中插件目录只读挂载；需要写入的数据使用单独、受限的插件数据目录。

## 6. 兼容旧配置

- `builtin_mikan_rss.py`、`builtin_parser.py`、`builtin_rename.py` 和已知默认插件名映射到对应 C# 内置 ID。
- `filter/mikan_tool.py` 映射到内置 MikanTool，并保留 AnimeGoHelper 的配置读写 API。
- 未知 `type: py/python` 配置在迁移报告中列出并明确报不支持；不执行、不静默丢弃。
- 新配置只写 C# 插件稳定 ID，不再写 `.py` 文件名。

## 7. SDK 与模板交付

提供：

- `AnimeGo.Plugin.Abstractions` NuGet 包。
- `AnimeGo.Plugin.Sdk` NuGet 包。
- `dotnet new animego-plugin --type filter` 模板。
- source/feed/parser/filter/rename/schedule 六种最小示例。
- Windows x64/ARM64、Linux x64/ARM64、macOS ARM64 NativeAOT GitHub Actions 模板。
- `AnimeGo.PluginTool validate/run/pack` 命令，用 fixture 在发布前验证 manifest、协议和结果。

## 8. 验证与提交拆分

1. `feat(plugins): add csharp plugin abstractions and catalog`
   - 六类接口、DTO、显式注册和顺序测试。
2. `feat(plugins): port builtin plugins and mikan tool`
   - 上游 fixture/golden 全部通过后提交。
3. `feat(plugins): add external executable protocol`
   - 生命周期、超时、取消、崩溃、脏输出、版本不匹配测试。
4. `feat(plugins): add sdk templates and packaging`
   - 六种示例插件 JIT/AOT 测试，五 RID 发布验证。
5. `feat(web-ui): manage csharp plugins`
   - 发现、启停、配置 schema、错误展示和 Docker 只读目录 E2E。

每一步测试通过后独立提交，不把协议、内置移植和 Web UI 混在同一个提交中。
