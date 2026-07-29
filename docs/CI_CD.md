# GitHub Actions、NativeAOT 与 Docker

## 当前门禁

- `.github/workflows/dotnet-ci.yml`：在 Windows 2025、Ubuntu 24.04 和 macOS 15 上还原、Release 构建并运行全部 .NET 测试。
- `.github/workflows/animegonet-native-aot.yml`：分别在原生 runner 发布并冒烟 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-arm64`。
- `.github/workflows/animegonet-docker.yml`：使用 Buildx 构建 `linux/amd64`、`linux/arm64`，并加载 amd64 镜像验证 API、SQLite、NativeAOT 和挂载路径。

原上游 Go 工作流保留作为行为基准，没有覆盖或删除。

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

`eng/smoke-native.ps1` 使用随机本地端口和独立临时目录，验证 `/ping`、受保护状态 API、SQLite schema、NativeAOT 标识、静态 WebUI、`data_path/logs/animego.log` 非空，以及 `/websocket/log` 的原生 upgrade 和 pause 控制帧。五 RID 还以 `-LegacyYamlUpgrade` 再启动一次同一原生二进制，验证旧 1.6.1 YAML 原字节备份、规范 1.7.1 替换和动态 tag 模板不误入静态 tag；两种模式结束时均回收进程和临时目录。

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
   探测，并验证 Mikan 路由到 `bt`、U2/TTG 路由到 `pt`。
5. 无论成功或失败都执行 `docker compose down --volumes --remove-orphans` 并删除
   临时根目录。

fixture 的 tracker 固定为 `http://127.0.0.1:9/announce`，文件名为
`animegonet-ci.bin`、长度 5 字节，不包含 passkey，不连接公网 tracker，也不读取
用户 RSS、下载任务、Cookie、WebUI 配置或 TestSpace。默认 .NET 测试不启动
Docker；Docker workflow 才执行这项门禁。

## 验证状态

2026-07-30 本机可验证全部 YAML 解析、两个容器 smoke shell 脚本语法、
隔离编排契约测试、全量 .NET 测试和 win-x64 NativeAOT published-binary smoke。
本机没有 Docker CLI，因此双 qB 实际容器调用、其余四个 RID 和双架构镜像仍由
GitHub Actions 运行并提供最终外部证据。

## 参考版本

- `actions/checkout@v6`
- `actions/setup-dotnet@v5`
- `actions/upload-artifact@v7`
- `docker/setup-qemu-action@v4`
- `docker/setup-buildx-action@v4`
- `docker/build-push-action@v7`

这些 major tag 和 runner label 在 2026-07-19 对照官方 action 仓库及 GitHub-hosted runner 文档确认。
