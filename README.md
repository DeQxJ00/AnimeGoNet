# AnimeGoNet

AnimeGoNet 是 `wetor/AnimeGo develop` 业务行为的 .NET 10 / NativeAOT
移植。主程序使用 ASP.NET Core Minimal API、SQLite 显式 SQL 和静态
TypeScript/HTML/CSS WebUI；首版下载器只支持 qBittorrent，可配置多个命名实例，
正式输入源仅交付 Mikan。U2 已由项目所有者确认为首版暂缓；现有通用
adapter/API/路由骨架只作为未来扩展兼容面保留，不声明为首版可用功能。Python 插件
已移除，官方插件均为编译期注册的 C# 实现。

当前开发基线和未完成项以 [TODO.md](TODO.md) 为准，上游逐项映射见
[docs/PORTING_CHECKLIST.md](docs/PORTING_CHECKLIST.md)。
本机私有 Mikan/qB 真实链路的显式验收方法见
[docs/MIKAN_LIVE_INTEGRATION.md](docs/MIKAN_LIVE_INTEGRATION.md)。

## 本机启动

需要 .NET SDK 10.0.300 或同一 feature band：

```powershell
dotnet restore AnimeGoNet.slnx
dotnet run --project src/AnimeGoNet.App -- `
  --data_path E:\AnimeGoNet\data `
  --download_path E:\AnimeGoNet\download `
  --save_path E:\AnimeGoNet\library `
  --movie_save_path E:\AnimeGoNet\movies
```

原生程序默认只监听 `http://127.0.0.1:7991`。可用 YAML `web.host` / `web.port`、
兼容变量 `ANIMEGO_WEB_HOST` / `ANIMEGO_WEB_PORT` 修改；标准 ASP.NET Core
`--urls` / `ASPNETCORE_URLS` 的优先级最高。

首次启动会在 `data_path/animego.yaml` 原子生成带注释的部署配置。也可显式指定：

```powershell
dotnet run --project src/AnimeGoNet.App -- --config E:\AnimeGoNet\animego.yaml
```

配置优先级为命令行/环境变量高于部署 YAML；WebUI 的安全私有覆盖低于被标记为
环境锁的字段。旧 `1.1.0`–`1.7.1` qBittorrent YAML 默认先保存原字节备份，再
原子升级为规范 1.7.1；旧 Transmission 配置保持原文件并 fail closed，必须先
人工迁移到 qBittorrent，绝不会静默改成默认实例。

部署 YAML 支持：

- `paths`：`data_path`、`download_path`、TV `save_path`、电影 `movie_save_path`
- `web`：监听 host/port、Access Key 和后台 worker 开关
- `downloaders.<id>`：qB WebUI 地址、用户名、密码、下载路径和启停
- `sources.<id>`：adapter、下载器绑定、文件策略、Torrent Host 白名单、
  category/tags、做种时间和 RSS 规则开关
- `metadata`：TMDB/Bangumi API 地址与代理、P4–P1 季度失败链、Bangumi 最终
  兜底、可信 offset、统一 AI 配置
- `torrent_fetch`、`schedule`、`data_update`

部署配置的详细边界见
[docs/DEPLOYMENT_CONFIGURATION.md](docs/DEPLOYMENT_CONFIGURATION.md)。

## Docker

正式版本的 amd64/arm64 镜像发布在 GHCR：

```powershell
docker pull ghcr.io/deqxj00/animegonet:1.0.0
```

也可使用 `latest`、主次版本标签，或按 Actions 输出的不可变 `sha256` digest 固定部署；
来源证明验证命令见 [CI / NativeAOT / Docker](docs/CI_CD.md)。

```powershell
$env:ANIMEGONET_ACCESS_KEY = '<strong-local-secret>'
# 可选：单独保护 WebUI；留空时裸 WebUI 可直接访问。
$env:ANIMEGONET_WEBUI_ACCESS_KEY = '<different-webui-secret>'
docker compose -f docker-compose.animegonet.yml up --build
```

官方容器固定：

- `data_path=/data`
- `download_path=/download/incomplete`
- `save_path=/download/anime`
- `movie_save_path=/download/movies`

Compose 将 `./data` 挂载到 `/data`，将同一个 `./download` 同时挂载给
AnimeGoNet 和两个 qB 容器；首次启动生成的 `/data/animego.yaml` 因此可持久化。
容器模式必须设置外部插件/API Access Key；WebUI AccessKey 独立且可选。

已有独立或远程 qBittorrent 时，使用
[`docker-compose.external-qbittorrent.yml`](docker-compose.external-qbittorrent.yml)，
并按[外部 qBittorrent 路径映射文档](docs/EXTERNAL_QBITTORRENT.md)把两端同一份共享
存储映射为完全相同的容器路径 `/download`。地址、用户名、密码和 Access Key 只
通过未跟踪环境变量或 secret 管理器传入。

## 测试

```powershell
dotnet test AnimeGoNet.slnx -c Release
```

本机 portable qBittorrent 测试默认只读；显式安全写入验收使用：

```powershell
./eng/qbittorrent-local-integration.ps1 -DispatchFixture
```

该测试只使用仓库内 5 字节、无可用 tracker 的固定 Torrent，并在结束时精确清理。
完整边界见
[docs/LOCAL_QBITTORRENT_INTEGRATION.md](docs/LOCAL_QBITTORRENT_INTEGRATION.md)。

NativeAOT 与容器 CI 覆盖 `win-x64`、`win-arm64`、`linux-x64`、
`linux-arm64`、`osx-arm64`，Docker 构建覆盖 amd64/arm64。

## 主要文档

- [用户迁移手册](docs/USER_MIGRATION.md)
- [运维、备份与恢复](docs/OPERATIONS.md)
- [外部 C# 插件安装与回滚](docs/PLUGIN_OPERATIONS.md)
- [架构与 NativeAOT 边界](docs/ARCHITECTURE.md)
- [统一输入与来源路由](docs/SOURCE_ROUTING.md)
- [TMDB/Bangumi/AI 元数据流程](docs/METADATA_RESOLUTION.md)
- [OpenAPI 契约](docs/API_OPENAPI.md)
- [WebUI](docs/WEB_UI.md)
- [外部 qBittorrent 部署](docs/EXTERNAL_QBITTORRENT.md)
- [CI / NativeAOT / Docker](docs/CI_CD.md)
- [首版实现完成审计](docs/IMPLEMENTATION_COMPLETION_AUDIT.md)
