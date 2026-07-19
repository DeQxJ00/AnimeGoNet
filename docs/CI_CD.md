# GitHub Actions 与发布矩阵

## 已加入的工作流

- `.github/workflows/ci.yml`：规划文件立即校验；工程骨架存在后自动启用 .NET 与 Web 测试。
- `.github/workflows/native-aot.yml`：五个原生 runner 发布、smoke、打包；`v*` tag 创建 GitHub Release 和 SHA-256。
- `.github/workflows/docker.yml`：Buildx 构建并推送 `linux/amd64`、`linux/arm64` 到 GHCR，再在 x64/ARM64 runner 分别 smoke。

工作流带文件探测：当前规划阶段没有 solution/Dockerfile 时相关 job 会安全跳过；Phase 0 创建约定文件后自动转为强制门禁，不需要换一套临时 Action。

## NativeAOT runner 矩阵

| RID | GitHub runner | 产物 |
|---|---|---|
| `win-x64` | `windows-2025` | `.zip` |
| `win-arm64` | `windows-11-arm` | `.zip` |
| `linux-x64` | `ubuntu-24.04` | `.tar.gz`、Docker `linux/amd64` |
| `linux-arm64` | `ubuntu-24.04-arm` | `.tar.gz`、Docker `linux/arm64` |
| `osx-arm64` | `macos-15` | `.tar.gz` |

Windows ARM64 与 Ubuntu ARM64 当前是 GitHub public preview runner，因此不设置 `continue-on-error`；如果 GitHub 临时不可用，发布应失败并重试，不能假装对应产物已验证。macOS 使用 `macos-15`，避免已经进入弃用过程的 `macos-14`。

## Action 版本基线（2026-07-13）

- `actions/checkout@v6`
- `actions/setup-dotnet@v5`
- `actions/setup-node@v6`
- `actions/upload-artifact@v7`
- `actions/download-artifact@v8`
- `docker/setup-qemu-action@v4`
- `docker/setup-buildx-action@v4`
- `docker/login-action@v4`
- `docker/metadata-action@v6`
- `docker/build-push-action@v7`

使用 major tag 便于接收同 major 安全修复；后续可由 Dependabot 自动更新。若采用供应链最高强度模式，再统一改为完整 commit SHA pin，并由机器人提交升级 PR。

## 分支与权限

- PR：只读 token，运行 build/test/AOT；Docker 只构建不推送。
- `main`：推送 GHCR 的 `main`/SHA tag，执行双架构容器 smoke。
- `v*`：发布五个 RID、checksums、GitHub Release、语义版本 Docker tag 和双架构 manifest。
- 默认 `contents: read`；仅 Release job 获得 `contents: write`，Docker build 获得 `packages: write`。
- tag 发布不启用 `cancel-in-progress`，普通分支更新取消旧流水线。

## Phase 0 必须补齐的约定文件

- `global.json`
- `AnimeGoNet.slnx`
- `src/AnimeGo.Host/AnimeGo.Host.csproj`
- `web/animego-web/package-lock.json` 和 `.nvmrc`
- `eng/smoke-aot.ps1`
- `eng/smoke-container.sh`
- `Dockerfile`、`.dockerignore`

这些文件缺失时对应 job 有意跳过；一旦加入仓库，分支保护应把 `CI / dotnet`、`CI / web`、五个 `NativeAOT / publish` 和两个 `Docker / smoke` 设为 required checks。

## 参考

- [GitHub-hosted runner labels](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
- [actions/setup-dotnet](https://github.com/actions/setup-dotnet)
- [Docker build-push-action](https://github.com/docker/build-push-action)
