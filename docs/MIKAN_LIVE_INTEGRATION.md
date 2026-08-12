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

批量验证完整整理链路时，推荐使用合成载荷模式。它仍真实执行 Mikan Torrent 获取、
规则筛选、SQLite 去重、qB 投递和文件清单校验、Bangumi/TMDB/可选 AI 匹配；下载准备
完成后不等待公网 BT，而是在本次 `run-*` 下的隔离下载目录按 Torrent 清单创建相同
长度的测试文件，再执行正式 move、重命名、NFO/sidecar、completion 和 qB 清理流程：

```powershell
.\eng\mikan-live-audit.ps1 -SyntheticPayload -StartRow 2 -MaxCases 29
```

`-SyntheticPayload` 与 `-RealDownload` 互斥。合成文件和整理结果都位于本次审计目录，
不会写入共享 `download_temp` 或 `jellyfin_data`。报告使用
`payload_mode=synthetic_file` 和 `payload=SyntheticFile` 明确标记，不计作真实 BT 下载。

真实下载默认应用 `-ZeroProgressSkipMinutes 5`：任务进入 qB 下载阶段后，若满
5 分钟仍低于 1%（即整数百分比仍显示 0%），该行记录
`download=SkippedZeroProgress`，随后以 `deleteFiles=false` 精确移除本次任务并继续
下一行。只要达到 1% 就继续按 `DownloadTimeoutMinutes` 等待；该跳过只表示元数据
预期已通过、真实载荷不可验收，不能算作真实下载完成。可显式修改等待分钟数，但
不能关闭总下载超时。

可用 `StartRow`/`MaxCases` 分批续跑；输入允许 1–100 条，默认仍执行原始 29 条基准。
`ep预期序号` 支持普通整数、`13-14` 形式的闭区间、明确表示“不应生成普通
TMDB Episode”的 `none`，以及只断言任务必须进入统一 AI 并正常收口、但不预设
模型输出的 `ai`。默认地址是本地反向代理，可通过脚本的 `MikanBaseUrl`、
`TmdbBaseUrl`、`BangumiBaseUrl`、`AiBaseUrl`、`TmdbMcpUrl`、`BangumiMcpUrl` 参数替换；
应用本身对应地址同样可在配置/WebUI修改。

脚本默认使用 `Release`；如果同目录 Release 主程序正在运行并锁定输出，可显式传入
`-Configuration Debug`，无需关闭正在展示的 WebUI。该选项只改变测试程序集的构建配置，
不改变业务流程或报告内容。

## 报告与验收

每次运行会增量写入 `mikan-live-audit.json`。每条记录包括 CSV 行号、标题、URL 单向指纹、筛选/投递/Series/Season/Episode 的完整 attempt 顺序、最终映射、qB hash、下载与整理状态、AI 是否调用、模型及 token/HTTP/tool-call 用量。报告不含 Torrent URL、passkey、Cookie、API key、qB 用户名/密码或媒体绝对路径。

验收要求：CSV 预期 TMDB Series/Season/Episode 与实际逐文件结果一致；合成载荷模式必须完成 qB 文件清单校验、下载准备、测试文件创建、move、NFO/sidecar、completion 和 qB `deleteFiles=false` 清理；真实下载模式在此基础上还必须完成真正的 BT 下载。`SyntheticFile` 和 `SkippedZeroProgress` 均不计入真实下载完成数，报告汇总时必须单列。AI 为可选且任务级最多一次，若调用必须在报告中有用量。

异常中止后，只能按本次唯一 audit category/tag 精确删除测试任务，并保持 `deleteFiles=false`；不得按 tracker、标题片段或全部任务批量清理。下载块与已经整理的媒体由测试人员确认后手工处置。
