# 上游基线

## 固定基线

- 仓库：[wetor/AnimeGo](https://github.com/wetor/AnimeGo)
- 分支：`develop`
- 提交：`c7475dfc55a374cd0dd08821bf17125dab1e3145`
- 提交时间：`2024-11-15T18:47:02+08:00`
- 提交说明：`Update README.md`
- 本地远端名：`upstream`
- 初次盘点日期：`2026-07-13`
- 复核日期：`2026-07-19`

移植实现以该提交的可观察行为为准，不以 `v0.10.5` Release 或其他分支为准。后续若同步上游，只比较 `upstream/develop`，并通过单独的兼容性变更提交完成，不在正在移植的模块中暗中混入上游更新。

## 已盘点的行为面

- CLI：`AnimeGo` 主程序和 `AnimeGo-plugin` 插件测试程序。
- 配置：YAML、环境变量覆盖、首次生成、备份、`1.1.0` 到 `1.7.1` 的升级链。
- 资源：首次启动释放内置 feed/filter/parser/rename/schedule Python 插件。
- 数据源：Mikan、Bangumi、Bangumi Archive、TheMovieDB。
- 输入与解析：RSS、torrent/magnet、番剧名/集数/字幕组/季度解析。
- 插件：feed、parser、filter、rename、schedule，以及 `core`、`log` 内置模块。
- 下载客户端：qBittorrent、Transmission。
- 下载流程：添加、状态轮询、完成/做种通知、去重、失败重试、删除。
- 文件流程：hard link、link+delete、move、wait_move、重命名、`tvshow.nfo`。
- 持久化：Bolt 缓存、目录 JSON 数据库、缓存 TTL、Bangumi Archive 缓存。
- 调度：六字段（含秒）Cron、启动即运行、取消与优雅退出。
- Web：权威 OpenAPI 列出 11 个 REST operation、1 个 WebSocket operation（早期计划误记为“10 个 HTTP API”）、静态页、Swagger、access-key 校验。
- 发布：Windows/Linux/macOS 多架构二进制、Docker、嵌入资源。

## 上游测试基线

上游包含 178 个 Go `Test*` 函数，其中一部分属于 vendored/third-party 包。fixture 覆盖配置历史版本、RSS、Mikan/Bangumi/TMDB 响应、torrent 文件、Python 插件及重命名输入。

当前 Windows 主机的用户级 Go 目标为 `GOOS=android`、`GOARCH=arm64`。不覆盖该环境时执行 `go test ./...`，测试包会生成 Android ARM64 二进制并统一在 Windows 启动失败：

```text
fork/exec ...\*.test: %1 is not a valid Win32 application.
```

局部覆盖为 `GOOS=windows GOARCH=amd64 CGO_ENABLED=0` 后，首次并行全量运行仅 `third_party/qbapi` 出现一次无诊断编译失败，单独重跑通过；串行全量复核中该包通过，但 `internal/pkg/request` 因本机策略禁止绑定上游硬编码的 `127.0.0.1:8080` 而失败。其余包通过或按上游门禁跳过。完整命令和白名单见 [`baseline/UPSTREAM_TESTS_WINDOWS.md`](baseline/UPSTREAM_TESTS_WINDOWS.md)。仍需在固定 Linux CI/容器保存机器可读基线。上游本来就失败或跳过的案例不计为移植回归，但必须登记。

## 兼容定义

“1:1”指相同输入产生等价输出和副作用，优先级如下：

1. 用户配置键、环境变量、CLI 参数及默认值兼容。
2. API 路径、方法、认证、状态码和 JSON 字段兼容。
3. RSS/解析/过滤/下载/重命名/刮削结果兼容。
4. qBittorrent 的交互和状态机兼容；上游 Transmission 支持是本项目明确接受的范围例外。
5. 数据目录和媒体目录的可观察结果兼容。
6. 日志文案尽量兼容；时间、并发顺序、堆栈等不稳定内容不做逐字比较。

下载器范围有一项永久例外：只实现 qBittorrent，多命名实例和全部业务状态机按 qB 验收；Transmission 配置只要求可读取并给出 `UnsupportedDownloaderType`，不实现适配器，也不列入后续里程碑。因此项目的“1:1”声明始终排除上游 Transmission 下载器能力。

以下不自动等同于“1:1”：Go 内部包结构、Bolt 文件二进制格式、gpython 的实现细节、原发布矩阵中的 MIPS/386 架构。它们分别通过 .NET 分层、迁移工具、插件协议和明确的 RID 支持矩阵处理。
