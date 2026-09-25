# 开发记录 D3：Prompt 外置、日志 v2 与 Prompt Lab 调试工具

日期：2026-09-25
范围：指挥链路的可调试性改造（不改任何玩法/执行逻辑）。

## 1. 动机

- Prompt 硬编码在 `PromptBuilder.cs`，改一句话都要重编译，无法独立调试。
- `llm_log.jsonl` 把 prompt 塞在转义后的 request 字符串里且截断 4000 字符，user prompt（含 digest）经常被截掉，"到底给模型发了什么"看不出来。
- `command_log.jsonl` 重复存 digest+request+response，一条几 KB。

## 2. 改动清单

### Prompt 外置（EresothRTS/prompts/）
- `system_prompt.txt`：角色 + 输出契约 + 军事/经济字段规范 + 规则。
- `user_template.txt`：user prompt 模板，占位符 `{digest}` `{history}` `{examples}` `{player_text}`。
- `examples.txt`：few-shot 示例（从 user prompt 里拆出来，军事/经济/编制/侦察各一条）。
- `PromptBuilder.cs` 运行时优先读文件，按修改时间热重载（改完下一条指令生效）；文件缺失回退内嵌默认（与文件内容一致，打包发布不依赖 prompts 目录）。
- 内容优化：删掉与 digest 中 vocab.aliases 重复的硬编码口语映射表（旧规则 7），改为"优先匹配 aliases"；重排结构、统一术语。

### 日志 v2（llm_log.jsonl）
- 新字段：`system` / `user`（完整 prompt，上限 64KB）、`content` / `reasoning`（正文与思考过程分开）、`usage`（prompt/completion/reasoning/total tokens）、`prompt_src`（当时生效的 prompt 文件版本）、`ms` / `status` / `error`；`response_raw` 只在出错时保留（截断 4000）。
- `LlmClient.cs` 解析 `usage` 与 `reasoning_content`，暴露 `LastSystemPrompt/LastUserPrompt/LastContent/LastReasoning/LastUsage`。
- `command_log.jsonl` 瘦身：去掉重复的 request/response（llm_log 里有完整现场，按时间戳对上），保留 `text/digest/model/reply/result/exec`。
- 兼容：jsonl 追加式，旧行新行混排不影响解析；Prompt Lab 按字段有无自适应 v1/v2。

### 顺手修复的真 bug
- `StateDigestBuilder.cs`：`vocab.units` 与 `techs` 数组后缺逗号，digest 一直是非法 JSON（模型容错没暴露，但任何下游解析都会炸）。已补逗号。

### Prompt Lab（AI_RTS/tools/，零第三方依赖）
- `prompt_lab.py`：本地服务器（stdlib http.server + urllib），`python tools/prompt_lab.py` 启动，自动开 `http://127.0.0.1:8735`。代理 LLM 调用（避开浏览器 CORS、api_key 不出服务端），组装规则与游戏内 PromptBuilder 完全一致。
- `prompt_lab.html`：三个页签——
  1. **日志查看**：llm_log / command_log 列表 + 完整现场（system/user/content/reasoning/usage/错误，digest 自动 pretty-print）。
  2. **Prompt 调试**：编辑三个 prompt 文件（保存即写盘、游戏热重载生效）；选 digest 来源（示例/日志最新/手贴）+ 历史 + 玩家命令 → 发送看返回，可一键存为样例。
  3. **批跑样例**：样例增删改、逐条/全部运行、结果表（JSON 合法性、orders/economy 条数、耗时、token、参谋回复 vs 期望备注）、导出 jsonl。
- `sample_digest.json`：从 command_log 真实态势改造的调试样例（补齐两个军团，军事/经济命令都能测）。
- `test_cases.json`：16 条预置样例（军事/经济/编制/侦察/条件/问答全覆盖，附期望备注）。

## 3. 验证

- `compile_check.sh`：COMPILE OK。
- Prompt Lab e2e：读真实日志 ✓；prompt 文件读写 ✓；用 kimi-k2.6 实跑"二军团去东矿，遇到主力就撤"→ attack_move + enemy_count_near≥8 → retreat 条件，与期望一致 ✓；"造4个弓箭手，攻击科技优先"→ train archer×4 + research human_atk（别名正确归一）✓；"现在还有多少木头？"→ orders/economy 留空纯回复 ✓。

## 4. 注意

- `EresothRTS/llm_config.json`（gitignored）由 Prompt Lab 的"模型设置"读写，游戏与调试共用一份配置。
- 批跑会真实消耗 API token（kimi-k2.6 推理模型单条约 3K token、12~18s）。
- 内嵌默认 prompt 与 prompts/*.txt 需保持一致：日常调 prompt 只改文件；确认更优后可把文件内容同步回 `PromptBuilder.cs` 的内嵌串，用 `python tools/check_prompt_sync.py` 校验两者一致。
