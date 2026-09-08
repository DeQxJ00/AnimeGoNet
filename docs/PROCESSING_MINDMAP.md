# AnimeGoNet 联合决策图册

**[在线查看决策导图](https://deqxj00.github.io/AnimeGoNet/)** ·
[HTML 直达链接](https://deqxj00.github.io/AnimeGoNet/animegonet-processing-mindmap.html) ·
[下载 HTML](diagrams/animegonet-processing-mindmap.html) · [返回 README](../README.md)

## 打开方式

- 在线：打开上方 GitHub Pages 入口，无需下载或启动服务。
- 本地仓库：用浏览器打开 `docs/diagrams/animegonet-processing-mindmap.html`。
- GitHub：打开上方链接，在文件页下载原始 HTML，再用浏览器打开下载的文件。
  GitHub 的文件页面展示源码，不会直接运行交互导图。
- 该 HTML 已内嵌样式、脚本和完整图数据，无需安装依赖或启动 AnimeGoNet，
  单独保存即可离线使用；只有点击「代码依据」才需要联网访问 GitHub。

## 在线发布

`main` 中的导图 HTML 或 Pages 工作流更新后，由
`.github/workflows/decision-map-pages.yml` 自动部署，也可在 Actions 手动运行。
仅发布导图 HTML：首页与 HTML 直达地址使用同一份内容，不发布应用配置、数据库或其他仓库文件。
仓库 Settings → Pages → Source 使用 `GitHub Actions`。

## 从哪里开始

- 顶部「01 来源接入」：从整个处理流程起点开始。
- 章节导航串联 Mikan TV、U2 TV、Movie、EP 门禁、统一 AI、复验、下载及整理；
  这是阅读导航，不表示每个任务都依次经过所有章节。
- 「上一步」返回阅读位置并恢复 AI 上下文；「清空路径」回到来源接入。
- 从实际分支进入 AI 后显示原调用入口；「查看该入口失败去向」区分 Mikan 季度、
  U2 季度和 Episode 门禁。直接打开 AI 章节而无上下文时，不推断失败出口。
- 「查看全部复验成功后的去向」用于阅读后续归类提交，不表示跳过程序验证。
- 「回到总览」：进入十个阶段的目录。总览虚线表示分类，不表示执行先后。
- 实线箭头：当前阶段中的执行方向；子节点上方标出进入该分支的条件。
- 「判断条件」节点：分别展开「有 / 没有」「满足 / 不满足」等后续分支。
- 「↗ 跳转继续」节点：点击后跳到下一处理阶段或返回点。
- 「＋ / −」：展开或收起子节点；内容较多时，可在详情中选择「从此节点继续展开」。
- 点击普通节点查看规则和代码依据；搜索可查找尚未展开的节点。
  支持拖动平移、滚轮缩放、适应视图与明暗主题切换。

## 覆盖内容与版本

导图包括来源接入、Mikan 作品与季度、U2 TV、U2 Movie、Episode 门禁、统一 AI、
复验与归类、下载做种、文件整理、失败恢复与人工操作。AI 的失败返回点按调用入口
区分，U2 的 AniDB 全集快捷分支、Movie 主片容量门禁和四种文件策略也分别展开。

此图是 **v1.2.1 / d16817fae750ccf5de59c69dcd3b516d787b4ede** 的代码逻辑快照，
不会自动随当前分支或后续版本更新。「代码依据」固定指向这个提交，避免把旧版本
导图与新版本实现混用。当前实现还应结合[元数据流程](METADATA_RESOLUTION.md)、
[来源路由](SOURCE_ROUTING.md)及对应代码查看。
