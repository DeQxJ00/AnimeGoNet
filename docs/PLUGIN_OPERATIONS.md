# 外部 C# 插件安装与运维

AnimeGoNet 不加载第三方 DLL，也不执行 Python。外部插件是单 RID、自包含的 C# 进程包，
主程序只通过严格 JSON Lines 协议通信。完整接口和 manifest 说明见
[PLUGIN_ARCHITECTURE.md](PLUGIN_ARCHITECTURE.md)。

## 包目录

每个包必须是 `data_path/plugins` 的一个直接子目录：

```text
data/plugins/com.example.filter/
  plugin.json
  config.schema.json
  AnimeGo.Example.Filter.exe
```

Linux/macOS 入口没有 `.exe`。一个包只能声明一个 RID：`win-x64`、`win-arm64`、
`linux-x64`、`linux-arm64` 或 `osx-arm64`。插件可写数据只允许放在
`data_path/plugin-data/<plugin-id>`；包目录应只读。不要把配置、Cookie、passkey、API key
或下载内容放进包目录。

## 安装前验证

先核对发布者提供的 SHA-256，再使用与主程序同版本的 PluginTool：

```powershell
dotnet run --project src/AnimeGo.PluginTool -c Release -- `
  validate E:\PluginStaging\com.example.filter --rid win-x64

dotnet run --project src/AnimeGo.PluginTool -c Release -- `
  run E:\PluginStaging\com.example.filter `
  --fixture E:\PluginStaging\filter.fixture.json --rid win-x64

dotnet run --project src/AnimeGo.PluginTool -c Release -- `
  pack E:\PluginStaging\com.example.filter `
  --output E:\PluginStaging\com.example.filter-win-x64.zip --rid win-x64
```

`validate` 不执行插件；`run` 才会启动真实进程。fixture 只能使用合成数据，不得包含生产
凭据、私人 Torrent URL 或本机用户目录。三个命令的 stdout/stderr 都是单行稳定 JSON；
退出码和严格边界见插件架构文档。

## 安装和启用

1. 保持插件默认禁用，停止 AnimeGoNet。
2. 把验证过的完整目录复制到 `data_path/plugins/<plugin-id>`；不要把 ZIP 直接放进去。
3. Unix 上确保包目录不可被 group/world 写入，入口具有执行位；Docker 中只读挂载 packages。
4. 启动 AnimeGoNet，在状态页确认插件 ID、版本、RID、类型和发现诊断。
5. 在 WebUI 外部插件页填写 schema 声明的参数，先保存为禁用，再显式启用。
6. 用合成任务验证对应 source/feed/parser/filter/rename/schedule operation。

启停、args 和 vars 保存于 `data_path/config/external-plugins.private.json`。它可能含第三方
凭据，只写不回显，必须随整个 `data_path` 安全备份且禁止提交 Git。插件进程不会直接获得
SQLite、DI 容器、下载器对象或其他插件的数据目录。

## 升级与回滚

同一插件 ID 不得同时存在于两个子目录。升级流程：

1. 在独立 staging 目录验证新包和 fixture，记录包 SHA-256。
2. 停用插件并等待活动调用结束，然后停止 AnimeGoNet。
3. 把旧包目录移动到 `data_path/plugin-backups/<id>/<version>`；不要覆盖已有备份。
4. 以一次完整目录替换安装新包，确认目录名不影响 manifest 中的稳定 ID。
5. 启动、检查发现状态，再显式启用并运行合成验收。

回滚时停用并停止主程序，恢复旧完整包目录，再启动和显式启用。插件自己的
`plugin-data` 是否向后兼容由插件作者负责；不确定时连同安装前备份一起恢复。不要让旧版和
新版进程同时指向同一个可写数据目录。

## 故障处理

- `manifest`/RID/权限/路径错误：修正包本身，不能用配置绕过。
- 协议超时、崩溃或脏 stdout：宿主关闭会话并指数退避；重复失败会自动禁用。
- 配置 schema 错误：原私有配置保持不变，按字段诊断修正后重新保存。
- 自动禁用后：先修复根因，再在 WebUI 显式 reset；不要通过删除状态文件伪造恢复。
- stderr：宿主只转发受限、脱敏、带插件 ID 的诊断；插件不得把 secret 写入 stdout/stderr。

卸载前先停用、停止 AnimeGoNet并备份。只移除精确插件包目录；是否保留
`plugin-data/<id>` 应由用户明确决定，主程序不会替第三方插件猜测删除策略。
