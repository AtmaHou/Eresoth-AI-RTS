# 技术附录：指令 Schema 细则（v2）

> 本文是《AI增强自然语言指挥实现计划》的技术契约附录，面向实现与调试。
> 设计层面的"为什么"见主文档第 3 章；本文只描述"契约长什么样"。

## 1. 军令模型

军令 = 主体 + 动作 + 目标选择器 + 约束 + 触发器。

```json
{
  "forceId": "force_2",
  "action": "AttackMove",
  "targetId": "enemy_east_mana",
  "stance": "Normal",
  "unit_filter": null,
  "priority": 50,
  "conditions": [
    { "metric": "EnemyCountNear", "op": "Ge", "value": 8, "subject": "self",
      "then": "Retreat", "thenTargetId": "own_retreat_point" }
  ],
  "count": 0, "ratio": 0, "resource": null
}
```

## 2. 动作白名单（12）

- 军事 8（ForceController 执行）：`Move / Attack / AttackMove / Defend / Retreat / FocusFire / Regroup / Hold`
- 经济 4（EconomyPlanner 执行）：`Train / Research / Build / AssignWorkers`（经济参数：`count / ratio / resource`）

判定：`action >= Train` 即经济军令。白名单之外一律拒绝，不做静默映射。

## 3. 触发器（谓词组合）

`metric + op + value + subject → then [+ thenTargetId]`

- 6 个度量：`EnemyCountNear / AllyHealthRatio / Resource / BuildingHpRatio / EnemyVisible / TimeElapsed`
- 4 个比较符：`Lt / Le / Gt / Ge`
- 旧版字符串条件（如"主力就撤""伤亡撤"）由解析层自动归一化为谓词形式，执行层只认谓词。

## 4. 语义地图

模型只引用稳定地标 ID，不输出坐标：

- 固定：`own_main_base / own_retreat_point / center_mana`
- 敌方半场：`enemy_east_mana` 等（中文别名消歧："东矿"→ 敌方半场，"家门口"→ 己方）
- 动态：`enemy_frontline`（由 ForceManager 每 2 秒根据可见敌情更新）

## 5. 优先级与仲裁

- 系统紧急军令（基地遇袭回防）：priority 90
- 玩家军令：priority 50
- 手动接管：玩家右键指挥的单位 `manualOverrideUntil = Time.time + 8s`，期间不受军团调度

## 6. 防退化措施

1. **动态词表**：Prompt 中的可用动作/度量/地标/兵种/科技由 `StateDigestBuilder` 按当前战局实时生成，不放静态命令列表——新能力 = 注册表加条目，解析逻辑不变。
2. **未知即拒**：模型输出词表外的 token 直接判非法并回报，不做近似映射。
3. **数据留痕**：每次指挥的原文/态势/模型输出/执行结果落盘 `command_log.jsonl`，供评测与端侧蒸馏。

## 7. LLM 接入契约

- 接口：OpenAI 兼容 `POST {base_url}/chat/completions`，JSON mode，temperature 0.2，超时 12s
- 配置：项目根目录 `llm_config.json`（`base_url / api_key / model`，gitignored）
- 输入：SystemPrompt（角色+输出契约+字段说明+规则）+ UserPrompt（态势摘要+4 轮历史+few-shot）
- 输出：`{ commands: [...], clarification_needed?, question?, player_reply? }`
- 兜底链：LLM → LocalFallbackParser（关键词+动态词表）→ "没听懂"

## 8. 关键组件索引（Assets/Scripts/Command/）

| 组件 | 职责 |
|---|---|
| Order / OrderDispatcher | 军令模型、校验、仲裁、日志、解释 |
| ForceController / Force / ForceManager | 军团战术执行、编组、动态地标 |
| ConditionEvaluator | 6 种谓词度量求值 |
| EconomyPlanner | 训练/研究/建造/工人分工的持续计划 |
| GameEventBus | 事件总线（条件触发/战报/态势共用） |
| SemanticMap(+Registrar) | 地标注册与别名解析 |
| StateDigestBuilder / PromptBuilder / LlmClient | 态势摘要、Prompt、HTTP 调用 |
| CommandConsole / LocalFallbackParser | 指挥台 UI、离线兜底解析 |
| DebugCommandRunner | F1~F8 示例、F10 编组、Schema DTO 映射 |
