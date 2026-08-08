# 外部 qBittorrent 部署与路径映射

`docker-compose.external-qbittorrent.yml` 只启动 AnimeGoNet，连接已有的两个
qBittorrent WebUI 实例。`bt` 可绑定 Mikan，`pt` 可绑定 U2/TTG；来源和下载器绑定
仍可在 WebUI 中修改。只使用一个实例时，可让所有来源绑定 `bt`，并从自己的部署
文件中删除 `pt` 的六个 `downloaders__pt__*` 环境项。

## 不可省略的路径契约

AnimeGoNet 不猜测或替换跨容器路径。AnimeGoNet 与每个 qBittorrent 必须看到同一
份共享存储，并且容器内路径必须完全相同：

| 用途 | AnimeGoNet 容器 | 外部 qBittorrent 容器 |
|---|---|---|
| 共享父目录 | `/download` | `/download` |
| `bt` 下载目录 | `/download/incomplete/bt` | `/download/incomplete/bt` |
| `pt` 下载目录 | `/download/incomplete/pt` | `/download/incomplete/pt` |
| 整理后媒体库 | `/download/anime` | 无需由 qB 写入，但共享挂载必须能看到 |

在同一 Docker 主机上，三个容器应把同一个宿主目录绑定到 `/download`。外部 qB
Compose 中的关键部分应为：

```yaml
services:
  qbittorrent-bt:
    volumes:
      - type: bind
        source: ${ANIMEGONET_SHARED_DOWNLOAD_ROOT:?required}
        target: /download
  qbittorrent-pt:
    volumes:
      - type: bind
        source: ${ANIMEGONET_SHARED_DOWNLOAD_ROOT:?required}
        target: /download
```

qB WebUI 中的默认保存路径也要分别设置为
`/download/incomplete/bt` 和 `/download/incomplete/pt`。AnimeGoNet 添加 Torrent
时会传入相同的实例下载路径；连接测试会比较 qB 默认路径，尽早发现配置漂移。

当 qB 位于另一台主机时，两台主机必须先通过 NFS、SMB 或其他共享文件系统挂载同
一份数据。宿主机上的挂载源可以不同，但进入各自容器后的目标都必须是
`/download`。例如 AnimeGoNet 看到 `/mnt/anime-share`、远程 qB 主机看到
`/srv/anime-share` 是允许的，只要两者是同一份存储且都绑定到容器内 `/download`。
不能让 qB 返回 `/downloads`、`/srv/torrents` 或仅存在于远端宿主机的路径。

## 启动

下面的值仅在当前终端设置。也可以放入 Compose 自动读取的 `.env`，但仓库已忽略
`.env` 和 `.env.*`；不要提交 WebUI 密码、Cookie、Torrent URL、passkey 或 Access
Key。`QBITTORRENT_*_URL` 必须是 AnimeGoNet 容器内可访问的 HTTP(S) 地址，不能
使用外部 qB 容器自己的 `127.0.0.1`。

```powershell
$env:ANIMEGONET_ACCESS_KEY = '<strong-random-secret>'
$env:ANIMEGONET_DATA_ROOT = 'D:\AnimeGoNet\data'
$env:ANIMEGONET_SHARED_DOWNLOAD_ROOT = 'D:\AnimeGoNet\download'

$env:QBITTORRENT_BT_URL = 'http://qbittorrent-bt.example.internal:8080/'
$env:QBITTORRENT_BT_USERNAME = '<bt-user>'
$env:QBITTORRENT_BT_PASSWORD = '<bt-password>'
$env:QBITTORRENT_PT_URL = 'http://qbittorrent-pt.example.internal:8080/'
$env:QBITTORRENT_PT_USERNAME = '<pt-user>'
$env:QBITTORRENT_PT_PASSWORD = '<pt-password>'

docker compose -f docker-compose.external-qbittorrent.yml config --quiet
docker compose -f docker-compose.external-qbittorrent.yml up -d --build
```

默认只在宿主机 `127.0.0.1:7991` 暴露 WebUI。确需局域网访问时再显式设置
`ANIMEGONET_BIND_ADDRESS`，并同时使用防火墙和强 Access Key。qB WebUI 本身也应
限制到受信网络，不应直接暴露到公网。

## 启动后验收

先在 WebUI 的下载器页面分别执行“连接测试”和“路径探测”，也可以调用：

```powershell
$headers = @{ 'X-AnimeGo-Access-Key' = $env:ANIMEGONET_ACCESS_KEY }
Invoke-RestMethod -Method Post -Headers $headers `
  http://127.0.0.1:7991/api/v1/downloaders/bt/test
Invoke-RestMethod -Method Post -Headers $headers `
  http://127.0.0.1:7991/api/v1/downloaders/bt/path-probe
Invoke-RestMethod -Method Post -Headers $headers `
  http://127.0.0.1:7991/api/v1/downloaders/pt/test
Invoke-RestMethod -Method Post -Headers $headers `
  http://127.0.0.1:7991/api/v1/downloaders/pt/path-probe
```

连接测试验证用户名/密码登录、任务列表、qB 版本和默认保存路径。路径探测不创建
Torrent；它确认实例下载目录与全局媒体目录都可见，并创建一个随机隐藏临时文件做
硬链接能力测试后立即清理。全部通过后，先用明确的测试 Torrent 做显式验收；任务
应使用可识别的 AnimeGoNet category/tag，并在确认路由后精确清理，禁止拿私人任务
做试验。

若 `test` 失败，先检查容器 DNS/路由、WebUI 监听地址和用户名/密码。若
`path-probe` 返回 `directory_missing`、`permission_denied` 或
`hard_link_unavailable`，修正共享挂载、UID/GID 或文件系统能力；不要用路径字符串
替换来掩盖两端实际看见的目录不一致。
