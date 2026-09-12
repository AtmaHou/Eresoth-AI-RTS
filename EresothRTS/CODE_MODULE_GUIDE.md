# Eresoth RTS 代码模块导读

> 本版基于当前代码实读重写（对齐 Phase 1 Demo 现状）。旧版导读中关于 "WorkerAI"、"数值全在 GameConfig 硬编码" 等描述已过时，以本文为准。

项目是一个 Unity 纯代码 RTS Demo：场景只提供入口，地形、单位、建筑、资源和 UI 全部运行时生成，零美术资源依赖。

## 1. 先记住这张关系图

```text
Main.unity
    |
    +-- RTSSceneSetup.cs          编辑器菜单：生成/准备演示场景（不参与运行时）
    |
    +-- Game.cs                   游戏总控：世界生成、资源、科技、胜负、弹道、施工调度
          |
          +-- RTSConfig.cs        ScriptableObject 总配置（Inspector 可编辑的数值/单位/建筑/科技表）
          +-- RuntimeConfig.cs    启动时加载 RTSConfig -> 生成 UnitDef/BuildingDef/TechDef 字典
          +-- Common.cs           枚举、ITargetable、Def 类、MapSettings、GameConfig（代理层）
          |
          +-- Unit.cs             单位移动/攻击/索敌/克制/受伤/动画驱动
          |     +-- Worker.cs     工人采集与施工状态机
          |     +-- UnitVfx.cs    职业化战场特效（对象池，三档质量）
          |
          +-- Building.cs         建筑生产、研究、箭塔、施工进度
          +-- ResourceNode.cs     树木 / 魔法水晶
          |
          +-- Models.cs           程序化建筑模型库（partial）
          +-- Models.Units.cs     程序化单位模型库 + BodyRig 动画挂点（partial）
          +-- Gfx.cs              材质缓存 + 图元/自定义 Mesh 工具
          |
          +-- SelectionManager.cs 玩家选择、右键命令、编队、建筑放置
          +-- EnemyAI.cs          脚本 AI：动态经济分工 + 克制配比暴兵 + 波次进攻
          +-- GameHUD.cs          OnGUI 界面：开局面板、资源栏、生产/研究、血条、结算
          \-- RTSCameraController.cs 镜头平移/缩放/旋转
```

## 2. 启动流程

1. 场景中的 `Game` 组件执行 `OnEnable()`：绑定单例 `Game.I`，调用 `RuntimeConfig.Initialize()` 加载 `Resources/EresothRTSConfig.asset`（缺失时用代码默认值兜底），初始化双方资源，创建相机，并动态挂载 `SelectionManager`、`EnemyAI`、`GameHUD`。
2. `GameHUD` 显示开局设置面板：阵营、地图大小、资源丰富度、胜利条件、难度、随机种子开关。
3. 玩家确认后 `Game.StartGame()`：读取 `MapSettings`（此刻才读，Awake 太早），困难难度给 AI 加资源补贴，按地图尺寸定位双方基地，调用 `BuildWorld()`。
4. `BuildWorld()` 生成：灯光/天空盒 → 噪声起伏地形（`TerrainHeight`）→ 崖壁/装饰 → 资源点（只为 0 号侧布局，1 号取中心对称点保证公平）→ 双方主基地与初始工人。
5. 每帧各组件 `Update()` 并行：单位行为、工人采集、建筑生产/研究、AI 决策（每 2 秒一轮）、弹道飞行、特效回收、镜头控制。
6. 按胜利模式（摧毁主基地 / 摧毁所有建筑），`Game.CheckEnd()` 判定胜负，`GameHUD` 显示结算。

## 3. 配置体系（改动最大、最需要注意的部分）

**数值不再硬编码在 `GameConfig`。** 新链路是：

```text
RTSConfig (ScriptableObject, Inspector 可编辑)
    -> RuntimeConfig.Initialize() 启动时加载一次
    -> 生成 Dictionary<string, UnitDef/BuildingDef/TechDef>
    -> GameConfig 静态属性代理转发（旧代码零改动）
```

- 策划调数值：改 `Assets/Resources/EresothRTSConfig.asset`，不动代码。
- 代码读数值：照旧用 `GameConfig.CounterBonus`、`GameConfig.Knight` 等静态入口。
- 新增单位/建筑/科技：在 RTSConfig 资产里加条目（或改 `RuntimeConfig.FillDefaults` 的兜底表），通过 id 引用组装（`trainUnitIds`、`techIds`、`factionBuildings`）。
- AI 决策参数也在 RTSConfig：进攻间隔、兵力阈值、工人数、资源价格与紧缺阈值（`resourceShortageRatio`）等。

## 4. 模块说明

### 4.1 `Common.cs`：类型与代理层

- 枚举：`Team`、`UnitKind`（Worker/Infantry/Ranged/Cavalry，循环克制：步→骑→远程→步）、`TechEffect`、`VictoryMode`、`Difficulty`。
- `ITargetable`：单位与建筑统一目标接口（Pos/Radius/Team/Alive/Hp01/Damage）。**后续 AI 军令层的 target 寻址用此接口**。
- `UnitDef` / `BuildingDef` / `TechDef`：静态数据类，支持可选外部 prefab（空则回退程序化模型）。`UnitDef` 含英雄字段（光环 `auraRadius/auraBonus`、AOE `aoeRadius`）。
- `MapSettings`：开局设置（static，跨"再来一局"保留）。
- `GameConfig`：纯代理，全部转发到 `RuntimeConfig`。

### 4.2 `Game.cs`：规则服务层（789 行）

跨实体全局规则集中在此，实体只管自身行为：

- **资源**：`TrySpend` / `Deposit` / `GatherAmt`（含收集站加成）、人口 `PopCount/PopCap`。
- **科技**：`AtkMult`（每级 +15% 乘区）、`DefMult`、`FinishResearch`。
- **弹道**：`SpawnProjectile`（远程兵/英雄/箭塔共用，支持英雄 AOE 溅射 + `NotifyAttacked` 通知）。
- **英雄光环**：`AuraDmgMult`（附近有己方英雄则伤害加成）。
- **建造**：`BuildStructure`（主基地 32 半径内随机选址）、`BuildForwardResourceHub`（朝中央富集矿方向的前哨收集站选址）、`CanPlaceAt`/`BuildAt`（玩家放置模式）、`AssignBuilders`（自动派空闲工人施工）。
- **查询**：`NearestHall` / `NearestDeposit` / `NearestNode` / `BuildingOfKind`。
- **世界生成**：`TerrainHeight(x,z)` 静态地形高度采样（所有实体贴地走）、`BuildTerrain/BuildCliffs/ScatterDecor`。
- **提示**：`Toast(msg)` 仅玩家侧使用，AI 不得调用。

### 4.3 `Unit.cs`：单位运行时（421 行）

- 命令入口：`CommandMove(Vector3)`、`CommandAttack(ITargetable)` —— **AI 军令层最终只能调用这两个入口**，不得直接改内部状态。
- `MoveStep(dest, dt, stopRadius)`：供外部驱动（Worker、未来的军团执行体）使用的单步移动。
- 自动索敌（`aggro` 半径）、被打后 `NotifyAttacked` 做"继续任务 vs 反击"的收益判断。
- `CounterMult()`：循环克制 ×CounterBonus，对建筑/工人无加成。
- 移动含局部避让（选空隙大的一侧绕行）与碰撞挤出，防止穿模。
- `stunT` 瘫痪计时、`holdSlot` 驻守索引（预留机制）。
- 动画：持有 `BodyRig rig`，行走/攻击时摆动腿臂披风等挂点；`AnimWalk()` 供 Worker 驱动。
- 受击/死亡触发 `UnitVfx` 对应特效；`Damage()` 应用防御科技倍率。

### 4.4 `Worker.cs`：采集/施工状态机（97 行）

```text
Idle -> ToNode -> Gathering -> Returning -> ToNode
  \-> Constructing -> Building
```

- `GatherAt(node)`、`BuildAt(building)`、`FinishBuilding`、`StopGather`。
- `CanAutoBuild`：Idle 且未主动被玩家派活时才接受自动施工调度（玩家指派有 `constructionOptOut` 保护，不会被立即抢回）。
- 交付走 `Game.NearestDeposit`（主基地或资源收集站就近）。

### 4.5 `Building.cs`：建筑运行时（243 行）

- 数据驱动：`BuildingDef.train`（可训练）、`techs`（可研究）、`atkRange > 0` 即防御塔。
- 生产队列 `queue`（上限 `MaxProductionQueue`）、研究单槽 `research/researchTimer`。
- 施工系统：`constructing` + `constructionProgress`，施工中 HP 逐步增长、受伤 ×2，`NeedsBuilders` 驱动 `Game.AssignBuilders` 派工；建成后回调 `EnemyAI.OnBuildingCompleted`。
- `rally` 集结点；未完工建筑不能生产/研究/攻击。

### 4.6 `EnemyAI.cs`：脚本 AI（392 行）

每 `aiDecisionInterval`（2s）一轮决策，已比旧导读描述的"固定配比"更智能：

1. **动态经济分工**：按资源价格、库存紧缺度（`resourceShortageRatio`）和近期消耗预测，把空闲工人分配给 wood/mana；空闲超 N 轮强制重分配。
2. 补工人至 `aiMaxWorkers` → 按序建兵种建筑/前哨资源收集站（`BuildForwardResourceHub`）/民居。
3. 轮流研究攻防科技。
4. **克制配比暴兵**：基础步:弓:骑 = .38/.32/.30，根据玩家兵种构成向克制兵种偏移；资源够时出英雄。
5. **波次进攻**：兵力达阈值后集结进攻，目标优先级：玩家分矿（收集站）→ 主基地附近守军 → 主基地；目标摧毁后自动选续攻目标。

定位：规则驱动执行体。未来 LLM 接入时保留其底层能力，LLM 只做上层目标/策略决策；它也是自然语言指挥失败时的降级执行体。

### 4.7 `SelectionManager.cs`：输入适配层（270 行）

左键点选/框选/双击选同类、右键移动/攻击/采集、选中建筑右键设集结点、Ctrl/Alt+数字编队、建造放置模式（`BeginPlacement`，左键确认、右键/Esc 取消）。只负责把输入翻译成调用，不含任何规则逻辑。

### 4.8 `GameHUD.cs`：OnGUI 界面（295 行）

零 UI 资源，全部 `OnGUI()`：开局面板、资源/人口/科技栏、建筑生产与研究按钮（内容来自 `BuildingDef`，新增单位无需改 HUD）、血条、Toast、结算界面。

### 4.9 表现层：`Models.cs` + `Models.Units.cs` + `Gfx.cs` + `UnitVfx.cs`

- `Gfx`：材质缓存（金属度/光滑度/自发光参数版）、图元创建、自定义 Mesh（屋顶/棱柱/圆台/面片）。
- `Models`（partial，1429 行合计）：纯代码拼装的低多边形风格化模型库。`Models.cs` 负责建筑与调色板 `Pal`（人族暖木石/不死暗紫骨白两套）；`Models.Units.cs` 负责人形骨架、武器、盾牌、披风、马体、英雄专属造型（骑士团长/霜骨巫妖），并定义 **`BodyRig` 动画挂点集合**（行走/攻击动画与特效定位都靠它，仅限视觉层使用）。
- `UnitVfx`：三档质量（Off/Low/High）对象池特效——移动尘土、攻击前摇、命中火花、格挡、采集、死亡，按阵营区分视觉语言（人族金色火花/不死魂火骨屑）。

### 4.10 `RTSCameraController.cs` / `ResourceNode.cs`

- 镜头：WASD/方向键/贴边平移、滚轮缩放、Q/E 旋转，限制在地图范围内。
- 资源点：`wood`/`mana` 两种，只持有类型与余量，耗尽销毁。

## 5. 三条核心数据流

```text
玩家命令：鼠标 -> SelectionManager -> Unit.CommandMove/CommandAttack -> Unit.Update -> Damage
工人采集：右键资源点 -> Worker.GatherAt -> 状态机 -> Game.GatherAmt -> Game.Deposit
建筑生产：HUD 按钮 -> Building.TryTrain -> Game.TrySpend -> queue -> Unit.Spawn
```

## 6. 修改速查表

| 想修改的内容 | 优先查看 |
| --- | --- |
| 任何数值、单位/建筑/科技属性 | `EresothRTSConfig.asset`（或 `RuntimeConfig.FillDefaults`） |
| 新增单位/建筑/科技 | RTSConfig 资产条目 + id 引用组装 |
| 单位战斗/移动/克制 | `Unit.cs` |
| 采集与施工 | `Worker.cs`、`Game.AssignBuilders` |
| 生产/研究/箭塔 | `Building.cs` |
| AI 经济与进攻节奏 | `EnemyAI.cs` + RTSConfig 的 AI 参数区 |
| 地图/地形/资源分布 | `Game.BuildWorld()`、`TerrainHeight`、`MapSettings` |
| 玩家输入方式 | `SelectionManager.cs` |
| 界面 | `GameHUD.cs` |
| 单位/建筑外观 | `Models.Units.cs` / `Models.cs`（+ `BodyRig`） |
| 特效 | `UnitVfx.cs`（含质量档位） |
| 胜负条件 | `Game.CheckEnd()` |
| **AI 军令层接口（重要）** | `Unit.CommandMove()` / `Unit.CommandAttack()` / `ITargetable` |

## 7. 修改时的注意事项

- 单位与建筑都实现 `ITargetable`，新增可攻击目标优先复用。
- 全局列表由 `Game` 持有，实体销毁时各自 `OnDestroy()` 移除自己。
- 远程攻击必须用 `Game.SpawnProjectile()`，不要瞬间扣血。
- 所有实体贴地走 `Game.TerrainHeight(x, z)`，新增生成逻辑不要假设 y=0。
- 只有玩家阵营提示可调用 `Game.Toast()`。
- 调试布局时 `MapSettings.randomMap = false`，固定种子 20240901 可复现。
- 输入依赖 Unity 旧版 Input API，无响应时检查 Active Input Handling 设置。
- RTSConfig 资产缺失时代码默认值兜底，但正式调参以资产为准，两处不要改出分歧。

## 8. 一句话总结

数据由 `RTSConfig → RuntimeConfig → GameConfig` 集中供给，规则由 `Game` 统筹，实体由 `Unit/Worker/Building` 执行，表现由 `Models/Gfx/UnitVfx` 程序化生成，玩家与脚本 AI 经统一命令入口驱动——为接入 LLM 军令层预留的扩展点是 `ITargetable` 与 `Unit.CommandMove/CommandAttack`。
