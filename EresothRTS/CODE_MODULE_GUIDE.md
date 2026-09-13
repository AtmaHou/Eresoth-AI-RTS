# Eresoth RTS 代码模块导读

> 目标：只说代码结构——模块职责、调用关系、扩展点。细节以代码为准。

纯代码 Unity RTS Demo：场景仅提供入口，地形/单位/建筑/资源/UI 全部运行时生成，零美术资源。核心特色是 Command/ 文字指挥链路（LLM 解析玩家原话 → 结构化军令）。

## 1. 模块关系图

```text
Main.unity
  +-- RTSSceneSetup.cs（Assets/Editor，编辑器场景工具，不参与运行时）
  +-- Game.cs                  总控：世界生成、资源/科技/弹道/施工调度/胜负；持有全局列表 units / buildings / nodes
        +-- Common.cs          枚举、ITargetable、UnitDef/BuildingDef/TechDef、MapSettings、GameConfig（代理层）
        +-- RTSConfig.cs       ScriptableObject：数值 + 单位/建筑/科技表（Inspector 可调）
        +-- RuntimeConfig.cs   启动加载 RTSConfig → 静态字典 Units/Buildings/Techs
        |
        +-- Unit.cs            单位命令入口 CommandMove/CommandAttack；移动/索敌/克制/受伤
        |    +-- Worker.cs        采集/施工状态机
        |    +-- UnitVfx.cs       战场特效对象池（三档质量）
        +-- Building.cs        生产/研究/箭塔/施工进度
        +-- ResourceNode.cs    树木/魔法水晶（耗尽销毁）
        +-- EnemyAI.cs         脚本 AI：经济分工 + 克制暴兵 + 波次进攻（LLM 不可用时的降级执行体）
        +-- FogOfWarManager.cs 战争迷雾：未探索全黑 / 已探索半暗；敌方物体与未探索资源点整体隐藏
        |
        +-- SelectionManager.cs    玩家输入翻译层：点选/框选/右键命令/编队/建造放置（底层同样走 Unit.Command*）
        +-- RTSCameraController.cs 镜头平移/缩放/旋转
        +-- GameHUD.cs             开局菜单 / 资源栏 / 生产研究面板 / 结算；LLM 配置区（F10 或面板按钮，仅存本机）
        |
        +-- Models.cs / Models.Units.cs / Gfx.cs   程序化模型库；BodyRig 动画挂点；材质/网格工具
        |
        \-- Command/               文字指挥链路（项目核心，全部组件由 Game 运行时挂载）
             CommandConsole.cs       右侧可收放指挥栏：Send(text) 入口；静态 TypingActive/PointerOver 供输入层静默；落盘 command_log.jsonl
             LlmClient.cs            OpenAI 兼容客户端（coroutine）；配置优先级：persistentDataPath 本机文件（运行时填写）→ 项目根 llm_config.json（gitignore）→ 走兜底
             LocalFallbackParser.cs  API 失败时的关键词规则解析（词表动态来自注册表，只覆盖高频意图）
             DebugCommandRunner.cs   LLM/兜底共用的请求 DTO（CommandRequest/OrderDto/ConditionDto）+ DTO→Order 映射校验（Map）
             PromptBuilder.cs        系统/用户 Prompt 拼装（词表随注册表自动更新）
             StateDigestBuilder.cs   态势摘要 JSON（军团/资源/战况，喂给 LLM）
             SemanticMap.cs          语义点注册表："东矿/家门口"等别名 → 世界坐标（TryGet）
             SemanticMapRegistrar.cs 开局注册本局语义点
             Order.cs                军令模型：OrderAction 白名单 + Order 五槽位 + OrderCondition 触发器
             OrderDispatcher.cs      SubmitOrder / Complete：校验入队、优先级覆盖、状态流转
             ForceManager.cs         军团注册表：forces / OfTeam / FindOfUnit
             Force.cs                军团：units / rallyPoint / stance / currentOrder / Centroid / HealthRatio / Prune
             ForceController.cs      军事执行体：0.25s tick 把军令翻译成 Unit.Command*；事件通道紧急回防（≤300ms）；残血撤退管理
             EconomyPlanner.cs       经济军令持续计划（Train/Research/Build/AssignWorkers），训练完成回调
             ConditionEvaluator.cs   触发器指标统一求值（ConditionMetric switch）
             GameEventBus.cs         全局事件 Publish / OnEvent（战报、基地遇袭等）
```

## 2. 启动流程

1. `Game.OnEnable`：`RuntimeConfig.Initialize()` 加载 RTSConfig 资产（缺失时 `FillDefaults` 兜底）；动态挂载全部组件（含 Command/*、FogOfWarManager）；建相机。
2. `GameHUD` 开局菜单：阵营 / 地图 / 资源 / 胜利条件 / 难度 / 演示模式，以及 LLM 配置区（只存本机，不入库）。
3. `StartGame()` → `BuildWorld()`：噪声地形（`TerrainHeight`）→ 资源点（只为 0 号侧布局，1 号取中心对称点保证公平）→ 双方基地与工人 → `SemanticMapRegistrar.Register` → `FogOfWarManager.ResetFog`。
4. 运行时各 `Update` 并行：`Unit/Worker/Building` 行为、`EnemyAI`（2s 决策）、`ForceController`（0.25s tick + 事件通道）、弹道/特效/镜头。
5. `CheckEnd()` 按胜利模式判定胜负 → `GameHUD` 结算。

## 3. 配置链路

```text
RTSConfig 资产（Inspector 可调）→ RuntimeConfig.Initialize() → 静态字典 → GameConfig 代理转发（旧代码入口不变）
```

- 调数值改 `Assets/Resources/EresothRTSConfig.asset`，不动代码；新增单位/建筑/科技在资产加条目，用 id 组装（`trainUnitIds` / `techIds` / `factionBuildings`）。
- AI 决策参数也在资产：进攻间隔、兵力阈值、工人数、资源价格与紧缺阈值。

## 4. 核心数据流

```text
玩家鼠标：SelectionManager → Unit.CommandMove / CommandAttack（与军令层共用同一入口，含 manualOverrideUntil 手操保护）
文字指挥：CommandConsole.Send → LlmClient.Parse（失败 → LocalFallbackParser 兜底）
           → CommandRequest(DTO) → DebugCommandRunner.Map 校验 → OrderDispatcher.SubmitOrder
           ├─ 军事：Force.currentOrder → ForceController tick → Unit.Command*（触发器命中 → 提交后继 Order）
           └─ 经济：EconomyPlanner 持续执行直至完成/被拒
工人采集：Worker.GatherAt → 状态机 → Game.GatherAmt → Game.Deposit（主基地/收集站就近交付）
迷雾感知：任何"敌人在哪"的查询走 FogOfWarManager.VisibleToPlayer（迷雾关闭时恒 true，调用方无需判空）
```

## 5. 军令模型（Command/Order.cs，改动前必读）

- `Order` 五槽位：`forceId` + `action` + `targetId`（语义点 ID）+ 约束（`stance` / `unitFilter` / `priority` / `expiresAt`）+ `conditions`（触发器列表）。
- `OrderAction` 白名单：军事 8 种（Move/Attack/AttackMove/Defend/Retreat/FocusFire/Regroup/Hold）+ 经济 4 种（Train/Research/Build/AssignWorkers）。新增动作 = 注册枚举 + 在 ForceController/EconomyPlanner 挂钩，解析/校验/Prompt 词表自动跟随。
- `OrderCondition` = `metric` + `op` + `value` + `subject`，命中后执行 `then` / `thenTargetId`。新增指标 = `ConditionMetric` 枚举 + `ConditionEvaluator` 一个 case。
- 优先级约定：玩家手操 = 100 覆盖一切；基地遇袭系统紧急回防 = 90；普通军令 50。
- `targetId` 一律经 `SemanticMap` 解析为坐标；空 `thenTargetId` 默认撤向己方撤退点。

## 6. 修改速查

| 想修改的内容 | 优先查看 |
| --- | --- |
| 任何数值、单位/建筑/科技属性 | `EresothRTSConfig.asset`（或 `RuntimeConfig.FillDefaults`） |
| 新增单位/建筑/科技 | RTSConfig 资产条目 + id 引用组装 |
| 单位战斗/移动/克制 | `Unit.cs` |
| 采集与施工 | `Worker.cs`、`Game.AssignBuilders` |
| 生产/研究/箭塔 | `Building.cs` |
| 脚本 AI 经济与进攻节奏 | `EnemyAI.cs` + RTSConfig 的 AI 参数区 |
| 地图/地形/资源分布 | `Game.BuildWorld()`、`TerrainHeight`、`MapSettings` |
| 玩家输入方式 | `SelectionManager.cs` |
| 界面（菜单/HUD/结算） | `GameHUD.cs`；指挥栏 = `CommandConsole.cs` |
| 单位/建筑外观、动画挂点 | `Models.Units.cs` / `Models.cs`（`BodyRig`） |
| 特效 | `UnitVfx.cs` |
| 文字指挥链路 / LLM 接入 | `Command/` 全目录，入口 `CommandConsole.Send`、`LlmClient.Parse` |
| 新触发器指标 | `Order.cs`（ConditionMetric）+ `ConditionEvaluator.cs` |
| 新语义点（"东矿"类别名） | `SemanticMapRegistrar.cs` |
| 迷雾表现与感知口径 | `FogOfWarManager.cs` |
| LLM 配置/密钥 | `LlmClient.cs`（运行时填，存 persistentDataPath，勿提交 llm_config.json） |
| 胜负条件 | `Game.CheckEnd()` |
| 执行层最终入口（重要） | `Unit.CommandMove()` / `Unit.CommandAttack()` / `ITargetable` |

## 7. 架构约束

- 单位与建筑都实现 `ITargetable`，新增可攻击目标优先复用该接口。
- 军令执行体最终只能调 `Unit.CommandMove/CommandAttack`，不得直接改单位内部状态。
- 远程攻击必须走 `Game.SpawnProjectile()`，不要瞬间扣血。
- 全局列表由 `Game` 持有，实体销毁时各自 `OnDestroy()` 移除自己。
- 所有实体贴地走 `Game.TerrainHeight(x, z)`，新增生成逻辑不要假设 y=0。
- `Game.Toast()` 仅玩家阵营侧可调用。
- 对局日志在 `EresothRTS/command_log.jsonl`（玩家原话/模型输出/执行结果），是评测与蒸馏数据源，改动指挥链路时保持字段稳定。
