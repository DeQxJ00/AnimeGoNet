# 静态 TypeScript WebUI 构建（2026-07-22）

## 设计

- `src/AnimeGoNet.App/WebUI/src/app.ts` 是浏览器逻辑的唯一手写源；`wwwroot/app.js` 是提交到 Git 的确定性编译产物。
- TypeScript 固定为 `7.0.2`，启用 `strict`、`noEmitOnError`、DOM/ES2023 lib，目标为无 bundler、无运行时框架的 ES2022 module。
- 根目录 `package-lock.json` 固定开发依赖；`node_modules` 被忽略，不进入程序镜像或发布产物。
- `AnimeGoNet CI` 增加独立 Ubuntu/Node 24 job：`npm ci`、类型检查、重新编译并用 `git diff --exit-code` 验证产物同步。

## 验收

```powershell
npm ci
npm run web:check
npm run web:build
node --check src/AnimeGoNet.App/wwwroot/app.js
dotnet test AnimeGoNet.slnx --configuration Release --no-build
```

TypeScript strict 检查、编译和 JavaScript 语法检查均通过。前端仍只使用浏览器原生 DOM、fetch、dialog、HTML 和 CSS；没有引入 Vue/React 或其他浏览器运行时依赖。
