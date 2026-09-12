# D1 阶段修改说明（文字指挥 Demo）

> 完整项目在 `Eresoth-AI-RTS/`，直接覆盖同名文件即可。
> 新增 .cs 无 .meta，Unity 首次打开自动生成。

## 这一阶段做了什么

**从"F 键硬编码演示"升级为"打字指挥玩一整局"**，同时把 Schema 从命令枚举升级为泛化谓词模型。

### 新增文件（Command/）

| 文件 | 职责 |
|---|---|
| `ConditionEvaluator.cs` | 谓词求值器：6 种 metric（敌军数量/军团血量/资源/建筑血量/敌情/计时）× 4 种比较符，"如果……就……"整类表达通用覆盖 |
| `StateDigestBuilder.cs` | 态势摘要（1~2K token）：资源/军团（位置=最近语义点）/建筑/敌情/近 60s 事件 + **动态词表**（动作、metric、目标、兵种、科技、建筑全部运行时现拼） |
| `PromptBuilder.cs` | 角色设定 + 输出契约 + 2 条 few-shot；**Prompt 里没有任何静态命令清单**，能力全随 digest 的 vocab 下发 |
| `LlmClient.cs` | OpenAI 兼容 API（`POST {base_url}/chat/completions`，JSON mode，temperature 0.2，超时 12s） |
| `CommandConsole.cs` | 左下角指挥台：输入框 + 参谋回复 + 追问显示 + 系统拒绝原因；4 轮对话上下文；逐条落盘 `command_log.jsonl` |
| `LocalFallbackParser.cs` | 离线兜底：关键词解析（词表同样动态来自注册表），解析不了就明说"没听懂" |
| `EconomyPlanner.cs` | 经济计划执行器：训练（计数进度）/研究（一级即收工）/建造（含开分矿）/工人比例分配，1s 一跳，资源不足静默等待 |

### Schema v2 泛化（防退化的关键）

军令 = 主体 + 动作 + 目标选择器 + 约束 + **谓词触发器**（`metric + op + value + subject`）。
新增能力的成本 = 注册表加一个词条（metric 枚举 + 求值 case），Prompt/校验/解析自动跟随——不再是"每条命令一个硬编码模板"。
旧版 `when:"enemy_main_force_seen"` 字符串在解析层自动归一化为谓词，老 JSON 不报废。

### 演示模式（开局面板可选）

开局自带：12 战斗单位（步6/弓4/骑2）自动编两军团 + 已完工兵营/弓箭场 + 双倍资源；AI 进攻间隔减半、首波提前。目标：3 分钟内出现"分兵—遇袭—条件撤退—回防"完整剧情，可稳定录屏。

### 对现有文件的改动

`Game.cs`（挂新组件 + SetupDemoMode）、`GameHUD.cs`（演示模式开关 + 面板加高）、`EnemyAI.cs`（演示模式进攻性）、`Common.cs`（demoMode 开关）、`RTSCameraController.cs`/`SelectionManager.cs`（指挥台输入/悬停时快捷键静默）、`Building.cs`（训练完成回调经济计划进度）。

## 上手步骤

1. 复制 `llm_config.template.json` 为项目根目录 `llm_config.json`（与 Assets 同级），填入你的 `base_url`/`api_key`/`model`（任何 OpenAI 兼容服务均可，国产模型网关也行）。
2. 打开项目，开局面板勾选**演示模式**，开始游戏。
3. 左下角指挥台直接打字，例如：
   - "一军团守家，二军团去打东矿，遇到主力就撤"
   - "所有远程集火敌方主力"（→ focus_fire）
   - "造 4 个弓箭手，研究攻击科技"
   - "六成的工人去采木头"
   - "主基地血量低于一半就全军回防"（building_hp_ratio 谓词）
4. 模型不理解时会追问；断网/超时时自动切本地关键词解析（回复带"［离线兜底］"）。

## 验证 Schema 泛化性的方法

不用改任何解析代码，直接试这些组合表达（谓词 × 动作笛卡尔积）：
- "敌人超过 5 个接近中矿就全军回防"（enemy_count_near + 语义点 subject）
- "木头低于 100 就停止造兵"（resource 谓词，D2 接经济计划条件）
- "30 秒后全线转防守"（time_elapsed）

## 已知边界

- 训练计划可能多排队少量单位（队列深度导致的过冲，D2 修）。
- 经济计划暂不支持 conditions（D2 把谓词接到 EconomyPlanner）。
- 无战争迷雾，"敌方前线"等于开全图（D3）。
- `command_log.jsonl` 与 `llm_config.json` 已加入 .gitignore。
