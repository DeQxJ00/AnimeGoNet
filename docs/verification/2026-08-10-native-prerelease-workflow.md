# NativeAOT 预发布闭环（2026-08-10）

- 五个 RID 继续在各自原生 runner 执行 publish、应用/迁移/元数据 smoke、插件模板验证，
  并生成内部 SHA-256、SBOM 与第三方许可证。
- 预发布 job 仅响应已经推送的 `vMAJOR.MINOR.PATCH-SUFFIX` 标签，并以
  `needs: publish` 等待整个矩阵成功。
- `actions/download-artifact@v8` 下载五份 RID artifact；固定 RID 集合逐份调用已有的
  确定性打包器，缺一个目录、ZIP 或 `.sha256` 都会失败。
- GitHub CLI 使用 `--verify-tag --prerelease --latest=false` 创建 Release，不代建标签、
  不覆盖已有资产，也不把预发布标记为稳定最新版。
- 本次只生成与测试工作流；尚未推送版本标签，因此不声称远端 runner 或首个
  Prerelease 已经成功。
