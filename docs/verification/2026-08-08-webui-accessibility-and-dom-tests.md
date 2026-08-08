# WebUI 响应式、状态与 DOM 验证（2026-08-08）

## 范围

- 新增无浏览器运行时依赖的 `ui-state.ts`，统一主异步区域的
  `loading / ready / empty / error` 状态和 ARIA 语义。
- 增加 skip link、全局可见焦点、44px 常规控件目标、reduced-motion、移动端标题/
  对话框/分页布局，并补齐 RSS 批次标题和旧 JSON 编辑器标签。
- 测试依赖 `linkedom@0.18.13` 仅在 Node 开发/CI 环境中解析真实 DOM，不进入发布的
  WebUI 或 .NET 运行时。

## 自动验证

```text
npm run web:test
13 passed, 0 failed

dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj \
  --filter FullyQualifiedName~StaticWebUiTests
106 passed, 0 failed

ANIMEGO_UPSTREAM_REPO=E:\\WorkSpaceAI\\AnimeGoNet\\AnimeGo \
  dotnet test AnimeGoNet.slnx --no-restore
1375 passed, 0 failed

dotnet restore src/AnimeGoNet.App/AnimeGoNet.App.csproj -r win-x64 \
  -p:PublishAot=true
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishAot=true --no-restore
eng/smoke-native.ps1 -Executable <publish>/AnimeGoNet.App.exe
Native smoke passed (first-start)
```

DOM 门禁覆盖：唯一 ID；主 section 和 dialog 的可访问名称；所有静态 input/select/
textarea/button 的名称；禁止正 tabindex；初始异步状态的 live/busy/status/atomic 契约；
错误 alert；安全 `textContent`；skip link、focus-visible、reduced-motion、移动断点和最小
控件高度。Kestrel 测试同时直接读取 `ui-state.js`、HTML 与 CSS 标记。

## 本机运行验收

使用独立 `.artifacts/webui-accessibility-smoke` 三目录启动 JIT Kestrel，不读取或写入
正式数据，也不连接用户 qBittorrent。应用内浏览器结果：

- `390 × 844`：无横向溢出，hero 单列、section heading 纵向排列；
- `1280 × 800`：无横向溢出，hero 双列、模块三列、section heading 横向排列；
- 11 个带状态的异步区域均从 loading 收敛为 ready 或 empty，`aria-busy=false`；
- 未发现无名称控件，浏览器 console warning/error 为 0。

发布镜像 Playwright 测试仍是独立发布门禁，不能由本次本机 JIT/DOM 验收替代。
