# 双 qBittorrent 隔离 Compose 门禁（2026-07-30）

## 已实现

- 新增专用 `docker-compose.qbittorrent-integration.yml`，同时定义 `bt`、`pt` 和
  AnimeGoNet，三者共享同一个临时 `/download`，数据和两个 qB profile 彼此隔离。
- 所有宿主端口只绑定 `127.0.0.1` 随机端口；临时根目录和 Compose project 名
  每次唯一，退出 trap 销毁容器、卷和目录。
- qB 首启认证不预置生产 secret。smoke 从各实例日志读取临时密码，通过正式
  WebUI Cookie 登录设置测试期密码，重启后再次登录，证明凭据/profile 可恢复。
- 两个实例分别验证 version、Web API version、默认保存路径、add、按 tag list、
  单文件 manifest、filePrio 0/1、start、stop 和 delete。
- Torrent fixture 只有一个 5 字节文件，announce 为
  `http://127.0.0.1:9/announce`；不包含外部 tracker、passkey 或版权内容。
- 每个实例使用唯一 `animegonet-ci-*` category/tag；测试结束显式删除任务、文件、
  tag、category，最终 trap 再销毁完整隔离根目录。
- AnimeGoNet 使用运行期私有下载器配置连接两个容器，验证连接诊断、默认路径、
  shared-volume 硬链接探测和 Mikan→bt、U2/TTG→pt 的 SourceProfile 路由。
- Docker GitHub Actions 在单镜像 smoke 后运行双 qB smoke。

## 本机验证

- `bash -n eng/smoke-qbittorrent-compose.sh`
- Python `yaml.safe_load` 解析生产 Compose、集成 Compose 和 Docker workflow。
- `DockerQbittorrentIntegrationContractTests` 验证隔离、安全、完整 API 生命周期、
  workflow 接线以及 fixture 的精确字节内容。
- Release 全量测试：897/897 通过，0 失败、0 跳过。
- `win-x64` .NET 10 NativeAOT publish 成功；published-binary smoke 通过 `/ping`、
  schema v30、SQLite、WebSocket、静态 WebUI、安全 ingest 拒绝和 qB capability。

本机没有 Docker CLI，因此没有声称在本机执行真实容器。实际 Web API 生命周期
由 GitHub Actions 的 Ubuntu Docker runner 运行；首次 runner 结果是把 TODO
P7-162 从“已接入门禁”提升为“已外部验收”的必要证据。

## 安全边界

该门禁不读取 `TestSpace`、portable profile、用户 Cookie、用户密码、API key、
passkey、RSS 或私人 Torrent。脚本中的密码是随机回环端口和临时 profile 内的公开
测试常量，整个 profile 在退出时删除，不能用于生产部署。
