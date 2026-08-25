# AnimeGoNet 首版实现完成审计

审计日期：2026-08-12。业务基线为
`wetor/AnimeGo develop@c7475dfc55a374cd0dd08821bf17125dab1e3145`，并叠加项目所有者
在开发期间确认的 AnimeGoNet 新语义。

## 结论

首版约定范围内已经没有未开始、进行中或阻塞的实现项。Ubuntu 24.04 x86_64 CT
已真实验证 linux-x64 NativeAOT Docker、双 qB、Mikan 完整链路、外部 C# 插件和发布
镜像 WebUI。`TODO.md` 中剩余 `[~]` 表示对应 arm64/macOS 平台或外部 Release 仍没有
完整运行证据。固定上游 Go Linux amd64 基线已于 2026-08-11 通过；
U2 已由所有者明确暂缓，不属于首版正式输入源。

## 原始硬性要求与证据

| 要求 | 当前实现证据 | 验收证据 |
|---|---|---|
| 从 Git 开始、`codex/` 分支、模块独立提交 | 当前分支 `codex/animegonet-main`；TODO、测试与代码按功能提交 | Git 历史及干净工作树 |
| .NET 10、NativeAOT、五 RID | `Directory.Build.props`、App AOT 项目、`animegonet-native-aot.yml` 五个原生 runner | win-x64 与 Ubuntu CT linux-x64 publish/smoke 已通过；win-arm64、linux-arm64、osx-arm64 由 `[~]` 明确等待远端原生 runner；工作流在 restore 前断言实际 OS/CPU 与 RID 一致 |
| GitHub Actions 构建/测试/AOT/Docker | `dotnet-ci.yml`、`animegonet-native-aot.yml`、`animegonet-docker.yml` | YAML/交付契约测试；Ubuntu CT linux-x64 Docker 实跑通过，linux-arm64 待远端 |
| Minimal API 与轻量静态 WebUI | `ApiEndpoints.cs`、TypeScript 7 strict 源码及嵌入 `wwwroot` | Kestrel、Node DOM、静态资源与 NativeAOT smoke |
| SQLite 显式 SQL、避免反射 ORM | `AnimeGoNet.Data` schema/migration/store 全部使用显式 SQL | migration、事务、并发、恢复及 AOT SQLite smoke |
| 移除 Python；C# 编译期内置插件 | `BuiltInPluginCatalog` 与进程隔离的可选 C# 插件协议 | 插件目录/协议/真实进程/NativeAOT template tests；不读取或执行 `.py` |
| 首版仅 qBittorrent；Mikan 默认 move | 命名 qB registry、默认 Mikan SourceProfile、四种文件策略 | 本机隔离 qB 单/多文件与真实 move/NFO/completion/cleanup |
| Docker 三路径与共享挂载一致 | `/data`、`/download/incomplete`、`/download/anime` 固定配置和 Compose 卷 | Ubuntu CT NativeAOT 与双 qB 共享映射、真实下载和 move 整理通过 |
| 统一导入与旧 Mikan API 兼容 | `/api/v1/ingest`、`/api/v1/rss/ingest`、`/api/download/manager`、`/api/rss` 共用 staging/路由 | OpenAPI/Kestrel/AnimeGoHelper 浏览器契约与 29 条真实 Mikan 输入 |
| 不同来源绑定不同 qB 与规则 | revision 化 SourceProfile 保存 downloader、规则开关、策略/category/tags/做种 | CRUD、route preview、双实例隔离与快照测试；U2 首版暂缓 |
| `mikanid` 人工覆盖与可信 offset | mikanid 规则最高优先；`mikanid+groupid` 按不同文件名 EP 学习、门槛可配置且默认 3，缓存默认关闭 | SQLite 状态机、冲突撤销、WebUI、重匹配和 Episode worker tests |
| RSS 黑白名单与有序优选 | 前置资格过滤后，仅多候选执行有序组；小写规范化 | 真实 RSS fixture、批次审计、历史回滚、WebUI CRUD/preview |
| TMDB 权威 Series/Season/Episode 与 Other | P4/P3/AI 远端结果逐级验证；小数/特别篇不冒充整数 EP | 多轮搜索、P3 回溯、Episode/字幕/Other 与真实 loopback tests |
| P4→P3→AI→P2→P1 失败链 | P3 需要 bgmid 且逐前作联合找 Series+Season；P2 只读任务 title；P1 本地 S01 | 决策顺序、失败分类、UI 说明与审计时间线 tests |
| TMDB 完全失败默认关闭 | 仅权威语义无匹配可写 `tmdbid=0`；固定 S01、有效 bgmid、待补全、不伪造进度 | 网络/认证/协议门禁、fallback claim/completion/NFO/恢复 tests |
| AI 单流程、默认关闭、600 秒 | 一个任务级 Prompt 同时返回 Series/Season/全部 Episode；MCP/作品参考可选 | fake HTTP/MCP、超时/429/畸形/伪造 ID/用量审计与 NativeAOT smoke |
| API/图片地址与选择性全局代理 | Mikan/TMDB API/TMDB image/Bangumi/AI/MCP 均可配地址；一个代理支持精确域名和 `*.` | YAML/API/WebUI、DNS/redirect/SSRF、HTTP(S)/SOCKS5 与脱敏 tests |
| WebUI 管理面与详细日志 | 左侧一级菜单、页内二级菜单；配置/来源/qB/规则/任务/作品/待补全/删除/日志 | TypeScript/DOM/Kestrel/Playwright NativeAOT 本机验收 |
| 字幕关联、四类删除和精确去重 | 字幕随 EP 改名并保留语言/轨道后缀；四类删除冻结计划；只跳过同 TMDB+EP | 多文件真实 qB、删除失败恢复、安全路径与 completion claim tests |
| Docker/跨容器路径模型 | 下载器保存根、应用 download/save 根和路径/硬链接探测均显式配置 | Ubuntu CT 双 qB 默认路径、共享目录/硬链接探测及全链通过 |
| Mikan 真实数据完整链 | 私有 CSV 29 条均真实执行 Mikan/qB 清单/规则/SQLite/Bangumi/TMDB；下载可替换为显式 synthetic payload | 29/29 匹配；3 条真实 BT 整理；其余完整整理或正确按 TMDB+EP 去重；AI 0 token |
| 可审查发布 | 五 RID 成功后逐 RID 验证/打包 ZIP+SHA，再用已有稳定或预发布标签创建 GitHub Release | 五 RID 远端 publish/smoke、打包器和工作流契约测试已通过；首次 `v1.0.0` 标签/Release 仍为 `[~]` |

## 当前验证快照

- `dotnet test AnimeGoNet.slnx --configuration Release --no-restore`：1613/1613 通过。
- `npm run web:test`：17/17 通过。
- 当前提交重新发布 `win-x64` NativeAOT：0 error；已发布二进制 first-start smoke 通过。
- qB Mikan 审计清理后，测试 category/tag 任务残留为 0。
- Ubuntu 24.04 x86_64 CT 报告
  `docker-ct-audit-be9bcedb24f546a2b4a8ea6a8ae8a3e7.json`：5/5 阶段与清理均为 0；
  发布镜像 Playwright 1/1 通过。
- 固定上游 `c7475df` 在官方 Go 1.22.10 linux/amd64 容器串行执行：exit 0、
  3109 条 JSON 事件、100 个上游 skip，报告 SHA-256 全部复验通过。

## 明确不冒充已验证的外部项

- Docker linux-arm64 构建和容器 E2E；linux-x64 已由 Ubuntu CT 验证。
- `win-arm64`、`linux-arm64`、`osx-arm64` 原生 runner 结果。
- 第一个实际 GitHub `v1.0.0` 标签和正式 Release。
- 独立 AnimeGoNetData 仓库的 token/变量及首次不可变 Release。

这些项目不是缺少实现；对应 workflow、脚本和失败门禁已经提交。只有取得对应平台或
外部发布结果后才能从 `[~]` 改为已验证。Ubuntu CT 的详细证据见
`docs/verification/2026-08-11-ubuntu-ct-docker-validation.md`。
