# GitHub Actions、NativeAOT 与 Docker

## 当前门禁

- `.github/workflows/dotnet-ci.yml`：在 Windows 2025、Ubuntu 24.04 和 macOS 15 上还原、Release 构建并运行全部 .NET 测试。
- `.github/workflows/animegonet-native-aot.yml`：分别在原生 runner 发布并冒烟 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-arm64`。
- `.github/workflows/animegonet-docker.yml`：使用 Buildx 构建 `linux/amd64`、`linux/arm64`，并加载 amd64 镜像验证 API、SQLite、NativeAOT 和挂载路径。

原上游 Go 工作流保留作为行为基准，没有覆盖或删除。

## NativeAOT runner 矩阵

| RID | GitHub runner | 冒烟入口 |
|---|---|---|
| `win-x64` | `windows-2025` | `AnimeGoNet.App.exe` |
| `win-arm64` | `windows-11-arm` | `AnimeGoNet.App.exe` |
| `linux-x64` | `ubuntu-24.04` | `AnimeGoNet.App` |
| `linux-arm64` | `ubuntu-24.04-arm` | `AnimeGoNet.App` |
| `osx-arm64` | `macos-15` | `AnimeGoNet.App` |

`eng/smoke-native.ps1` 使用随机本地端口和独立临时目录，验证 `/ping`、受保护状态 API、SQLite schema、NativeAOT 标识、静态 WebUI，以及 `/websocket/log` 的原生 upgrade 和 pause 控制帧，并在结束时回收进程和临时目录。

## Docker 契约

为保留上游 `Dockerfile`，新主程序使用 `Dockerfile.animegonet`。镜像为 .NET 10 NativeAOT、非 root 用户运行，并固定：

- `data_path=/data`
- `download_path=/download/incomplete`
- `save_path=/download/anime`

`docker-compose.animegonet.yml` 将 AnimeGoNet 和 qBittorrent 的同一宿主 `./download` 挂载到容器 `/download`，数据目录单独挂载到 `/data`。容器模式强制非空 `access_key`。

## 验证状态

2026-07-19 本机已通过 52 项测试、win-x64 NativeAOT publish 与 published-binary smoke；smoke 也会真实 POST 统一导入端点，并验证 source-generated JSON、默认路由、URL 指纹和 qBittorrent capability。所有新增 YAML 可解析，容器 smoke shell 脚本通过语法检查。本机没有 Docker CLI，其余四个 RID 和双架构容器由 GitHub Actions 首次运行完成最终验收。

## 参考版本

- `actions/checkout@v6`
- `actions/setup-dotnet@v5`
- `actions/upload-artifact@v7`
- `docker/setup-qemu-action@v4`
- `docker/setup-buildx-action@v4`
- `docker/build-push-action@v7`

这些 major tag 和 runner label 在 2026-07-19 对照官方 action 仓库及 GitHub-hosted runner 文档确认。
