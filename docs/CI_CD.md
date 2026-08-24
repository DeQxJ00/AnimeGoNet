# GitHub Actions、NativeAOT 与 Docker

## 当前门禁

- `.github/workflows/dotnet-ci.yml`：在 Windows 2025、Ubuntu 24.04 和 macOS 15 上还原、Release 构建并运行全部 .NET 测试。
- `.github/workflows/animegonet-native-aot.yml`：分别在原生 runner 发布并冒烟 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-arm64`；每个 artifact 同时包含相同 RID 的 NativeAOT 旧缓存导入器，并执行 help/headless 零监听 CLI、cache JSON + 目录 sidecar 组合迁移 smoke。
- `.github/workflows/dotnet-ci.yml`：除三平台 .NET/WebUI 门禁外，用 Go 1.22 对只读 legacy bbolt exporter 的真实 fixture 运行测试。
- `.github/workflows/animegonet-docker.yml`：使用 Buildx 构建 `linux/amd64`、`linux/arm64`，并加载 amd64 镜像验证 API、SQLite、NativeAOT 和挂载路径。

`.NET` build-test job 另把公开 `wetor/AnimeGo@c7475dfc55a374cd0dd08821bf17125dab1e3145`
检出到独立 `upstream-animego` 子目录，只供 parity tests 读取。当前四个真实
`.torrent` 测试直接解析原文件，但 DTO/断言/TRX 均不投影 announce 或 tracker；
fixture 不复制进 AnimeGoNet Git 历史。

## NativeAOT runner 矩阵

| RID | GitHub runner | 冒烟入口 |
|---|---|---|
| `win-x64` | `windows-2025` | `AnimeGoNet.App.exe` |
| `win-arm64` | `windows-11-arm` | `AnimeGoNet.App.exe` |
| `linux-x64` | `ubuntu-24.04` | `AnimeGoNet.App` |
| `linux-arm64` | `ubuntu-24.04-arm` | `AnimeGoNet.App` |
| `osx-arm64` | `macos-15` | `AnimeGoNet.App` |

每个矩阵任务在安装 SDK 之前先运行 `eng/assert-native-runner.ps1`，直接读取当前进程的
实际 OS 与 `OSArchitecture`，并要求与 RID 完全一致。runner 标签即使将来被重映射，
错误架构也会在 restore/publish 和 artifact 上传之前失败。2026-08-11 已对照 GitHub
官方 hosted/partner runner 清单复核 `windows-11-arm`、`ubuntu-24.04-arm` 和
`macos-15`；实际跨架构运行结果仍以对应 Actions job 为准。

`eng/smoke-native.ps1` 使用随机本地端口和独立临时目录，验证 `/ping`、受保护状态 API、SQLite schema、NativeAOT 标识、静态 WebUI、`data_path/logs/animego.log` 非空，以及 `/websocket/log` 的原生 upgrade 和 pause 控制帧。五 RID 还以 `-LegacyYamlUpgrade` 再启动一次同一原生二进制，验证旧 1.6.1 YAML 原字节备份、规范 1.7.1 替换、旧动态 tag 模板进入专用 `dynamic_tag_template` 且不误入静态 tags；两种模式结束时均回收进程和临时目录。

`eng/smoke-native-metadata.ps1` 在同一五 RID 矩阵中再次使用实际发布二进制：先关闭 workers 完成首次建库，再由 `AnimeGoNet.NativeMetadataSmokeFixture` 通过正式 Data Store 写入唯一的已下载单文件任务；随后打开 workers，并把 AI、TMDB MCP 与 TMDB API 全部固定到随机 `127.0.0.1` fixture。门禁要求一次任务级 AI 调用完成两轮对话和 MCP 工具调用，经 TMDB Series/Season/Episode 逐级验证后在 SQLite 与公开任务 API 中得到 `S02E07`、`ai_metadata` 和 `tmdb_verified`。qB 实例在该 smoke 中显式禁用；临时数据库、日志、fixture 进程和两个原生进程无论成功失败都回收，不读取用户 TestSpace 或真实凭据。

`eng/smoke-native-cli.ps1` 的无 Web headless 进程完成建库和零监听检查后由测试直接发送
SIGTERM；Unix runner 接受应用自行归零退出或标准的 `128 + SIGTERM = 143`，其他退出码、
发送失败或七秒内未退出仍然失败。完整 Web 宿主 smoke 继续要求应用完成优雅关闭。

每个 RID 在 `upload-artifact` 前运行 `eng/generate-release-metadata.ps1`。脚本只读取该 RID 的实际 publish 目录和本次 restore 的 `project.assets.json`，生成三项随 artifact 一起交付的确定性元数据：覆盖所有发布文件但不自包含的 `SHA256SUMS`、包含精确 NuGet 名称/版本/package SHA-512/SPDX 许可证和 purl 的 CycloneDX 1.5 `sbom.cdx.json`，以及 `THIRD-PARTY-LICENSES.txt`。NuGet 声明许可证文件时会把有界 UTF-8 原文纳入清单；缺失/未知许可证、非法 nuspec URL、包缓存缺失、符号链接、重复规范路径或不安全输入均使 job 失败。输出不包含本机包缓存路径、仓库路径、凭据或生成时间，重复执行字节一致。

推送 `vMAJOR.MINOR.PATCH-SUFFIX` 标签后，只有五个 RID 的 publish、原生 smoke、
插件模板和发布元数据任务全部成功，`prerelease` job 才会下载五份 Actions artifact。
它逐 RID 调用 `eng/package-native-release.ps1`，重新验证内部 `SHA256SUMS` 的精确文件集，
生成五个确定性 ZIP 及各自 `.sha256`，并要求包数完整后使用已有远端标签创建 GitHub
Prerelease。`--verify-tag` 禁止工作流暗中创建标签，`--latest=false` 不把预发布误设为
稳定最新版；工作流不使用覆盖资产的 `--clobber`。普通 branch/PR/workflow_dispatch
只构建 artifact，不发布 Release。

AnimeGoNetData 的构建和 Release 发布由独立数据仓库自行负责，主程序仓库不再定义或
调度该 Action。这里仍保留 DataBuilder、数据格式、离线导入和在线更新客户端，供独立
数据仓库生成包以及 AnimeGoNet 主程序消费经过校验的发布资产。

## Docker 契约

为保留上游 `Dockerfile`，新主程序使用 `Dockerfile.animegonet`。镜像为 .NET 10 NativeAOT、非 root 用户运行，并固定：

- `data_path=/data`
- `download_path=/download/incomplete`
- `save_path=/download/anime`

`docker-compose.animegonet.yml` 将 AnimeGoNet 和两个 qBittorrent 的同一宿主
`./download` 挂载到容器 `/download`，数据目录单独挂载到 `/data`。容器模式
强制非空 `access_key`。AnimeGoNet 以镜像内非 root 用户、只读根文件系统、
`/tmp` tmpfs 和 `no-new-privileges` 运行；可写位置只来自上述显式挂载。
Compose 用同一组 `PUID`/`PGID` 运行 AnimeGoNet 和两个 qBittorrent，避免共享目录
出现跨容器所有权不一致。启动前应显式创建 `data`、`download/incomplete/bt`、
`download/incomplete/pt`、`download/anime`、`qbittorrent/bt` 和
`qbittorrent/pt`，并把这些目录所有权设为配置的 `PUID:PGID`；不要依赖 Docker
以 root 自动创建宿主 bind 目录。

基础镜像 smoke 不依赖 Dockerfile 的默认 UID：它显式用 runner 的非 root
UID/GID 启动同一镜像，并强制 `--read-only`、`/tmp` noexec tmpfs 与
`no-new-privileges`。启动后同时检查 Docker 实际 HostConfig、容器内 UID/GID、
`/data`/`/download`/`/tmp` 精确临时写删、镜像 healthcheck、NativeAOT 状态和
SQLite 建库；最后发送 SIGTERM，要求 7 秒内退出码为 0 且挂载数据库仍存在。
这证明自定义 PUID/PGID 不要求写入 `/app` 或根文件系统。

LinuxServer qBittorrent 首次启动会在容器日志中提供临时 `admin` 密码。生产部署
必须先登录两个实例并设置各自密码，再通过 WebUI 写入 AnimeGoNet 私有下载器
配置；不要把密码写入 Compose 或 Git。两个实例的默认保存路径应分别设为
`/download/incomplete/bt` 和 `/download/incomplete/pt`。

## 双 qBittorrent 容器集成门禁

`docker-compose.qbittorrent-integration.yml` 仅用于 CI/显式集成测试。入口
`eng/smoke-qbittorrent-compose.sh IMAGE` 会：

1. 创建全新的临时根目录和唯一 Compose project，所有映射端口只绑定
   `127.0.0.1` 的随机端口。
2. 启动 `bt`、`pt` 两个独立 qBittorrent，读取各自首启临时密码，设置测试期
   密码和 `/download/incomplete/{bt|pt}` 保存路径，重启并重新登录。
3. 对两个实例分别创建唯一 category/tag，添加仓库内 5 字节 Torrent fixture，
   验证 list、文件清单、priority 0/1、start、stop、delete。
4. 启动 AnimeGoNet NativeAOT 镜像，验证两个下载器的连接诊断和共享目录硬链接
   探测，并验证 Mikan 路由到 `bt`、U2 路由到 `pt`。
5. 无论成功或失败都执行 `docker compose down --volumes --remove-orphans` 并删除
   临时根目录。

fixture 的 tracker 固定为 `http://127.0.0.1:9/announce`，文件名为
`animegonet-ci.bin`、长度 5 字节，不包含 passkey，不连接公网 tracker，也不读取
用户 RSS、下载任务、Cookie、WebUI 配置或 TestSpace。默认 .NET 测试不启动
Docker；Docker workflow 才执行这项门禁。

## 验证状态

2026-08-11 已在 Ubuntu 24.04 x86_64 CT 真实构建并运行 linux-x64 NativeAOT 镜像，
通过非 root/只读根文件系统/healthcheck/SQLite/共享路径/SIGTERM、双 qB 统一导入、
合法 WebSeed 下载、Bangumi/TMDB、move/NFO/sidecar、外部 C# 插件及发布镜像
Playwright WebUI 门禁。脱敏报告和明确边界见
`docs/verification/2026-08-11-ubuntu-ct-docker-validation.md`。

这份 CT 证据不替代 linux-arm64 Docker/原生 runner、win-arm64、osx-arm64 或实际
GitHub Prerelease。双架构镜像仍要等 arm64 原生/Buildx 结果后才能整体标记完成。

## 参考版本

- `actions/checkout@v6`
- `actions/setup-dotnet@v5`
- `actions/upload-artifact@v7`
- `actions/download-artifact@v8`
- `docker/setup-qemu-action@v4`
- `docker/setup-buildx-action@v4`
- `docker/build-push-action@v7`

这些 major tag 和 runner label 在 2026-07-19 对照官方 action 仓库及 GitHub-hosted runner 文档确认；`actions/download-artifact@v8` 与 GitHub CLI `gh release create --verify-tag --prerelease` 在 2026-08-10 复核官方发布页/手册。
