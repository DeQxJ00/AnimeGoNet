# Ubuntu 24.04 Docker CT 显式测试

这套测试不会由默认单元测试或 CI 自动执行。凭据不得写入参数、配置文件、报告或 Git。

入口：`eng/docker-ubuntu-ct-integration.ps1`

默认目标为 `root@192.168.1.164`，使用 PuTTY `plink/pscp -batch` 已配置的认证。流程会验证 Ubuntu 24.04、x86_64、Docker 和 Compose，使用 `git archive HEAD` 上传已提交源码，在唯一 `/var/tmp/animegonet-docker-audit-<run>` 目录中构建 NativeAOT 镜像，然后执行容器 API/SQLite/挂载测试及 qBittorrent Compose 链路。为避免上游 `latest` 漂移，CT 夹具默认固定 qBittorrent `5.1.4`；可通过 `-QbittorrentImage` 显式覆盖，实际值会写入报告。

测试不会执行全局 Docker prune，只删除本次唯一标签的镜像及唯一远端目录。脱敏报告写入 `TestSpace/animegonet_data/docker-ubuntu-ct/`。

```powershell
& .\eng\docker-ubuntu-ct-integration.ps1
```

加入 `-FullChainWebUi` 会在完整链路成功后运行发布镜像 WebUI 的 Chromium 验收：

```powershell
& .\eng\docker-ubuntu-ct-integration.ps1 -FullChainWebUi
```

CT 没有 Node/npm 时，脚本使用固定的官方
`mcr.microsoft.com/playwright:v1.62.0-noble` 容器，在 host network 上执行仓库的
Playwright 测试。源码只读挂载，`node_modules`、测试产物和 `/tmp` 都位于临时 tmpfs；
若该 Playwright 镜像不是测试前已有，退出时会精确删除。脚本在完整链路前重置本次
`mktemp` 根内的三个 AnimeGoNet SQLite 文件，并在容器重启后重新读取随机宿主端口，
不会删除正式数据或用户 TestSpace 数据。

2026-08-11 的 Ubuntu x86_64 实跑结果见
`docs/verification/2026-08-11-ubuntu-ct-docker-validation.md`。
