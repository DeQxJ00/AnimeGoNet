# 配置保存预览与 revision 备份验收

日期：2026-07-30

## 已实现行为

- `POST /api/v1/config/preview` 复用实际保存的 revision、环境锁、规范化和强类型校验，但不写入 `application.private.json`。
- 预览返回字段级 before/after 与 `hot_reload`/`restart` 生效方式。TMDB API Key 和 Read Token 只返回 `inherit/configured/cleared` 状态，响应不包含请求中的 secret。
- WebUI 必须先预览再确认保存；没有差异时确认按钮禁用，任一表单输入变化都会清除待提交对象、隐藏旧 diff 并要求重新预览。
- 覆盖已有私有配置或恢复部署默认前，旧文件先写入
  `data_path/backups/application.private.revision-{revision:D20}.json`。
  备份采用临时文件、flush、原子 move，Unix 权限为 `0600`。
- 已存在且内容相同的 revision 备份可幂等复用；同 revision 内容不同则
  fail closed，当前配置不发生变化。
- API 只回传 `backup_revision`，不回传备份内容或磁盘路径。首个私有
  revision 没有旧文件，因此该字段为 `null`。
- 原始部署 YAML 保持运维只读；Web 不展示或改写可能包含 secret、注释和
  格式信息的 YAML 原文。

## 自动验证

```text
npm run web:check
npm run web:build
Passed

dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigurationApiTests|FullyQualifiedName~ApplicationOverrideStoreTests"
Passed: 17, Failed: 0

dotnet test AnimeGoNet.slnx --no-restore
Plugin abstractions: 11 passed
Core: 264 passed
Data: 146 passed
App: 432 passed
Total: 853 passed, 0 failed
```

定向格式检查覆盖本次修改的全部 C# 文件并通过，`git diff --check` 通过。
全仓 formatter 另报告既有未修改的
`src/AnimeGoNet.Core/Scheduling/SixFieldCronExpression.cs` 换行风格；本提交
没有混入该无关格式改动。

## NativeAOT

`win-x64` Release 以 `PublishAot=true` 成功发布，没有 trim/AOT 警告。
`eng/smoke-native.ps1` 从发布后的原生可执行文件验证 `/ping`、schema v29、
NativeAOT 标志、SQLite compatibility API、安全导入拒绝和静态 WebUI。

## 浏览器验收

从本次 `win-x64` NativeAOT 产物启动一次性隔离实例，使用独立
data/download/save 目录且关闭后台 workers：

1. 打开“编辑应用配置”，把 TMDB 语言从 `zh-CN` 改为 `ja-JP`。
2. 点击“预览差异”，页面显示一项 `zh-CN → ja-JP`，效果为“重启生效”，
   “确认保存并备份”由禁用变为启用。
3. 不点击确认保存，把语言改回 `zh-CN`；旧 diff 立即隐藏，页面提示
   “配置已修改，请重新预览差异”，确认按钮恢复禁用。
4. 读取服务端与隔离文件系统，revision 仍为 0，
   `application.private.json` 不存在，备份数量为 0。
5. 浏览器 console 没有 warning/error。隔离 tab、原生进程和临时目录均已
   清理；未访问主预览实例或真实 qBittorrent。
