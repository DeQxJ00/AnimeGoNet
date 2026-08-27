# U2 Torrent 本地识别审计

把待分析的 `.torrent` 文件放入仓库根目录的 `u2-torrent-test/`（该目录及生成的 CSV 不会提交到 Git），运行：

```powershell
dotnet run --project tools/AnimeGoNet.U2TorrentAudit/AnimeGoNet.U2TorrentAudit.csproj --no-restore
```

也可以指定其他目录：

```powershell
dotnet run --project tools/AnimeGoNet.U2TorrentAudit/AnimeGoNet.U2TorrentAudit.csproj --no-restore -- D:\u2-torrent-test
```

每个 Torrent 会在原目录生成同名 `.csv`。CSV 会保留 Torrent 内部的相对路径，并记录：

- `episode_candidate`：U2 当前规则识别到普通 EP；
- `unresolved`：视频文件但当前规则未确认 EP；
- `non_video`：非视频附件；
- `parser_reason`：具体规则结果，便于后续逐条调整 U2 规则。
