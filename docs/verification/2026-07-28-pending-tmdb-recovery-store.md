# 待补全 TMDB 恢复事务（2026-07-28）

## 范围

- schema v20 为 `fallback_completion_records` 增加 `pending`、`resolved`、`duplicate_after_resolution` 状态、恢复来源、恢复时间和规范 completion 外键。
- `completion_aliases` 保存独立的 fallback scope kind/key，且数据库约束禁止残缺 alias。
- `PendingTmdbRecoveryStore` 接收已经过调用方验证的 TMDB Series/Season/Episode，并在单个 SQLite 事务内完成正式作品、季度、Episode、completion、alias 和任务文件投影。
- 同一批次按原 fallback 完成时间排序；同一规范 Episode 只创建一个 completion，其他记录保留并标为 `DuplicateAfterResolution`。
- 部分恢复继续保留 `tmdbid=0` 待补全作品；最后一条恢复后删除该临时投影。
- 事务不创建下载任务、不移动文件、不删除冲突媒体。删除规范 completion 时，SQLite 外键级联清除恢复记录和 fallback alias。

## 验收

执行：

```text
dotnet test tests\AnimeGoNet.Data.Tests\AnimeGoNet.Data.Tests.csproj --no-restore
```

结果：

- Data：89/89 通过；
- 全解决方案：533/533 通过（Core 199、Data 89、App 245）；
- `dotnet publish src\AnimeGoNet.App\AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true /p:PublishAot=true --no-restore`：通过。

测试覆盖：

- schema 19 → 20 保留旧 fallback 数据并赋予 `pending` 默认状态；
- 两条 fallback 收敛到同一 Episode，仅产生一个规范 completion，另一条标记解析后重复；
- 已有规范 completion 优先，既有媒体路径不被覆盖；
- 分批恢复时待补全视图持续存在，最后一条完成后退出；
- 缺少 fallback ID 或 TMDB Episode ID 冲突时整个事务回滚；
- 目标规范 Episode 仍有活动 claim 时拒绝恢复，不改变在途任务或创建 completion；
- 规范 completion 删除后 alias 与已恢复 fallback 记录同步失效。

## 后续边界

本增量只实现经过验证后的数据库提交点。人工恢复 API/WebUI、调用 TMDB 在线验证、`tvshow.nfo` 的 `tmdbid=0` 原子替换，以及对冲突文件的人工处理入口仍需单独实现和验收。
