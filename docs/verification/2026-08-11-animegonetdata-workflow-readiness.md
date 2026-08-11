# AnimeGoNetData workflow readiness — 2026-08-11

## 修复

外部数据发布工作流的 DataBuilder 命令在
`--minimum-episode-count 300000` 后缺少 Bash 续行符，导致下一行
`--minimum-relation-count 10000` 会被当作独立命令。真实 Action 会在生成包阶段退出，
且关系数量门禁没有作为 DataBuilder 参数执行。

`.github/workflows/animegonet-data.yml` 现已恢复续行，并把 checkout、setup-dotnet、
upload-artifact 升级到项目统一的 v6、v5、v7。

## 验证

- YamlDotNet 成功解析完整 workflow。
- 从 workflow 原样提取 DataBuilder Bash block 后，`bash -n` 通过。
- 契约测试锁定两个数量参数之间必须存在续行符，避免只检查“文字出现”却漏掉命令
  结构。
- `BangumiArchivePackageBuilderTests` 9/9 通过，覆盖确定性在线资产、离线 ZIP、
  `SHA256SUMS`、关系完整性和不可变外部 Release 契约。
- 格式检查和 `git diff --check` 通过。

## 剩余外部边界

本地仍没有独立 AnimeGoNetData Git 仓库、`ANIMEGONET_DATA_REPOSITORY` 或最小权限
Release token，因此没有创建 tag、draft、公开 Release 或 latest 指针。首次真实发布仍需
仓库所有者提供外部仓库和授权；本次只证明工作流不再在 DataBuilder 命令边界必然失败。
