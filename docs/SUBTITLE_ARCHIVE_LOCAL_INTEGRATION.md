# 字幕压缩包真实夹具测试

`subtitles-test/` 是本机专用的字幕压缩包夹具目录，整个目录已加入 `.gitignore`。
其中可以放 ZIP、RAR、7z、TAR 以及 GZ/BZ2/XZ 包装的 TAR；压缩包内容、字幕、媒体文件和测试产物都不会进入 Git。

## 运行

在仓库根目录执行：

```powershell
$env:ANIMEGONET_SUBTITLE_ARCHIVE_INTEGRATION = "1"
dotnet test tests\AnimeGoNet.LocalIntegration.Tests\AnimeGoNet.LocalIntegration.Tests.csproj `
  --filter "FullyQualifiedName~SubtitleArchiveFixtureTests"
Remove-Item Env:ANIMEGONET_SUBTITLE_ARCHIVE_INTEGRATION
```

测试会逐个打开 `subtitles-test` 目录及其子目录下的已支持压缩包，用真实的
`SubtitleArchiveImportService` 解压、路径安全检查和集数解析，并要求每个压缩包至少得到一个字幕候选。
随后会重新读取暂存会话、确认导入全部候选，并验证 EP/Extras 计数、目标文件实际落盘数量和会话清理。
使用详细控制台日志时会逐条输出候选相对路径、大小、解析 EP 和集数范围，便于定位真实文件名兼容问题。
每个夹具都使用独立临时 `data_path`，测试结束后清理临时目录，不会写入正式动画库，也不会连接 TMDB、AI、qBittorrent 或外部网络。

未设置 `ANIMEGONET_SUBTITLE_ARCHIVE_INTEGRATION=1` 时，测试会直接拒绝运行并提示显式开关；目录不存在或目录中没有支持格式也会给出明确断言。因此默认 CI 和普通测试不会依赖本机私有夹具（该 LocalIntegration 项目不在默认解决方案门禁中）。

可用格式与应用上传限制仍以 WebUI/API 文档为准：单个上传不超过 512 MiB，单个字幕不超过 128 MiB，解压字幕总量不超过 512 MiB，最多 500 个字幕候选。

## 字幕 AI 匹配边界

字幕 AI 使用独立的 `subtitle-v2` 提示词。输入文件标识为压缩包内相对路径，模型必须保持原顺序和原文；已确认的 TMDB Series ID 与 Season 不允许被模型修改。简体、繁体、双语和不同格式字幕可以共同映射到同一个 Episode。

模型结果返回后，主程序还会验证 Series、Season 和每个不同的 Episode。任何相对路径变更、Series/Season 变更、汇总状态不一致或 TMDB Episode 不存在都会拒绝整次建议，不会自动写入导入表单。AI 仅回填候选，用户仍需在 WebUI 中复核并确认导入。

字幕 AI 单元测试使用 fake 模型和 fake TMDB，不调用真实外部服务；真实压缩包夹具测试也不会消耗 AI Token。

## 清理与 Git 检查

测试不会删除 `subtitles-test` 中的源文件。确认目录被忽略：

```powershell
git check-ignore -v subtitles-test\<任意夹具文件>
git status --short --ignored subtitles-test
```

输出应显示命中根目录 `/subtitles-test/` 规则；不要使用 `git add -f` 添加该目录。
