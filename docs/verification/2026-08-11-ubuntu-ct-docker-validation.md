# Ubuntu 24.04 CT Docker 验证 — 2026-08-11

## 结论

在隔离的 Ubuntu 24.04 x86_64 CT `root@192.168.1.164` 上，提交
`e22cb081b4ffb8155fa2293b134a48b5c95be95d` 已完成 linux-x64 NativeAOT、
容器运行约束、双 qBittorrent、Mikan 完整链路和发布镜像 Chromium WebUI 的真实验收。
全部阶段退出码为 0，清理退出码为 0。

脱敏机器报告保存在本机非 Git 测试目录：

`TestSpace/animegonet_data/docker-ubuntu-ct/docker-ct-audit-be9bcedb24f546a2b4a8ea6a8ae8a3e7.json`

报告只记录环境、镜像版本、提交、阶段耗时和退出码，不包含密码、Cookie、passkey、
Torrent URL 或 WebUI 凭据。

## 已执行阶段

| 阶段 | 结果 | 耗时 |
|---|---:|---:|
| 环境预检 | 通过 | 0.205 秒 |
| 提交归档解包 | 通过 | 0.157 秒 |
| linux-x64 NativeAOT 镜像构建 | 通过 | 21.019 秒 |
| 容器 API、SQLite、路径与 SIGTERM | 通过 | 5.546 秒 |
| 双 qB、完整链路和 Chromium WebUI | 通过 | 425.937 秒 |

最后一阶段的首次运行包含拉取固定的 Playwright `v1.62.0-noble` 镜像；浏览器断言
本身为 1/1 通过、1.6 秒。

## 已验证行为

- AnimeGoNet 以 linux-x64 NativeAOT、任意非 root UID/GID、只读根文件系统、
  `no-new-privileges` 和受限 tmpfs 运行；healthcheck、SQLite 建库和 SIGTERM 零退出通过。
- `/data`、`/download/incomplete`、`/download/anime` 与双 qB 的共享 `/download`
  映射一致；默认保存路径、显式路径转换和硬链接能力探测通过。
- 两个隔离 qBittorrent 5.1.4 实例分别完成登录、版本、路径、任务生命周期及清理；
  Mikan 来源投递到 `bt`，测试用 U2 路由骨架投递到 `pt`，两实例不存在串任务。
- 外部 C# NativeAOT 插件在只读包挂载和非 root 进程下启用、执行、禁用；可写数据
  目录与只读程序包边界通过。
- 合法 128 KiB WebSeed 经统一导入、真实 qB 下载、Bangumi/TMDB Series/Season/Episode
  验证、Mikan `move` 整理、NFO/三层 sidecar、完成记录和媒体库 API 后精确清理。
- Episode 使用 `tmdb_episode_bangumi_date`，即已确认 Season 内以 Bangumi 单集日期
  证据匹配并最终验证 TMDB Episode；测试未调用 AI。
- 发布镜像内静态 WebUI 由官方 Playwright Chromium 容器访问，下载、元数据和作品库
  的完成状态可见，且无 console/page error。

## 隔离和清理

测试使用唯一远端 `/var/tmp/animegonet-docker-audit-<run>`、唯一 Compose project、
唯一镜像标签、测试 category/tag/hash。退出时只删除这些精确资源，不执行全局 prune。
验收后复查本次容器和唯一镜像均为 0，根文件系统剩余 8.6 GB。

## 仍未覆盖

本证据只适用于 Ubuntu 24.04 x86_64/linux-x64。它不替代 linux-arm64 Docker/原生
runner、win-arm64、osx-arm64、真实 GitHub Prerelease、上游 Go Linux 基线或独立
AnimeGoNetData Release 的验收，也未使用用户私人 Torrent、RSS、qB profile 或凭据。
