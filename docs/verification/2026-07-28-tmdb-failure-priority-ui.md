# TMDB 季度失败优先级 WebUI 验证

## 显示契约

- 生效配置卡与编辑器都按 P4 `TMDBFailSkip` → P3 `TMDBFailBacktrace` → P2 `TMDBFailUseTitleSeason` → P1 `TMDBFailUseFirstSeason` 显示确定性优先级。
- P3 明确标注仅有 `bgmid` 时可执行 Backtrace。
- P2 明确标注为前面策略全部失败后的本地标题解析，只读取统一导入任务 `title`；解析出的季度不验证 TMDB Season，解析不到继续 P1。
- P1 明确标注为前序策略全部失败后，勾选即使用本地 `S01`，不验证 TMDB Season。
- P4、P3 与独立季度 AI 的远端结果仍必须验证。
- Bangumi 完全兜底标注为“一般不启用”；说明限定为 TMDB 完全失败、有 `bgmid`、固定 `S01` 且不提供有效 TMDB ID，同时明确内部 `tmdbid=0` 逻辑不变。
- AI 季度匹配显示在 P3 与 P2 之间的独立分支，明确默认关闭且不占确定性优先级。
- EP-AI 明确标记为独立后置阶段。
- 每项同时显示名称、说明和启用状态，不仅依赖颜色表达状态。
- Skip 的说明明确命中后终止低优先级 fallback；成功立即停止的规则显示在链顶部。

## 验收

- `ConfigurationApiTests` 4/4 通过，静态 WebUI 测试断言 P4/P3/独立 AI/P2/P1 的语义标记及说明。
- TypeScript 严格检查、静态资源构建和 JavaScript 语法检查通过。
- 全量解决方案 566/566 通过（Core 205、Data 99、App 262）。
- win-x64 NativeAOT 发布输出包含 `Generating native code`，隔离目录 smoke 通过。
- 本地 Minimal API 页面使用真实浏览器检查配置卡与编辑弹窗的顺序、状态文字；390px viewport 下 fieldset 与所有优先级项均无横向溢出。
