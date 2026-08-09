# WebUI 一级/二级工作区导航（2026-08-09）

## 信息架构

- 总览：运行状态、管理入口；
- 动画库：作品与季度、待补全 TMDB；
- 任务中心：下载任务、匹配与整理、详细日志；
- Mikan 自动化：导入、人工规则、可信 Offset、候选规则、五级过滤；
- 连接与配置：应用配置、输入源、下载器、外部插件；
- 系统：数据更新、缓存管理。

## 行为边界

所有顶层区域仍存在于同一原生 HTML/TypeScript 应用，但只有当前
`workspace/subview` 可见。hash 只保存非敏感导航 ID；切换不提交表单、不重置
revision、不重连 WebSocket，也不创建另一套 API。900px 以下左栏变为抽屉，二级
标签横向滚动。

## 自动验收

- TypeScript strict check 与确定性 `web:build`；
- `WorkspaceNavigationTests` 从真实 Kestrel 读取 HTML/JS/CSS，验证六个一级入口、
  二级容器、hash 路由、隐藏边界和响应式抽屉；
- 既有 WebUI/API 测试继续验证所有原表单、配置和脱敏契约。
