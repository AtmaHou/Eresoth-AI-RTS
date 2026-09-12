using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>硬编码 JSON 军令驱动入口（S0 阶段不接模型）。
    /// DTO 字段命名与未来 CommandSchema 同构（force_id/action/target_id/...），S4 接 API 时只换解析来源。
    /// F1~F8 触发示例军令，F10 自动编组，方便演示与回归测试。</summary>
    public class DebugCommandRunner : MonoBehaviour
    {
        // ---------- CommandSchema 同构 DTO（JsonUtility 可反序列化） ----------

        [Serializable]
        public class WhenDto
        {
            public string metric;     // enemy_count_near/ally_health_ratio/resource/building_hp_ratio/enemy_visible/time_elapsed
            public string subject;    // force / 语义点ID / wood / mana / 建筑kind
            public string op;         // < <= > >=
            public float value;
        }

        [Serializable]
        public class ThenDto
        {
            public string action;
            public string target_id;
        }

        [Serializable]
        public class ConditionDto
        {
            public WhenDto when;            // Schema v2：谓词条件
            public ThenDto then;
            // 兼容旧版字段（when 为字符串的老 JSON）
            public string legacy_when;
            public float threshold;
        }

        [Serializable]
        public class OrderDto
        {
            public string force_id;
            public string action;          // 军事 8 种 + 经济 train/research/build/assign_workers
            public string target_id;       // 军事：语义点；经济：unit_id/tech_id/building_kind
            public string unit_filter;     // worker/infantry/ranged/cavalry，空 = 全军团
            public int priority = 50;
            public string stance = "defensive";   // aggressive/defensive/cautious
            public float expires_after_seconds;
            public int count;              // train 用
            public float ratio;            // assign_workers 用
            public string resource;        // assign_workers：wood/mana
            public List<ConditionDto> conditions = new();
        }

        [Serializable]
        public class CommandRequest
        {
            public string player_text;
            public List<OrderDto> orders = new();
            public List<OrderDto> economy = new();   // Schema v2：经济计划独立列表
            public bool clarification_needed;        // 模型请求追问（LLM 输出用）
            public string question;                  // 追问内容
            public string player_reply;              // 参谋回复（LLM 输出用）
        }

        // ---------- 示例军令（对应 MVP 命令 1/3/4/5/7/13/14） ----------

        static readonly (KeyCode key, string json)[] Samples =
        {
            (KeyCode.F1, @"{""player_text"":""一军团防守己方主基地"",""orders"":[{""force_id"":""army_1"",""action"":""defend"",""target_id"":""own_main_base""}]}"),
            (KeyCode.F2, @"{""player_text"":""二军团去东矿，遇到主力就撤"",""orders"":[{""force_id"":""army_2"",""action"":""attack_move"",""target_id"":""enemy_east_mana"",""conditions"":[{""when"":{""metric"":""enemy_count_near"",""subject"":""force"",""op"":"">="",""value"":8},""then"":{""action"":""retreat"",""target_id"":""own_retreat_point""}}]}]}"),
            (KeyCode.F3, @"{""player_text"":""一军团全员集火最高价值目标"",""orders"":[{""force_id"":""army_1"",""action"":""focus_fire"",""stance"":""aggressive""}]}"),
            (KeyCode.F4, @"{""player_text"":""二军团防守中场"",""orders"":[{""force_id"":""army_2"",""action"":""defend"",""target_id"":""center_field""}]}"),
            (KeyCode.F5, @"{""player_text"":""一军团撤退到后方"",""orders"":[{""force_id"":""army_1"",""action"":""retreat"",""target_id"":""own_retreat_point"",""priority"":60}]}"),
            (KeyCode.F6, @"{""player_text"":""二军团谨慎推进敌方前线，伤亡过半就撤"",""orders"":[{""force_id"":""army_2"",""action"":""attack_move"",""target_id"":""enemy_frontline"",""stance"":""cautious"",""conditions"":[{""when"":{""metric"":""ally_health_ratio"",""op"":""<"",""value"":0.5},""then"":{""action"":""retreat"",""target_id"":""own_retreat_point""}}]}]}"),
            (KeyCode.F7, @"{""player_text"":""一军团的骑兵去骚扰敌方西矿"",""orders"":[{""force_id"":""army_1"",""action"":""attack_move"",""target_id"":""enemy_west_mana"",""unit_filter"":""cavalry""}]}"),
            (KeyCode.F8, @"{""player_text"":""造四个弓箭手再研究攻击科技"",""economy"":[{""action"":""train"",""target_id"":""archer"",""count"":4},{""action"":""research"",""target_id"":""human_atk""}]}"),
        };

        void Update()
        {
            if (Game.I == null || !Game.I.started || Game.I.over) return;

            if (Input.GetKeyDown(KeyCode.F10))
            {
                ForceManager.I?.AutoForm(Game.I.playerTeam);
                return;
            }
            foreach (var (key, json) in Samples)
                if (Input.GetKeyDown(key)) Run(json);
        }

        /// <summary>解析 JSON 军令并提交。非法 JSON / 未知动作 / 未知目标均报可读错误，不崩溃。</summary>
        public bool Run(string json)
        {
            CommandRequest req;
            try { req = JsonUtility.FromJson<CommandRequest>(json); }
            catch (Exception e)
            {
                Game.I.Toast($"军令 JSON 解析失败：{e.Message}");
                return false;
            }
            if (req == null || req.orders.Count == 0)
            { Game.I.Toast("军令为空"); return false; }

            int ok = 0;
            foreach (var dto in req.orders)
            {
                var o = Map(dto, req.player_text, out string err);
                if (o == null) { Game.I.Toast($"军令无效：{err}"); continue; }
                if (OrderDispatcher.I.SubmitOrder(o)) ok++;
            }
            if (req.economy != null)
                foreach (var dto in req.economy)
                {
                    var o = Map(dto, req.player_text, out string err);
                    if (o == null) { Game.I.Toast($"经济计划无效：{err}"); continue; }
                    if (OrderDispatcher.I.SubmitOrder(o)) ok++;
                }
            return ok > 0;
        }

        /// <summary>DTO → Order 映射（Schema v2）；字段不合法时返回 null 并给出原因。</summary>
        public static Order Map(OrderDto dto, string playerText, out string err)
        {
            err = null;
            if (!TryParseAction(dto.action, out var action)) { err = $"未知动作 {dto.action}"; return null; }
            var o = new Order
            {
                playerText = playerText,
                forceId = dto.force_id,
                action = action,
                targetId = dto.target_id,
                priority = dto.priority,
                stance = ParseStance(dto.stance),
                expiresAt = dto.expires_after_seconds > 0 ? Time.time + dto.expires_after_seconds : 0f,
                count = dto.count,
                ratio = dto.ratio,
                resource = dto.resource,
            };
            if (!string.IsNullOrEmpty(dto.unit_filter))
            {
                if (TryParseKind(dto.unit_filter, out var kind)) o.unitFilter = kind;
                else { err = $"未知兵种筛选 {dto.unit_filter}"; return null; }
            }
            if (dto.conditions != null)
                foreach (var c in dto.conditions)
                {
                    var cond = MapCondition(c, out err);
                    if (cond == null) return null;
                    o.conditions.Add(cond);
                }
            return o;
        }

        /// <summary>条件映射：v2 谓词优先；兼容旧版 when 字符串（归一化为谓词，老 JSON 不报废）。</summary>
        static OrderCondition MapCondition(ConditionDto c, out string err)
        {
            err = null;
            string thenAction = c.then?.action;
            if (!TryParseAction(thenAction, out var then)) { err = $"条件动作未知 {thenAction}"; return null; }
            var cond = new OrderCondition { then = then, thenTargetId = c.then?.target_id };

            if (c.when != null && !string.IsNullOrEmpty(c.when.metric))
            {
                if (!TryParseMetric(c.when.metric, out var m)) { err = $"未知指标 {c.when.metric}"; return null; }
                if (!TryParseOp(c.when.op, out var op)) { err = $"未知比较符 {c.when.op}"; return null; }
                cond.metric = m; cond.op = op; cond.value = c.when.value; cond.subject = c.when.subject;
                return cond;
            }
            // 旧版兼容
            switch (c.legacy_when)
            {
                case "enemy_main_force_seen":
                    cond.metric = ConditionMetric.EnemyCountNear; cond.subject = "force";
                    cond.op = ConditionOp.Ge; cond.value = 8; return cond;
                case "self_health_below":
                    cond.metric = ConditionMetric.AllyHealthRatio;
                    cond.op = ConditionOp.Lt; cond.value = c.threshold > 0 ? c.threshold : 0.35f; return cond;
                default:
                    err = "条件缺少 when.metric"; return null;
            }
        }

        static bool TryParseMetric(string s, out ConditionMetric m)
        {
            m = default;
            switch (s)
            {
                case "enemy_count_near": m = ConditionMetric.EnemyCountNear; return true;
                case "ally_health_ratio": m = ConditionMetric.AllyHealthRatio; return true;
                case "resource": m = ConditionMetric.Resource; return true;
                case "building_hp_ratio": m = ConditionMetric.BuildingHpRatio; return true;
                case "enemy_visible": m = ConditionMetric.EnemyVisible; return true;
                case "time_elapsed": m = ConditionMetric.TimeElapsed; return true;
                default: return false;
            }
        }

        static bool TryParseOp(string s, out ConditionOp op)
        {
            op = ConditionOp.Ge;
            switch (s)
            {
                case "<": op = ConditionOp.Lt; return true;
                case "<=": op = ConditionOp.Le; return true;
                case ">": op = ConditionOp.Gt; return true;
                case ">=": op = ConditionOp.Ge; return true;
                default: return false;
            }
        }

        static bool TryParseAction(string s, out OrderAction a)
        {
            a = default;
            switch (s)
            {
                case "move": a = OrderAction.Move; return true;
                case "attack": a = OrderAction.Attack; return true;
                case "attack_move": a = OrderAction.AttackMove; return true;
                case "defend": a = OrderAction.Defend; return true;
                case "retreat": a = OrderAction.Retreat; return true;
                case "focus_fire": a = OrderAction.FocusFire; return true;
                case "regroup": a = OrderAction.Regroup; return true;
                case "hold": a = OrderAction.Hold; return true;
                case "train": a = OrderAction.Train; return true;
                case "research": a = OrderAction.Research; return true;
                case "build": a = OrderAction.Build; return true;
                case "assign_workers": a = OrderAction.AssignWorkers; return true;
                default: return false;
            }
        }

        static ForceStance ParseStance(string s) => s switch
        {
            "aggressive" => ForceStance.Aggressive,
            "cautious" => ForceStance.Cautious,
            _ => ForceStance.Defensive,
        };

        static bool TryParseKind(string s, out UnitKind k)
        {
            k = default;
            switch (s)
            {
                case "worker": k = UnitKind.Worker; return true;
                case "infantry": k = UnitKind.Infantry; return true;
                case "ranged": k = UnitKind.Ranged; return true;
                case "cavalry": k = UnitKind.Cavalry; return true;
                default: return false;
            }
        }
    }
}
