# Bangumi 活动 Archive 缓存（2026-07-30）

## 上游与目标

上游 `internal/animego/anisource/bangumi.GetCache` 先查询本地
`bangumi_sub`，未命中后才访问 Bangumi API。AnimeGoNet 不读取旧
`bolt_sub.db`，而是复用已经校验并版本化导入 SQLite 的 AnimeGoNet Data
Archive。

## 运行语义

- `BangumiArchiveStore` 在一个 SQLite 读事务中读取
  `data_update_state.active_version`、Subject 和按 `sort_number` 排序的 Episode，
  防止版本切换时混合两个数据版本。
- Subject 命中活动版本即使用本地值。
- Episode 只有在本地集合非空且数量满足 Subject 的 `episode_count` 时才视为
  完整；缺失、不完整或 `episode_count=0` 且集合为空时访问在线 API，避免缓存
  长期遮蔽新播 Episode。
- Archive Episode 是数据生产阶段已清洗的普通 Episode，读取时明确投影为
  `type=0`；小数 Episode 原样保留，不会变成普通整数候选。
- 关系数据未包含在 Archive schema 中，继续调用在线
  `/v0/subjects/{bgmid}/subjects`。
- 活动版本切换和 rollback 每次读取都重新解析 SQLite 指针，无进程级陈旧缓存，
  不需要重启。
- 只有主程序创建的真实 Bangumi 客户端被包装；测试或显式注入的客户端保持原
  行为，方便故障注入。

## 验证

- `dotnet build AnimeGoNet.slnx -c Release --no-restore`：
  0 warning / 0 error。
- Data 聚焦 24/24：无活动版本、未知 Subject、Subject/Episode 映射、小数集、
  不完整集合、版本激活与 rollback。
- App 聚焦 47/47：默认 DI 确实使用 Archive、完整缓存零上游调用、缺失/不完整/
  零集未知回退、关系在线，以及现有 Bangumi、发布日期候选、季度回溯和自动元数据
  回归。
- `ANIMEGO_UPSTREAM_REPO=E:\WorkSpaceAI\AnimeGoNet\AnimeGo dotnet test
  AnimeGoNet.slnx -c Release --no-restore`：986/986 通过
  （Plugin 11、Core 289、Data 159、App 527）。
- win-x64 NativeAOT publish 通过，无 trim/AOT warning；
  `eng/smoke-native.ps1` 原生进程验证 schema 31、SQLite、WebUI、WebSocket 和
  安全来源凭据投影后正常清理。

测试使用临时 SQLite 与合成 Archive，不访问用户 Bangumi/TMDB key、qBittorrent、
TestSpace、Cookie 或 passkey。
