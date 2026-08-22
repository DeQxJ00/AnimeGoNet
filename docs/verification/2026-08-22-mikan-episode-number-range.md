# Mikan 文件名 EP 数字范围验收

## 规则

- 仅文件名中的弱数字标记接受 `1～9999`。
- 超范围数字先作为哈希/校验码排除，再对剩余候选判断是否唯一。
- 没有其他有效候选、只有超范围数字时，返回 `episode_number_out_of_range`。
- 该范围不限制最终由 TMDB API 验证的 Episode。

## 自动化样本

- `[Dynamis One] Kokoore - 07 ... [13335833].mkv`：排除 `13335833`，接受 EP 7。
- `[Group] Show [13335833].mkv`：无有效 EP，返回 `episode_number_out_of_range`。
- 两个都在范围内且不同的标记仍返回 `ambiguous_episode_markers`，可按既有流程进入 AI。
