# Eresoth RTS 代码模块导读

这份文档用于帮助快速理解项目代码。项目是一个 Unity 6 的纯代码 RTS Demo：场景只提供入口，地图、单位、建筑、资源和 UI 都在运行时生成。

## 1. 先记住这张关系图

```text
Main.unity
    |
    +-- RTSSceneSetup.cs       编辑器菜单：生成/准备演示场景
    |
    +-- Game.cs                游戏总控、世界生成、资源、科技、胜负、弹道
          |
          +-- Common.cs        规则和静态数据：UnitDef / BuildingDef / TechDef
          +-- Unit.cs          所有单位的移动、攻击、索敌、受伤
          |     \-- Worker.cs  工人的采集状态机
          +-- Building.cs      建筑生产、研究、箭塔攻击、受伤
          +-- ResourceNode.cs  树木和魔法水晶
          |
          +-- SelectionManager.cs  玩家选择和下达命令
          +-- EnemyAI.cs            AI 每 2 秒做一次经济和军事决策
          +-- GameHUD.cs            开局面板、资源栏、建筑操作、结算界面
          +-- RTSCameraController.cs 镜头移动、缩放、旋转
          \-- Gfx.cs               用 Unity 内置图元生成模型和材质
```

## 2. 游戏启动流程

1. 打开 `Main.unity`，场景中的 `Game` 组件执行 `Awake()`。
2. `Game.Awake()` 设置单例 `Game.I`，创建相机，并动态添加 `SelectionManager`、`EnemyAI`、`GameHUD`。
3. `GameHUD` 显示开局设置。玩家选择阵营、地图随机和资源丰富度后点击开始。
4. `Game.StartGame()` 将 `started` 设为 `true`，调用 `BuildWorld()`。
5. `BuildWorld()` 创建地面、边界、资源点、双方主基地和初始工人。
6. Unity 每帧调用各组件的 `Update()`：单位行动、工人采集、建筑生产/研究、AI 决策、弹道飞行和镜头控制同时运行。
7. 主基地被摧毁时，`Game.CheckEnd()` 设置胜负；`GameHUD` 显示结算界面。

## 3. 模块说明

### 3.1 `Common.cs`：规则数据中心

这是阅读项目时最应该先看的文件。它主要定义：

- `Team`、`UnitKind`、`TechEffect` 等枚举。
- `ITargetable`：单位和建筑都实现的统一目标接口。攻击、选取和未来 LLM 指挥不需要区分目标具体类型。
- `UnitDef`：单位静态数据，如生命、伤害、射程、速度、造价、是否工人/英雄。
- `BuildingDef`：建筑静态数据，如生命、造价、可训练单位、可研究科技和箭塔攻击参数。
- `TechDef`：科技名称、费用、研究时间、最大等级和效果。
- `MapSettings`：开局设置，并通过静态字段在“再来一局”时保留。
- `GameConfig`：人口上限、采集速度、训练时间、克制倍率，以及所有单位/建筑/科技表。

**改数值时**优先改 `GameConfig`，不要把数值散落到行为脚本中。行为脚本只负责“怎么执行”。

### 3.2 `Game.cs`：游戏总控

`Game` 是场景中的核心单例，访问方式是 `Game.I`。它负责：

- 保存双方资源、科技等级、单位列表、建筑列表和资源点列表。
- 生成地图、主基地、初始工人和相机。
- 统一扣除资源、增加资源、统计人口和查询最近目标。
- 管理远程攻击和箭塔共用的弹道系统。
- 计算科技攻击/防御倍率、英雄攻击光环倍率和工人采集加成。
- 校验建筑放置位置、执行建造、限制箭塔数量。
- 检查主基地是否被摧毁并结束游戏。

可以把它理解成“规则服务层”：实体负责自身行为，但跨实体的全局规则集中在这里。

### 3.3 `Unit.cs`：单位运行时行为

`UnitDef` 是配置，`Unit` 是运行中的具体单位。每个单位包含阵营、当前生命值、目标、移动目的地和最近攻击者。

核心流程：

- `CommandMove(Vector3)`：清除攻击目标，设置移动目的地。
- `CommandAttack(ITargetable)`：锁定单位或建筑目标。
- `Update()`：按目标距离决定移动或攻击；没有显式目标时自动索敌。
- 被攻击后通过 `lastAttacker` 自动反击并追击。
- 远程单位调用 `Game.SpawnProjectile()`，近战单位直接造成伤害。
- `CounterMult()` 实现步兵克骑兵、远程克步兵、骑兵克远程。
- `Damage()` 应用本阵营防御科技倍率，生命值归零后销毁对象。

**未来接入 LLM 的关键位置**：LLM 不应直接改单位内部状态，而应最终调用 `CommandMove` 或 `CommandAttack`。

### 3.4 `Worker.cs`：工人采集状态机

工人是带有 `Worker` 组件的 `Unit`。它不负责普通战斗，而是在以下状态之间循环：

```text
Idle -> ToNode -> Gathering -> Returning -> ToNode
```

- `GatherAt()`：指定树木或魔法水晶。
- 到达资源点后按 `GameConfig.GatherTime` 计时。
- 采集量来自 `Game.GatherAmt()`；有伐木场时木材采集量增加 50%。
- 回到最近主基地后通过 `Game.Deposit()` 入账。
- 资源点耗尽后销毁，工人回到空闲状态。

### 3.5 `Building.cs`：建筑生产与科技

建筑通过 `BuildingDef` 决定能力，不同建筑共享同一套运行时脚本：

- 主基地：训练工人。
- 兵营/地穴：训练步兵和英雄，并研究攻防科技。
- 弓箭场/诅咒神殿：训练远程单位。
- 马厩/死亡马厩：训练骑兵。
- 伐木场：为全阵营提供采集加成。
- 箭塔：自动寻找射程内最近敌方单位并发射弹道。

生产使用 `queue`，一次最多排队 5 个单位；研究使用单独的 `research` 槽位。`TryTrain()` 和 `TryResearch()` 负责费用、人口、英雄唯一性和科技等级校验。

### 3.6 `EnemyAI.cs`：第一阶段脚本 AI

AI 每 2 秒执行一轮固定优先级决策：

1. 给空闲工人分配木材或魔法矿。
2. 主基地补充工人，最多维持 8 名工人。
3. 按顺序建造步兵建筑、远程建筑、骑兵建筑和伐木场。
4. 轮流研究攻击和防御科技。
5. 按步兵、远程、骑兵比例训练军队，并在资源充足时训练英雄。
6. 部队达到阈值后集体攻击玩家主基地，波次规模逐渐增加。

它是规则驱动的执行体，不是通用规划器。后续如果加入 LLM，建议保留这里的基础执行能力，把 LLM 放在“决定目标/策略”的上层。

### 3.7 `SelectionManager.cs`：玩家输入到游戏命令的适配器

该脚本把鼠标和键盘操作转换为游戏对象能理解的调用：

- 左键点击：选择单位或建筑。
- 左键拖动：框选己方单位。
- 双击单位：选择屏幕内同类型单位。
- 右键地面：对选中单位调用 `CommandMove()`。
- 右键敌人：调用 `CommandAttack()`。
- 右键资源点：工人调用 `Worker.GatherAt()`。
- 选中建筑后右键：修改建筑集结点。
- `Ctrl/Alt + 数字` 保存编队，数字键召回编队。
- 建造时进入放置模式，使用 `Game.CanPlaceAt()` 校验，确认后调用 `Game.BuildAt()`。

这是“输入层”，不应在这里实现伤害、资源扣除或单位移动细节。

### 3.8 `GameHUD.cs`：无资源 UI

所有界面使用 Unity `OnGUI()` 绘制，不依赖 Canvas 或美术资源。主要包括：

- 开局设置面板。
- 顶部资源、人口、科技等级和地图种子信息。
- 选中建筑后的训练、建造和研究按钮。
- 选中单位后的数量和操作提示。
- 单位/建筑血条。
- 胜负结算与重新开始。

按钮内容主要来自 `BuildingDef.train` 和 `BuildingDef.techs`，因此新增可训练单位或科技时通常不需要修改 HUD。

### 3.9 `RTSCameraController.cs`：RTS 镜头

挂在 `CameraRig` 上，控制：

- WASD、方向键和鼠标贴边平移。
- 鼠标滚轮缩放，并限制最小/最大距离。
- Q/E 旋转。
- 将镜头位置限制在地图范围内。

### 3.10 `ResourceNode.cs`：资源实体

资源点只关心资源类型和剩余数量：`wood` 表示木头，`mana` 表示魔法水晶。`Game.BuildWorld()` 决定生成位置和数量，`Worker` 决定如何采集。

### 3.11 `Gfx.cs`：程序化表现层

提供两个公共工具：

- `Mat(Color)`：按颜色缓存材质，兼容内置管线和 URP。
- `Prim(...)`：创建内置 Cube、Sphere、Cylinder 等图元，并自动移除装饰图元的碰撞体。

单位、建筑、资源点的外观都在运行时由它拼出来，因此项目不需要额外美术资源。

### 3.12 `Assets/Editor/RTSSceneSetup.cs`：编辑器工具

它属于编辑器侧代码，不参与运行时战斗逻辑。README 中的“工具 → 生成 RTS 演示场景”入口由它提供，主要用于创建或准备演示场景。

## 4. 三条最重要的数据流

### 玩家移动/攻击

```text
鼠标输入
  -> SelectionManager.Command()
  -> Unit.CommandMove() / Unit.CommandAttack()
  -> Unit.Update()
  -> MoveStep() 或 FaceAndHit()
  -> Damage()
```

### 工人采集

```text
右键资源点
  -> SelectionManager
  -> Worker.GatherAt()
  -> Worker 状态机
  -> Game.GatherAmt()
  -> Game.Deposit()
```

### 建筑生产

```text
GameHUD 按钮
  -> Building.TryTrain()
  -> Game.TrySpend()
  -> Building.queue
  -> Building.Update()
  -> Unit.Spawn()
```

## 5. 推荐阅读顺序

### 只想快速理解

1. `Common.cs`：看数据表和全局常量。
2. `Game.cs`：看世界如何创建、资源如何流动、胜负如何判定。
3. `Unit.cs`：看单位如何移动、攻击和自动反击。
4. `Building.cs`：看生产、科技和箭塔。
5. `SelectionManager.cs`：看玩家输入如何变成命令。
6. `EnemyAI.cs`：看 AI 如何使用同一套建筑和单位接口。

### 想修改功能

| 想修改的内容 | 优先查看 |
| --- | --- |
| 单位属性、建筑费用、科技数值 | `Common.cs` 的 `GameConfig` |
| 新增单位类型或攻击效果 | `Unit.cs`、`UnitDef` |
| 采集速度、资源点数量 | `Worker.cs`、`Game.BuildWorld()`、`GameConfig` |
| 新建筑或生产队列 | `Building.cs`、`BuildingDef`、`GameHUD.cs` |
| 玩家快捷键和指挥方式 | `SelectionManager.cs` |
| AI 行为和进攻节奏 | `EnemyAI.cs` |
| 地图布局和随机规则 | `Game.BuildWorld()`、`MapSettings` |
| 单位/建筑外观 | `Unit.BuildBody()`、`Building.Spawn()`、`Gfx.cs` |
| 胜负条件 | `Game.CheckEnd()` |
| LLM 军令接口 | `Unit.CommandMove()`、`Unit.CommandAttack()` |

## 6. 修改时的注意事项

- 单位和建筑都实现 `ITargetable`，新增攻击目标时优先复用这个接口。
- 全局列表由 `Game` 持有；实体销毁时依靠各自的 `OnDestroy()` 移除自己。
- 新增可训练单位时，先在 `GameConfig` 创建 `UnitDef`，再放入对应 `BuildingDef.train`。
- 新增科技时，创建 `TechDef`，加入 `GameConfig.Techs`，再把 ID 放入建筑的 `techs`。
- 远程攻击不要直接在单位里瞬间扣血，应使用 `Game.SpawnProjectile()` 保持弹道表现一致。
- 只有玩家阵营的提示才应调用 `Game.Toast()`，AI 不应污染玩家提示。
- 调试地图布局时关闭地图随机，固定种子可以复现同一布局。
- 代码依赖 Unity 旧版输入 API；若输入无响应，检查 README 中的 Active Input Handling 设置。

## 7. 一句话总结

这是一个“数据由 `Common` 集中配置、规则由 `Game` 统筹、实体由 `Unit/Worker/Building` 执行、玩家和 AI 通过统一命令入口驱动”的小型 RTS 架构。