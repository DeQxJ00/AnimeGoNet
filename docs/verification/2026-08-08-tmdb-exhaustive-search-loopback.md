# 2026-08-08 TMDB 多轮 Series/Season 搜索验收

## 目的

关闭“某个搜索词选出相似 Series，但其季度失败后是否提前切换名字”的歧义。成功条件始终是经官方端点验证的完整 `TMDB Series + ordinary Season`，不是仅搜索到一个 Series。

## 生产流程证据

- `TmdbSeriesResolver`：每个输入标题执行原始词和四步上游后缀清理；同一响应内按精确名、UTF-8 byte 相似度和稳定响应顺序遍历全部合格 Series；candidate validator 返回 false 后继续下一 Series 和下一搜索词。
- `TmdbSeriesSeasonResolver`：Bangumi `name` 在 `name_cn` 之前；每个候选取得 identity-matched details、按开播日期选普通季度、再调用官方 Season endpoint；只缓存已经检查过的 Series ID，不因同一 Series 跨名字重复请求详情。
- `BangumiSeasonBacktraceResolver`：每个前作作为新的联合搜索节点，不锁定当前 tmdbid；`bgmid`、前作名字和开播日期共同决定下一次完整验证。

## 受控 HTTP fixture

`TmdbSeriesSeasonLoopbackTests` 使用随机 loopback Kestrel 和生产 `TmdbClient`：

1. 日文原始标题返回 Series 10，但其日期季度不匹配；
2. 清理后的日文标题返回零结果；
3. 中文原始标题同一响应返回 Series 20/30；Series 20 季度失败；
4. Series 30 的 S04 日期匹配，并由 `/3/tv/30/season/4` 验证成功。

断言实际搜索顺序恰为日文原始词、日文清理词、中文原始词；details 顺序为 10、20、30；只有 `(30,4)` 请求 Season。请求同时验证 discover 的 sort/language/timezone/genre/API key 参数。fixture 不访问互联网或真实凭据。

既有 `BangumiSeasonBacktraceLoopbackTests` 另用实际 Bangumi/TMDB clients 验证 Bangumi 503、TMDB 429、二级前作、不同 tmdbid 恢复和最终 Season endpoint。

## 结果

- 定向 Resolver + 两个 loopback suites：7/7 passed。
- 最终 Release build：0 warnings / 0 errors；完整 .NET：1429/1429。
- 生产源码与上一发布模块相同的最终 win-x64 NativeAOT 程序再次通过 first-start 与正式 AI metadata worker smoke；随机 loopback 临时进程和目录均已回收。
