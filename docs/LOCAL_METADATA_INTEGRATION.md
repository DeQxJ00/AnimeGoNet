# TMDB / Bangumi 本机集成测试

默认 CI 只使用 fake HTTP，不访问 TMDB/Bangumi。真实联调位于未加入 `AnimeGoNet.slnx` 的 `AnimeGoNet.LocalIntegration.Tests`，必须显式提供环境变量。

## TMDB

```powershell
$env:ANIMEGONET_TMDB_INTEGRATION = '1'
$env:ANIMEGONET_TMDB_API_KEY = '<local-test-key>'
$env:ANIMEGONET_TMDB_BASE_URL = 'https://api.themoviedb.org/' # 可省略
$env:ANIMEGONET_TMDB_PROXY_URL = 'http://127.0.0.1:7890/'      # 可省略；也支持 socks5://

dotnet test tests\AnimeGoNet.LocalIntegration.Tests\AnimeGoNet.LocalIntegration.Tests.csproj `
  --filter FullyQualifiedName~TmdbLiveSmokeTests
```

测试只读取已知 Series `72517`，不创建或修改远端数据。API key 只从进程环境读取，不得写入 `appsettings`、`application.private.json`、测试源码、验收文档或命令输出。完成后清除当前 PowerShell 的 `ANIMEGONET_TMDB_*` 变量。

## 运行配置

主程序支持以下部署键，也可通过 WebUI 私密覆盖编辑：

- `tmdb_base_url`、`tmdb_proxy_url`、`tmdb_timeout_second`
- `bangumi_base_url`、`bangumi_proxy_url`、`bangumi_timeout_second`

Base URL 支持带路径前缀的 HTTP(S) 地址，但必须以 `/` 结尾。代理支持无凭据的 `http://`、`https://` 和 `socks5://` origin；TMDB 与 Bangumi 可使用不同代理，也可以只配置 API 地址而不启用代理。
