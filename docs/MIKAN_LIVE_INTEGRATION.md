# Mikan 本机真实链路验收

该验收只用于显式本机 integration test，不属于默认单元测试或 CI。测试 CSV、qB 二进制/profile、下载内容、Cookie、WebUI 凭据、passkey、TMDB/OpenAI key 都是私有输入，禁止提交 Git。

## 隔离目录

- 沙箱：`E:\WorkSpaceAI\AnimeGoNet\TestSpace`
- qB 下载目录：`TestSpace\download_temp`
- AnimeGoNet 整理目录：`TestSpace\jellyfin_data`
- 临时 SQLite/审计：`TestSpace\animegonet_data\mikan-live-audit\run-*`
- 输入期望：`E:\WorkSpaceAI\AnimeGoNet\测试数据.csv`

脚本仅处理带 `animegonet-mikan-audit-<run-id>` category/tag 的测试任务，不接管或清理用户已有 qB 任务。清理 qB 测试任务固定使用 `deleteFiles=false`；整理后的媒体不会由脚本删除。

## 凭据与启动

凭据只能设置为当前 PowerShell 进程环境变量，不作为参数传递，也不写入报告：

```powershell
$env:ANIMEGONET_QBIT_USERNAME = '<local-user>'
$env:ANIMEGONET_QBIT_PASSWORD = '<local-password>'
$env:ANIMEGONET_TMDB_API_KEY = '<test-key>'
$env:ANIMEGONET_AI_API_KEY = '<test-key>'
```

先启动隔离 qB，确认 Web API 可登录且默认保存路径指向 `TestSpace\download_temp`。只做元数据、投递与筛选审计，不恢复真实下载：

```powershell
.\eng\mikan-live-audit.ps1 -StartRow 2 -MaxCases 29
```

明确允许真实下载和 move 整理时：

```powershell
.\eng\mikan-live-audit.ps1 -RealDownload -StartRow 2 -MaxCases 29 -DownloadTimeoutMinutes 180
```

可用 `StartRow`/`MaxCases` 分批续跑。默认地址是本地反向代理，可通过脚本的 `MikanBaseUrl`、`TmdbBaseUrl`、`BangumiBaseUrl`、`AiBaseUrl`、`TmdbMcpUrl`、`BangumiMcpUrl` 参数替换；应用本身对应地址同样可在配置/WebUI修改。

## 报告与验收

每次运行会增量写入 `mikan-live-audit.json`。每条记录包括 CSV 行号、标题、URL 单向指纹、筛选/投递/Series/Season/Episode 的完整 attempt 顺序、最终映射、qB hash、下载与整理状态、AI 是否调用、模型及 token/HTTP/tool-call 用量。报告不含 Torrent URL、passkey、Cookie、API key、qB 用户名/密码或媒体绝对路径。

验收要求：CSV 预期 TMDB Series/Season/Episode 与实际逐文件结果一致；真实下载模式还必须完成 qB 下载、下载准备、move、NFO/sidecar、completion 和 qB `deleteFiles=false` 清理。AI 为可选且任务级最多一次，若调用必须在报告中有用量。

异常中止后，只能按本次唯一 audit category/tag 精确删除测试任务，并保持 `deleteFiles=false`；不得按 tracker、标题片段或全部任务批量清理。下载块与已经整理的媒体由测试人员确认后手工处置。
