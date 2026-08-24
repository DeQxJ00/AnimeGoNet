# Mikan 字幕组对照与 Episode 展示验收

## 行为

- `groupid` 只读取已经由 Mikan Episode/RSS 解析确认的任务字段，不从 PublishGroup 页面反推身份。
- 首次发现新 groupid 后，后台通过对应来源配置和全局网络策略请求 `{mikan_base_url}/Home/PublishGroup/{groupid}`。
- 页面中的“名称作品年表”解析成功后去掉“作品年表”并长期保存；失败每 6 小时重试。
- 人工编辑名称后自动刷新不能覆盖；只有用户点击“从 Mikan 重新获取”才恢复自动来源。
- 动画库 Episode 若能关联到已确认 groupid，则显示对照表名称和 groupid；外部补录或无来源证据时保持空白。

## WebUI

入口：`输入源 → Mikan → 字幕组对照`。逐项显示 groupid、名称来源、获取状态、更新时间和稳定失败码，支持编辑名称及显式重新获取。

## 自动化验收

```powershell
dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj -c Release --filter "FullyQualifiedName~MikanPublishGroupStoreTests"
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --filter "FullyQualifiedName~MikanPublishGroupApiTests|FullyQualifiedName~SeasonDetailReturnsOfficialEpisodeGridWithoutLocalMediaPaths"
```
