# 可配置上游与本地反向代理（2026-08-09）

## 范围

- Mikan RSS、作品页和 Torrent 下载统一使用 `metadata.mikan.base_url` 改写 origin；
- TMDB API、TMDB 图片和 Bangumi API 均保留独立 Base URL；
- 四个地址进入部署 YAML、应用私有配置 API 和静态 WebUI，修改均标记为重启生效；
- 仅显式配置的 Mikan Base host 可解析到私网地址，其它 host 和 redirect 继续拒绝私网、loopback 与特殊地址。

## 本地反代示例

```yaml
metadata:
  mikan:
    base_url: http://mikan.local/
  tmdb:
    base_url: http://api.tmdb.local/
    image_base_url: http://image.tmdb.local/t/p/
  bangumi:
    base_url: http://api.bgm.local/
```

反代示例没有包含 key、Cookie、passkey 或真实 Torrent URL。Mikan 改写只替换
scheme/host/port，保留原始 path/query；审计和异常不输出这些私密部分。

## 自动验收

- `MikanEndpointRewriterTests`：验证 origin 替换、path/query 保留和非法 URL 拒绝；
- `TorrentStagingServiceTests`：验证显式可信私网 host 可用，但相邻 allowed host 不会继承信任；
- `AnimeCoverServiceTests`：验证 `http://image.tmdb.local/t/p/` 生成正确 poster 请求；
- `DeploymentYamlConfigurationTests`、`ConfigurationApiTests` 和
  `AnimeGoOptionsValidatorTests`：验证 YAML/API/WebUI 绑定、锁定、兼容旧私有配置及 URL 边界。

真实 CT 可达性只记录 host、HTTP 状态与非敏感摘要；真实输入和凭据留在隔离测试目录。

2026-08-09 在隔离 Ubuntu CT 上使用 DNS `192.168.1.170` 验证：四个 `.home`
主机均解析到该 DNS/反代地址；Mikan、Bangumi API、TMDB API 根路径返回 200，TMDB
图片 `/t/p/` 无具体图片名时返回预期 404，证明反代可达但未把不存在资源误判为成功。
