# 下载器凭据只写私有覆盖（2026-07-26）

- 私有文件固定为 `data_path/config/downloaders.private.json`，不进入 SQLite；`data/` 和 `TestSpace/` 均由 Git 忽略。
- source-generated JSON 格式带 format/global/instance revision；写入使用同目录随机临时文件、flush、原子覆盖，Unix 设置 `0600`。
- `PUT /api/v1/downloaders/{id}` 支持新建/更新；密码省略时保留，显式 `clear_password` 才清除。GET 只返回 `credentials_configured`。
- `DELETE` 移除私有覆盖；基础部署实例会在重启后恢复默认，私有新增实例会消失。任何来源/导入/下载引用都会阻止停用或移除。
- 保存返回 `restart_required=true`；启动在 AnimeGoOptions 校验和 qB registry 创建前应用覆盖。
- WebUI 提供凭据只写表单、revision 冲突提示、私有覆盖移除和明确重启提示。

定向测试覆盖原子文件、reload、revision、凭据替换/保留、启动真实应用、API 负向泄露、引用保护、非法 URL/路径、静态页面契约。私有文件本身含部署密钥，运维必须保护 data_path；它不应被日志、API、截图或 Git 收集。

TypeScript strict/build 与生成 JavaScript 语法检查通过。完整 Release 回归为 Core 168 + Data 66 + App 169，共 403/403；win-x64 NativeAOT 发布 0 warning/0 error。
