# 外部 C# 插件容器门禁（生成、未验证）

日期：2026-08-09

> 状态更新（2026-08-11）：本文正文保留生成当日的验收边界。linux-x64 外部 C# source
> 插件现已在 Ubuntu 24.04 x86_64 CT 随完整容器链实跑通过，包括发现、启用、normalize、
> 插件数据目录、包只读、非 root UID 和禁用后的失败边界；证据见
> `2026-08-11-ubuntu-ct-docker-validation.md`。linux-arm64 插件容器仍未验证。

## 生成内容

新增 `AnimeGoNet.ContainerPluginFixture`，它是只用于交付验收的 source 插件：

- 直接引用 `AnimeGo.Plugin.Sdk`，使用 source-generated JSON 和 JSON Lines 宿主；
- 只声明 `linux-x64`、`linux-arm64`，由独立 Dockerfile 进行 NativeAOT publish；
- 执行时把有效 UID 和包目录只读结果写入宿主分配的
  `ANIMEGO_PLUGIN_DATA_PATH/container-smoke.txt`；
- 主动尝试在 `AppContext.BaseDirectory` 创建探针，只有写入被拒绝才继续规范化；
- 不读取 SQLite、下载器、用户目录、网络凭据或其他插件数据。

`eng/export-external-plugin-fixture.sh` 只接受一个尚不存在的输出子目录，使用唯一临时
Docker image/container 导出包，拒绝符号链接，固定目录/入口为 `0555`、其余文件为
`0444`，并复核 manifest ID、类型、RID 和入口。退出 trap 只删除脚本自己创建的精确
container/image，不递归删除调用方输出。

## Compose 边界

双 qB 隔离 Compose 继续把临时 `animegonet/data` 挂到 `/data`，并用第二个更具体的
bind mount 将
`/data/plugins/com.animegonet.container-source` 覆盖为 `:ro`。因此主程序仍能在
`/data/plugin-data/<plugin-id>` 建立插件数据，但包本身不可修改。

AnimeGoNet 和它启动的插件进程使用相同的动态非 root UID/GID。smoke 将 fixture 报告的
有效 UID 与该动态 UID 比对，不依赖容器内固定用户名。

## 运行流程

现有 `eng/smoke-qbittorrent-compose.sh` 在启动 AnimeGoNet 前生成并挂载 fixture，随后：

1. 从 `/api/v1/status` 确认 source 包无发现错误、RID 为 `linux-x64`、初始禁用；
2. 通过带 revision 的配置 API 启用插件；
3. 创建使用该外部 adapter 的 SourceProfile；
4. 调用 `/api/v1/ingest`，真实启动插件并完成 normalize；
5. 使用不在 SourceProfile 白名单的合成 Host，使主程序在 DNS/网络访问前以
   `HostNotAllowed` 安全停止；
6. 检查插件数据 marker、非 root UID、包只读自检，以及 runtime `ready`/零失败；
7. 通过 revision API 禁用插件、删除 marker，再次导入并确认 unavailable 且 marker
   不再生成，runtime 回到 `stopped`。

该流程不使用 Python，不访问公网，不提交或读取 TestSpace、用户插件、Cookie、密码、
API key、passkey 或私人 Torrent。

## 本次验收边界

本机完成 fixture JIT 编译（0 warning/0 error）、两个 Bash 脚本语法检查、Compose 与
Workflow YAML 解析、相关 delivery contract 10/10 和完整 .NET Release 回归
1463/1463（0 失败、0 跳过）。按项目所有者要求，
没有执行 Docker build/run/cp/Compose 命令，不声称 Linux NativeAOT 插件或容器挂载已经
运行成功；后续由项目所有者执行现有 Docker workflow 或 smoke 脚本验收。
