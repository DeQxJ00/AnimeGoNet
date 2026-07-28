# 待补全 TMDB 人工恢复 API（2026-07-28）

## 接口

- `GET /api/v1/metadata/pending-tmdb/{bgmid}` 的 `recovery_candidates` 返回：
  - 随机 fallback record ID；
  - 来源、来源 Episode、用户可理解的去重边界和完成时间。
- 响应不返回内部 scope key、媒体绝对路径、Torrent URL、Cookie、passkey 或 TMDB 凭据。
- `POST /api/v1/metadata/pending-tmdb/{bgmid}/recover` 接收 TMDB Series ID，以及每个 fallback record 对应的 Season/Episode。

## 验证与提交

1. 请求先验证正整数和不重复的 fallback record ID。
2. 通过 `ITmdbClient` 获取并核对真实 Series。
3. 每个不同 Season 只验证一次。
4. 每个不同 Episode 只验证一次，并核对 Series/Season/Episode 父子身份。
5. 全部通过后才以 `manual` 来源调用 schema v20 恢复事务。
6. 目标有活动规范 claim、候选已变化或数据库身份冲突时返回冲突，不创建 completion。
7. `DuplicateAfterResolution` 作为明确结果返回，不触发下载或文件删除。

## 验收

定向测试：

```text
dotnet test tests\AnimeGoNet.App.Tests\AnimeGoNet.App.Tests.csproj --no-restore --filter "FullyQualifiedName~PendingTmdb"
```

结果：4/4 通过。

覆盖安全候选投影、真实 TMDB 调用参数、成功事务提交、响应脱敏、完成后退出待补全列表，以及 Episode 无法验证时数据库零变更。

全解决方案 535/535 通过（Core 199、Data 89、App 247）；win-x64 NativeAOT 发布通过。

## 剩余边界

WebUI 人工映射表单和 `tvshow.nfo` 的持久化可恢复重写仍为后续模块。NFO 不直接塞入本端点，是为了避免进程崩溃时形成无法判断的文件/数据库半提交。
