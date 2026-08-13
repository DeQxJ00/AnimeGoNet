# AI 文件名回显不参与整理验收（2026-08-13）

## 业务边界

- AI 响应 `files[]` 的数量必须与输入一致，并按输入数组顺序解释 Season/Episode 结果。
- 单文件任务只有唯一对应关系，AI 的 `files[0].name` 仅用于方便人工观察，不参与文件身份、整理、改名或落盘。
- 多文件任务仍要求逐项原样回显 `name`；乱序或改写会以 `ai_file_identity_mismatch` 拒绝，避免 Episode 绑定到错误文件。
- 主程序构造 `ValidatedAiMetadataFile` 时始终使用相同下标的原始
  `AiMetadataFileInput`，因此 AI 无法改写真实 Torrent 文件名。
- TMDB Series、Season、Episode、重复 Episode 目标和结果完整性校验不放宽。
- 生产 Prompt 本次未修改，仍要求模型原样回显文件名；主程序只是不再让无用途的回显差异
  否决已经通过 TMDB 验证的结果。

## Re:0 回归样本

输入文件：

```text
[ANi] Re：從零開始的異世界生活 第四季 - 12 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4
```

AI 回显文件名：

```text
[ANi] Re：從零開始的異世界生活 第四季 - 12 [1080P][Baha][WEB-DL][AAC AVC][CHT][MP4]
```

回归测试使用该精确差异并返回 S01E78，验证结果成功，最终
`ValidatedAiMetadataFile.Input.Name` 仍是带 `.mp4` 的原始文件名。

## 自动验证

- `AiMetadataResultValidatorTests`：16/16 通过，包含 Re:0 单文件放宽和多文件乱序拒绝；
- `OpenAiCompatibleMetadataMatcherTests` 与 AI Tester `ResultValidatorTests` 定向集合：
  42/42 通过。
