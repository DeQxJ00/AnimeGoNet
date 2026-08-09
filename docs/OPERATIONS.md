# AnimeGoNet 运维手册

本手册面向已经完成配置的实例。部署字段和容器路径分别以
[DEPLOYMENT_CONFIGURATION.md](DEPLOYMENT_CONFIGURATION.md)与
[EXTERNAL_QBITTORRENT.md](EXTERNAL_QBITTORRENT.md)为准。

## 启动与存活检查

开发/JIT 启动：

```powershell
dotnet run --project src/AnimeGoNet.App -- --config E:\AnimeGoNet\animego.yaml
```

发布包使用同样的 `--config` 参数。`GET /ping` 是公开存活检查；
`GET /api/v1/status` 是受 Access Key 保护的就绪/能力快照；生成的 API 契约位于
`GET /openapi/v1.json`。不要把 Access Key 放进 URL、shell history、日志或监控标签，优先
使用请求头和 secret manager。

正常停止应向进程发送 CTRL+C/SIGTERM，并给主程序至少 7 秒：业务关闭期限为 5 秒，额外
时间用于服务管理器调度。不要用强制结束作为日常停止方式；它会把未完成租约留给下次启动
恢复。

## 日常检查

- 状态页：数据库 schema、NativeAOT、legacy migration 阻断、后台 worker、来源/下载器、
  外部插件和计划任务状态。
- 下载页：qB 状态、wanted 进度、速度/ETA、做种门禁、媒体整理阶段和 stale 状态。
- 元数据页：TMDB 获取阶段、失败分类/原因、P4→P1 实际策略和待补全 TMDB。
- 日志：`data_path/logs/animego.log`，Information 以上，2 MiB 轮转，最多 14 份并清理
  超过 14 天的受管备份。
- qBittorrent：只处理匹配 AnimeGoNet category/tag/hash 的任务，不能批量清理私人任务。

## 备份

一致性备份建议停机执行：

1. 正常停止 AnimeGoNet，确认进程退出。
2. 复制完整 `data_path`，包括 `animegonet.db`、部署 YAML、`config/*.private.json`、
   `backups`、插件配置/数据和必要日志。
3. 分别备份 qBittorrent profile 与媒体库 sidecar；下载中的 payload 是否备份由存储策略决定。
4. 对备份生成 SHA-256 清单并验证可读性，保存程序版本/RID和备份时间。
5. 在隔离目录定期演练恢复，不要等故障时第一次验证。

SQLite 的 WAL 不是备份。停机后可选运行：

```powershell
sqlite3 E:\AnimeGoNet\data\animegonet.db "PRAGMA quick_check;"
```

结果必须为 `ok`。不要在运行中的生产库上复制单个 `.db` 文件，也不要手工编辑
`schema_migrations`。

## 升级与恢复

升级前阅读 release notes，确认目标 RID，停止服务并做完整备份。替换发布目录时保留
`data_path` 在外部持久位置；启动新版后检查 `/ping`、受保护状态、schema、下载器路径和
插件 RID。先用合成任务验收，再恢复自动 RSS。

数据库 migration 只向前。若新版启动后需要退回旧二进制，必须同时恢复升级前完整
`data_path`；不能让旧程序打开新版 schema。恢复时还要使用同一时间点的 qB profile 和被
移动/删除的媒体文件，否则数据库、下载器与文件系统会不一致。

## 常见故障

| 现象 | 先检查 | 安全动作 |
|---|---|---|
| 启动时 legacy downloader 阻断 | 状态中的 migration diagnostic | 人工改成 qBittorrent；不要删除诊断或伪造默认实例 |
| qB 离线/stale | 实例 URL、登录、circuit、路径探测 | 修复连接后等健康探测恢复；不要重建已有任务 |
| 文件路径不一致 | AnimeGoNet 与 qB 的共享目录映射 | 修正为同一绝对/容器路径；不要复制一份同名假目录 |
| TMDB/Bangumi 网络失败 | failure category、代理/API 地址、超时 | 保持任务待重试；网络错误不能启用完全失败兜底 |
| metadata semantic no-match | P4/P3/AI/P2/P1 审计 | 人工覆盖优先；只有明确满足条件才使用 Bangumi 最终兜底 |
| 整理中断 | 持久化 stage、file operation、cleanup | 让 worker 从 pending-only 恢复；不要手工标记完成 |
| 插件自动禁用 | 稳定错误码、RID、manifest、stderr | 修复后显式 reset；不要删除 private config 绕过退避 |

四类删除（业务记录、下载器任务、源文件、媒体文件）彼此独立，必须先预览并提交同一
指纹确认。qB 清理固定使用 `deleteFiles=false`；文件只由受根目录约束的整理/删除执行器
处理。手工删除文件会破坏重试和审计，应只作为已经备份后的最后恢复手段。

## Docker 状态

仓库已经生成双架构 NativeAOT Dockerfile、Compose、非 root/只读 rootfs/healthcheck/
SIGTERM smoke、双 qB 集成脚本和 GitHub Actions。按项目所有者要求，这些功能文件保留，
但当前状态明确为“未验证”，不把未执行的 Docker/远端 runner 结果写成成功。正式使用前
由部署者自行构建并验证；固定容器路径仍是 `/data`、`/download/incomplete`、
`/download/anime`。
