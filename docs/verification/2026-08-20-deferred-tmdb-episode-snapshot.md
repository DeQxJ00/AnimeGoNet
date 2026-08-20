# 延后 TMDB Episode 正式投影

日期：2026-08-20

## 修正语义

- Series/Season 确认只保存作品、季度、首播日期、封面和 `episode_count`。
- TMDB Season HTTP 响应仍可进入成功缓存，供后续 Episode 判断复用；它不是作品库正式 Episode 投影。
- 逐文件确定性判断、统一 AI 匹配及最终 TMDB 验证结束后，Episode 完成事务才替换 `tmdb_episodes` 的完整 Season snapshot。
- TMDB 对同一 Series 内未来 Episode ID 的集号调整按最新完整 snapshot 替换，不再让无关的未来集阻断当前文件判断。

## 回归样本

名侦探柯南 `TMDB 30983 / S01` 的远端数据把 Episode ID `7622653` 从 E1210 调整为 E1212。旧流程在文件 `DR118-2` 进入 EP 判断前写整季 snapshot，因身份冲突回滚并等待租约。新流程先完成目标文件判断，最终事务再以 TMDB 当前完整 snapshot 更新正式投影。

## 验收

- Season 完成后 `tmdb_episodes` 仍为空，但 Series/Season 和任务季度身份已经提交。
- Episode 完成事务写入完整 snapshot，并可把同一 Series 内旧的未来 Episode ID 从旧集号移动到 TMDB 当前集号。
- 多季度 AI 结果要求每个已确认 Season 都提供完整且通过身份验证的 snapshot。

## 真实任务验证

任务 `e6eca8100a8042f9b3a4e6cd8768c467` 使用文件
`[SBSUB][CONAN][DR118-2_SubOnly_Beginning&Ending][TVRIP][1080P][HEVC_AAC][CHS_CHT_JP][PGS](8EFBBF6F).mkv` 验证：

- 本地文件名判断先返回 `episode_not_parsed`，此时尚未提交正式 Episode snapshot。
- 随后调用 `gpt-5.6-luna`，耗时 42628 ms，AI 结果经 TMDB 验证为 `TMDB 30983 / S01E118 / Episode ID 1489537`。
- Episode 判断完成后，事务一次性写入 S01 的 1212 条正式 Episode snapshot。
- 远端变更的 Episode ID `7622653` 已从旧 E1210 更新为 E1212；当前 E1210 为 Episode ID `7684373`。
- 任务随后进入 `downloading`，证明不再被全季度缓存写入异常卡在 Metadata Worker。
