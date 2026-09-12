# 第一阶段代码修改计划（S0 指挥底座 + S1 战术执行 MVP）

> 用途：作为后续让执行模型修改代码时的 Prompt 基准文档。
> 使用方法：每次只让执行模型做一个任务块（Task），把本文件中该任务块 + 相关现有文件源码一起喂给它，不要一次喂整个计划。
> 范围：纯本地、不接任何 LLM API。用硬编码 JSON 驱动。LLM 接入属 S4，不在本阶段。
> 硬约束：不破坏现有人机对局；玩家手动操作行为完全不变；所有新代码放 `Assets/Scripts/Command/` 子目录，namespace 仍为 `Eresoth`。

---

## 0. 给执行模型的全局上下文（每次都要附上）

现有可复用接口（禁止改写其行为，只可调用或做最小扩展）：

```csharp
// Unit.cs
public void CommandMove(Vector3 p);          // 移动命令
public void CommandAttack(ITargetable t);    // 攻击命令（单位/建筑通用）
public bool MoveStep(Vector3 dest, float dt, float stopRadius); // 单步移动，到达返回 true
public Team team; public UnitDef def; public float hp;
public ITargetable target; public Unit lastAttacker;
public float Hp01 => hp / def.hp;            // 经 ITargetable

// Common.cs
public interface ITargetable { Vector3 Pos; float Radius; Team Team; bool Alive; float Hp01; string DisplayName; void Damage(float dmg); }
public enum UnitKind { Worker, Infantry, Ranged, Cavalry }
// UnitDef: id, name, kind, hero, speed, range ...

// Game.cs（单例 Game.I）
public readonly List<Unit> units; public readonly List<Building> buildings; public readonly List<ResourceNode> nodes;
public readonly Vector3[] baseCenter;        // [0]/[1] 双方基地中心
public Team playerTeam;
public static float TerrainHeight(float x, float z);   // 所有定位必须贴地：pos.y = TerrainHeight(x,z)
public void Toast(string msg);                          // 仅玩家侧提示
public Building Hall(Team t); public Building NearestDeposit(Team t, Vector3 p);

// Building.cs
public bool TryTrain(UnitDef def); public bool TryResearch(string techId); public Vector3 rally;

// Worker.cs
public void GatherAt(ResourceNode n); public enum State { Idle, ToNode, Gathering, Returning, Constructing, Building }
public State state;
```

代码风格约定（与现有代码一致）：

- 中文 XML 注释 `<summary>`；字段用公有字段而非属性（除只读计算属性）；MonoBehaviour 单文件一类。
- 数值常量进 `RTSConfig.cs`（并在 `RuntimeConfig.FillDefaults` 兜底），不要散落硬编码。
- 每帧逻辑注意性能：战场最多约 200 单位，禁止每帧 LINQ 分配，用 for 循环。

---

## Task 1：事件总线 `GameEventBus.cs`（最先做，后续全部依赖它）

**新增文件** `Command/GameEventBus.cs`：

```csharp
public enum EventSeverity { Info, Warning, Critical }
public enum GameEventType {
    EnemySpotted, BaseUnderAttack, ResourceDepleted, ForceEngaged, ForceRetreated,
    OrderCompleted, OrderFailed, BuildingDestroyed, TechCompleted,
    TargetLost, ResourceShortage, ManualOverride
}
public class GameEvent {
    public GameEventType type; public Team team;      // 事件归属方（谁的视角）
    public Vector3 pos; public float time;            // Time.time
    public EventSeverity severity;
    public string subjectId;                          // 相关实体/军团/命令 ID，可空
    public string detail;                             // 人类可读描述（战报直接用）
}
public static class GameEventBus {
    public static event System.Action<GameEvent> OnEvent;
    public static void Publish(GameEvent e);          // 触发 + 追加到环形日志（容量 200）
    public static IReadOnlyList<GameEvent> Recent(Team team, float sinceTime); // 战报/摘要用
}
```

**改动点**（只做最小挂钩，每处一两行）：

- `Unit.Damage()`：己方主基地/建筑被打时已有逻辑附近 → 在 `Building.Damage()` 中，若 `kind=="hall"` 且本次伤害来源为敌方，Publish `BaseUnderAttack`（加 5 秒去重，防刷屏）。
- `Building.Damage()` 销毁处：Publish `BuildingDestroyed`。
- `Game.FinishResearch()`：Publish `TechCompleted`。
- `ResourceNode` 耗尽销毁处：Publish `ResourceDepleted`。

**验收**：开局后拆掉一座箭塔、被打一下主基地，日志中能按时间和阵营过滤出对应事件。

---

## Task 2：地图语义点 `SemanticMap.cs`

**新增文件** `Command/SemanticMap.cs`：

```csharp
public class SemanticPoint {
    public string id;          // "own_main_base" 等稳定 ID
    public string displayName; // "己方主基地"
    public Vector3 pos;        // 动态点（如 enemy_frontline）由注册方更新
    public bool isDynamic;
}
public static class SemanticMap {
    public static void Clear();
    public static void Register(string id, string displayName, Vector3 pos, bool isDynamic = false);
    public static void Update(string id, Vector3 pos);
    public static bool TryGet(string idOrAlias, out SemanticPoint p);  // 先查 ID 再查别名表
    public static List<SemanticPoint> All { get; }
    // 别名表（静态字典）："东矿"-> 按玩家阵营解析为 enemy_east_mana 或 own_east_mana（消歧规则：战场方位词默认指敌方半场对应点；"家门口/家里"指己方）
}
```

**改动点** `Game.BuildWorld()` 末尾调用 `SemanticMapRegistrar.Register(this)`（新文件 `Command/SemanticMapRegistrar.cs`）：

- 固定点：`own_main_base` / `enemy_main_base`（= baseCenter，相对 `playerTeam` 命名）、`center_field`（地图中心）、`center_mana`（中央富集矿中心）。
- 资源点：扫描 `nodes`，按象限+资源类型命名，如 `enemy_east_mana`（敌方半场最东 mana 群中心）、`own_west_wood` 等。同名多点取簇中心，ID 加序号。
- 动态点：`own_retreat_point`（= 己方主基地向地图边缘偏移 10）、`enemy_frontline`（初始=敌方主基地，后续由 ForceManager 更新为可见敌军质心；本阶段可先不更新）。

**验收**：固定种子局（randomMap=false）打印 `SemanticMap.All`，每类至少有 1 个点，坐标贴地（y=TerrainHeight）。

---

## Task 3：军团系统 `Force.cs` + `ForceManager.cs`

**新增** `Command/Force.cs`：

```csharp
public enum ForceStance { Aggressive, Defensive, Cautious }   // Cautious=残血主动撤
public class Force {
    public string id;                 // "army_1" ...
    public string name;               // "一军团"
    public Team team;
    public List<Unit> units = new();
    public ForceStance stance = ForceStance.Defensive;
    public Vector3 rallyPoint;        // 集结/撤退点
    public Order currentOrder;        // Task 4 定义
    public float HealthRatio { get; } // 平均 Hp01（for 循环计算）
    public Vector3 Centroid { get; }
    public Dictionary<UnitKind,int> Composition();  // 兵种构成统计
}
```

**新增** `Command/ForceManager.cs`（MonoBehaviour，挂到 Game 物体，`Game.OnEnable` 里 `AddComponent`）：

- `List<Force> forces`；`CreateForce(team, name)`；`Assign(unit, force)`；`Disband(forceId)`。
- `AutoForm(Team team)`：开局或手动触发，把某阵营非工人单位按兵种编 1 个初始军团（MVP 阶段玩家侧 1~2 个即可）。
- 新训练单位自动编入：挂钩 `Unit.Spawn`（在 Spawn 末尾调用 `ForceManager.I?.OnUnitSpawned(unit)`，编入 rallyPoint 所属建筑设置的军团，未设置则进默认军团）。
- `Find(string idOrName)`：支持 "army_1"/"一军团" 查找。
- 每 2 秒更新一次各 Force 的 rallyPoint 默认值（= 当前 Centroid）与 `enemy_frontline` 语义点。

**改动点** `Unit.cs`：加两个公有字段（不改任何逻辑）：

```csharp
public string forceId;        // 所属军团，空=未编组
public float manualOverrideUntil;  // > Time.time 时军团执行体跳过该单位
```

`SelectionManager` 右键命令处加一行：对被选中单位设 `manualOverrideUntil = Time.time + 8f`（8 秒手动接管，之后回归军团控制）。

**验收**：控制台指令或调试按钮触发 `AutoForm(playerTeam)`，军团统计（构成/血量/质心）正确；手动右键一个单位后该单位 8 秒内不受军团命令影响。

---

## Task 4：军令系统 `Order.cs` + `OrderDispatcher.cs`

**新增** `Command/Order.cs`：

```csharp
public enum OrderAction { Move, Attack, AttackMove, Defend, Retreat, FocusFire, Regroup, Hold }
public enum OrderState { Created, Validated, Queued, Executing, Completed, Failed, Cancelled, Overridden }
public class OrderCondition {                       // MVP 只支持两种
    public string when;    // "enemy_main_force_seen" | "self_health_below"
    public float threshold;                          // self_health_below 用（如 0.4f）
    public OrderAction then; public string thenTargetId;  // 触发后动作与语义点
}
public class Order {
    public string id;                 // "ord_###" 自增
    public string playerText;         // 玩家原话快照（硬编码阶段填调试来源说明）
    public string forceId;
    public OrderAction action;
    public string targetId;           // 语义点 ID 或实体寻址（MVP：语义点）
    public UnitKind? unitFilter;      // 只影响军团内该兵种（"骑兵切后排"用）
    public ForceStance stance;
    public int priority;              // 大者优先；手动接管=100，系统紧急=90
    public List<OrderCondition> conditions = new();
    public float expiresAt;           // Time.time 截止，0=不过期
    public OrderState state;
    public string failReason;         // 人类可读失败原因（执行解释用）
    public float createdAt;
}
```

**新增** `Command/OrderDispatcher.cs`（MonoBehaviour）：

- 入口 `SubmitOrder(Order o)`：校验（军团存在/语义点存在/军团非空）→ 与现役命令冲突仲裁（同军团新命令 priority >= 旧的则旧命令置 Overridden 并 Publish `ManualOverride` 事件以外的覆盖日志）→ 置 Executing → 登记到 `Force.currentOrder`。
- `CancelOrder(orderId, reason)`、`CancelForceOrders(forceId, reason)`。
- 军令槽位：`public int maxConcurrentOrdersPerForce = 99;`（本阶段默认近似无限，接口先存在）。
- 命令日志 `OrderJournal`（同文件内部类即可）：环形保存最近 100 条 Order 终态，供"为什么没执行"查询。

**验收**：硬编码构造一条 `AttackMove(enemy_east_mana, conditions:[enemy_main_force_seen -> Retreat own_retreat_point])` 提交后：军团状态、日志、事件三者一致；非法 targetId 被拒绝且 failReason 可读。

---

## Task 5：战术执行体 `ForceController.cs`

**新增** `Command/ForceController.cs`（MonoBehaviour，0.25s  tick，不要每帧跑）：

对每个 `state==Executing` 的 Order 驱动对应 Force：

| Action | 执行逻辑（MVP 简化版） |
|---|---|
| Move / AttackMove | 全体 `CommandMove(目标点)`；AttackMove 途中遇敌不中断（复用 Unit 自动索敌）；质心距目标 < 5 即 Completed |
| Attack | 同 AttackMove，但目标为语义点关联的敌方建筑/单位簇（MVP：语义点 5 半径内最近敌人，无敌人则 Completed） |
| Defend | 以语义点为圆心驻留；敌人进入 12 半径才 `CommandAttack`；**追击上限**：单位离圆心 > 15 立即 `CommandMove` 回圆心 |
| Retreat | 全体 `CommandMove(目标)`；到达 Completed；Publish `ForceRetreated` |
| FocusFire | 每 tick 选"军团 12 半径内最贵的敌方单位"（hero 优先，其次 mana 造价高者），filter 兵种对其 `CommandAttack`；目标死亡重选 |
| Regroup | 全体向 rallyPoint 收拢 |
| Hold | 清除移动，原地驻留 |

通用规则（在 tick 里统一处理，不分散到各 Action）：

1. **手动接管过滤**：`manualOverrideUntil > Time.time` 的单位跳过一切下发。
2. **残血撤退**：stance==Cautious 或命令带 `self_health_below` 条件时，`Hp01 < threshold` 的单位 `CommandMove(Force.rallyPoint)`，回到 0.9 以上才归队。
3. **条件检查**：`enemy_main_force_seen` = 军团质心 15 半径内敌战斗单位数 >= 8；触发即把当前 Order 置 Completed 并 Submit 条件里定义的 then Order。
4. **目标丢失**：Attack/FocusFire 找不到目标 → `TargetLost` 事件 → 有 conditions 则按条件，否则 Order Failed（failReason="目标消失"）。
5. **过期**：`expiresAt` 到点 → Order Expired（按 Cancelled 处理并记录）。

性能：所有"半径内敌人"查询用 for 循环遍历 `Game.I.units`，tick 间隔 0.25s。

**验收**（硬编码驱动，控制台或调试快捷键触发）：

- 命令 1/3/4/5/7/13/14 对应的 JSON 结构能跑通（见 Task 7 映射表）。
- 基地被打事件触发时，回防命令从事件到部队掉头 ≤ 300ms（单独通道：事件总线订阅者直接 Submit 高优先级 Order，不走 tick）。
- 100 单位混战帧率与改动前相当。

---

## Task 6：硬编码 JSON 驱动入口 `DebugCommandRunner.cs`

**新增** `Command/DebugCommandRunner.cs`：

- 定义与 S4 要用的 CommandSchema **同构**的 DTO（`CommandRequest { string playerText; List<OrderDto> orders; }`，字段命名即未来 schema 命名：`force_id/action/target_id/unit_filter/priority/stance/conditions/expires_after_seconds`），用 `JsonUtility` 反序列化。
- `Run(string json)`：解析 → 映射成 Order → OrderDispatcher.SubmitOrder。
- 内置 6~8 条示例 JSON（对应 MVP 命令 1/3/4/5/7/13/14），数字键 F1~F8 触发，方便演示与回归测试。

**验收**：贴入一段 JSON 即驱动战局；非法 JSON/未知 action/未知 target 均报可读错误不崩溃。

---

## Task 7：最小反馈 UI（GameHUD 扩展，不新建系统）

- `GameHUD` 增加一个可折叠调试面板（F9 开关）：列出玩家方各 Force（名称/构成/血量/当前命令与状态）、最近 10 条事件（时间+描述）。
- Order 状态颜色：Executing 白、Completed 绿、Failed 红、Overridden 灰。
- 命令终态时 `Game.Toast()` 一行战报（"一军团 已到达 东侧魔法矿" / "二军团 撤退 失败：目标消失"）。

**验收**：不看 Console 也能知道每条命令的执行状态与失败原因。

---

## 执行顺序与依赖

```text
Task 1 事件总线（无依赖）
  -> Task 2 语义地图（无依赖，可与 1 并行）
  -> Task 3 军团系统（依赖 1 的事件）
  -> Task 4 军令系统（依赖 3）
  -> Task 5 战术执行（依赖 1/2/3/4）
  -> Task 6 JSON 入口（依赖 4/5）
  -> Task 7 反馈 UI（依赖 1/4）
```

## 本阶段总验收（对齐传播点 #1 的雏形）

固定种子开局 → AutoForm 两个军团 → 用 F1~F8 硬编码命令完成：

1. "一军团 Defend 北口，追击不出路口"（命令 4）
2. "二军团 AttackMove 东矿，遇主力 Retreat"（命令 1，条件触发可见）
3. 主基地被打 → 最近军团自动回防（命令 5）
4. 战报面板能回答"二军团刚才为什么撤退"（命令 41 的雏形）

全程不碰鼠标右键指挥战斗单位（允许手动接管验证 override），即达标。

## 明确不做（防止执行模型发散）

- 不接任何网络/API；不做语音；不做经济生产计划（S2）；不做战争迷雾过滤（S3）。
- 不重构 EnemyAI（仅允许 Task 3 的 `OnUnitSpawned` 类最小挂钩）。
- 不改 Unit/Worker/Building 的现有行为逻辑，只允许新增字段与事件挂钩。
- 不引入第三方库（JSON 用 Unity 自带 JsonUtility 即可）。
