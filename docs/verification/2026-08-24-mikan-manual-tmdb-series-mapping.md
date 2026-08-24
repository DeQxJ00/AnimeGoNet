# Mikan 人工 TMDB Series 映射验收

## 业务语义

- 当 AI 对单文件 Other 任务提出与当前结果不同、且已经通过 TMDB Series / Season / Episode 三段验证的候选时，必须由用户人工同意。
- 同意后按精确的 `mikanid + groupid` 保存目标 `tmdbid + season`；不同字幕组、缺少任一 ID、Movie 以及强制 Other 重新适配均不复用。
- 后续同一组合先读取人工映射并再次验证 TMDB Series / Season，再进入正式 Episode 匹配；不复用某一集的 Episode 结论，也不跳过最终 Episode 验证。
- 删除映射仅影响未来任务，不改写已整理文件、完成记录或历史审核。

## WebUI 验收

入口：`输入源 → Mikan → 人工 TMDB 映射`。

页面逐条显示 Mikan ID、字幕组 ID、原预期 TMDB Series、人工接受的 TMDB Series / Season、来源审核任务和时间，并支持删除单条映射。

## 自动化验收

```powershell
dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj -c Release --filter "FullyQualifiedName~MikanManualSeriesMappingStoreTests|FullyQualifiedName~AiSeriesChangeProposalIsShown"
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --filter "FullyQualifiedName~ApprovedMikanSeriesMappingBypassesTitleSearchForExactPair|FullyQualifiedName~MikanTrustedOffsetApiTests"
```

覆盖精确组合隔离、更新/删除、审核提议固化 Mikan 身份、API 列表/删除，以及命中映射后不再执行标题搜索。
