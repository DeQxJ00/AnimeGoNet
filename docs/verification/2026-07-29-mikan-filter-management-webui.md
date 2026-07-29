# Mikan 五级过滤管理与 WebUI 验证

## 范围

- 默认 `mikan` SourceProfile 的现代 GET/PUT/import/rollback/preview API。
- `Filiter0`～`Filiter4` 强类型编辑、顺序、启停与旧 JSON 往返。
- SQLite 快照列表、乐观并发冲突和不删除历史的回滚。
- 静态 TypeScript WebUI 的来源总开关、五档 CRUD、精确关键词数组编辑、警告和逐档预览。
- NativeAOT source-generated JSON 边界。

## 兼容行为

- F0 全部按保存顺序执行，但最终仅最后一条 F0 的结果生效。
- F1 `key_{mikanid}_{groupid}`、F2 `mikanid`、F3 `groupid` 只执行第一条命中的最高档。
- F0、选中的 F1/F2/F3、F4 以 AND 合并。
- 匹配使用区分大小写的普通子串；空字符串匹配全部标题。
- 导入导出保留旧拼写、顺序、重复值、空字符串和原始大小写。
- Web/导入/回滚采用 `expected_revision`；旧 API 的并发完整上传仍沿兼容契约执行。

## 自动验收

执行：

```powershell
npm run web:check
dotnet test AnimeGoNet.slnx --no-restore
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true /p:PublishAot=true
```

针对性测试覆盖：

- Core 引擎的五档、优先级、F0 顺序、大小写、Unicode、空值和预览 trace。
- Data store 的完整替换、重复/空关键词、快照倒序与上限、revision 冲突和回滚。
- App API 的 GET/PUT、旧 JSON 导入导出、服务端预览、稳定错误码、冲突和回滚。
- 静态页面包含五档编辑器、总开关、导入导出、快照回滚和可解释预览入口。

## 本次结果

- `npm run web:check` 与 `npm run web:build`：通过，TypeScript strict 无错误，生成的 `wwwroot/app.js` 与源码同步。
- `dotnet test AnimeGoNet.slnx --no-restore`：726 通过、0 失败、0 跳过（Plugin 11、Core 229、Data 116、App 370）。
- `win-x64` Release NativeAOT publish：通过，无 trim/AOT 警告。
- `eng/smoke-native.ps1 -ExpectedSchemaVersion 24`：通过 `/ping`、NativeAOT 状态、schema v24、SQLite 初始化、静态 WebUI、qB capability 和安全 ingest 拒绝检查；进程与隔离临时目录已回收。
