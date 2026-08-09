# Ubuntu 24.04 Docker CT 显式测试

这套测试不会由默认单元测试或 CI 自动执行。凭据不得写入参数、配置文件、报告或 Git。

入口：`eng/docker-ubuntu-ct-integration.ps1`

默认目标为 `root@192.168.1.164`，使用 PuTTY `plink/pscp -batch` 已配置的认证。流程会验证 Ubuntu 24.04、x86_64、Docker 和 Compose，使用 `git archive HEAD` 上传已提交源码，在唯一 `/var/tmp/animegonet-docker-audit-<run>` 目录中构建 NativeAOT 镜像，然后执行容器 API/SQLite/挂载测试及 qBittorrent Compose 链路。为避免上游 `latest` 漂移，CT 夹具默认固定 qBittorrent `5.1.4`；可通过 `-QbittorrentImage` 显式覆盖，实际值会写入报告。

测试不会执行全局 Docker prune，只删除本次唯一标签的镜像及唯一远端目录。脱敏报告写入 `TestSpace/animegonet_data/docker-ubuntu-ct/`。

```powershell
& .\eng\docker-ubuntu-ct-integration.ps1
```
