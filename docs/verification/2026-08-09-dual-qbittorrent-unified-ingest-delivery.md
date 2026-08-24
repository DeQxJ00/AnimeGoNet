# 双 qBittorrent 统一导入容器门禁（生成、未验证）

> 历史状态说明：本文记录 2026-08-09 的生成状态。该门禁已于 2026-08-11 在
> Ubuntu 24.04 x86_64 CT 实跑通过，当前证据见
> `2026-08-11-ubuntu-ct-docker-validation.md`。

日期：2026-08-09

## 交付范围

`docker-compose.qbittorrent-integration.yml` 与
`eng/smoke-qbittorrent-compose.sh` 已扩展为真实调用 AnimeGoNet 统一入口，而不再只做
SourceProfile 路由预览：

- `mikan-ci` 通过 `POST /api/v1/ingest` 路由到 `bt`；
- `u2-ci` 通过相同入口路由到 `pt`；
- AnimeGoNet 容器仅在该集成 Compose 中启用后台 workers，使 staged Torrent 真正由
  dispatcher 投递到 qBittorrent。

脚本对每次统一导入检查：响应为 `staged`、实际 SourceProfile revision、目标下载器、
固定 info-hash、单文件清单和 URL 指纹；响应不得包含 Torrent URL。随后从两个 qB Web
API 分别确认目标实例中的 hash、category、tag、暂停状态、`/download/incomplete/bt|pt`
保存路径和文件名/大小，并确认另一实例不存在同一 hash。

## 隔离 fixture

Compose 新增只读、非 root 的 `busybox:1.37` HTTP fixture 服务，不发布宿主端口，只向
同一临时 Compose bridge 提供两个仓库内固定 Torrent：

| 文件 | info-hash | 内容声明 |
|---|---|---|
| `animegonet-ci.torrent` | `bcff48bafa9434c0062a4c2a45ed885f26701721` | 5 字节单文件 |
| `animegonet-ci-pt.torrent` | `9356dbb012e7d8a6999badefacfc74dd1d00593e` | 7 字节单文件 |

两个 Torrent 的 tracker 都固定为各自 qB 容器内不可达的
`http://127.0.0.1:9/announce`，不含 passkey、私人 tracker、Cookie、凭据或版权载荷。
两个不同 info-hash 避免全局去重使第二个来源无法实际投递。

fixture 服务使用测试专用地址 `11.22.33.44` 和别名
`torrent-fixture.invalid`。这是为了让生产 SSRF 策略仍按真实规则验证一个允许的公网型
地址；地址只配置在本次临时 Compose bridge，HTTP 服务无宿主端口，测试 URL 不指向
Internet 或用户网络。

## 清理边界

category、tag、来源项 ID 和任务均使用 `animegonet-ci-*` 可识别名称。两个任务保持
暂停；验收后脚本只按两个固定测试 hash 执行 `deleteFiles=true`，再删除对应 tag 和
category，并从两个实例复查两个 hash 均不存在。无论中途成功或失败，退出 trap 都会
执行 `docker compose down --volumes --remove-orphans` 并删除 `mktemp` 临时根目录。

脚本不读取 `TestSpace`、用户 qB profile、用户下载目录、用户密码、API key、Cookie、
passkey 或私人 Torrent。

## 后续自行执行

构建待测镜像后，在仓库根目录运行：

```bash
./eng/smoke-qbittorrent-compose.sh animegonet:ci
```

可通过 `QBITTORRENT_IMAGE` 覆盖隔离 qB 镜像。失败时脚本会输出相关容器日志，退出 trap
仍负责清理；如需人工调试，应先复制脚本并有意识地暂时调整 trap，不能改用用户实例。

## 本次静态验收

- `bash -n eng/smoke-qbittorrent-compose.sh`；
- Compose YAML 解析；
- .NET delivery contract 验证 fixture 精确字节、隔离服务、安全边界、双实例实际
  `/api/v1/ingest`、反向实例排除及清理代码均已生成；
- 完整 .NET Release 测试 1460/1460 通过，0 失败、0 跳过。

按项目所有者要求，本次不执行 Docker 命令，不声称容器构建、启动、网络、qB Web API
或统一投递已经运行成功；该门禁状态为“已生成、未验证”。
