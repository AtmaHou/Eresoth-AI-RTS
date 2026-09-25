using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Eresoth
{
    /// <summary>Prompt 构造器：角色设定 + 输出契约 + few-shot。
    /// 内容外置在 EresothRTS/prompts/ 三个文本文件（system_prompt.txt / user_template.txt / examples.txt），
    /// 改文件即热重载（按修改时间检测，下一条指令生效），可脱离代码独立调试 prompt；
    /// 文件缺失时回退内嵌默认（与文件内容保持一致，打包发布无 prompts 目录也能跑）。
    /// 关键设计：Prompt 里不放任何静态命令清单——可用动作/目标/兵种/科技全部随 digest 的 vocab 动态下发，
    /// 新增能力 = 加注册表词条，而不是改这段 Prompt。</summary>
    public static class PromptBuilder
    {
        // ---------- 内嵌默认（与 prompts/*.txt 保持一致；改 prompt 请优先改文件） ----------

        const string DefaultSystem = @"# 角色
你是即时战略游戏（RTS）的 AI 参谋。玩家是战场指挥官，用中文自然语言下达命令。
你的唯一职责：把命令翻译成结构化 JSON 军令，交给本地系统校验执行。

# 输出契约
只输出一个 JSON 对象，此外不输出任何文字：
{
  ""orders"": [军事军令列表，作用于军团，可为空],
  ""economy"": [经济计划列表，作用于阵营，可为空],
  ""clarification_needed"": false,
  ""question"": null,
  ""player_reply"": ""用参谋口吻给玩家的一句中文回复""
}

# 军事军令（orders 中的对象）
- force_id（必填）：军团 id，从 user 消息 vocab.forces_list 中选；玩家说""全军""时给每个军团各发一条。
- action（必填）：从 vocab.actions 中选。
- target_id（可选）：目标语义点，从 vocab.targets 中选。
- unit_filter（可选）：只动军团内的某类兵种，取值 infantry/ranged/cavalry/worker。
- target_ref（可选）：模糊目标指代，从 vocab.target_refs 中选；仅 attack/focus_fire 用，如""集火英雄""→""hero""。
- stance（可选）：aggressive/defensive/cautious，缺省 defensive。
- priority（可选）：整数，缺省 50。
- expires_after_seconds（可选）：军令超时秒数。
- conditions（可选）：触发器列表，玩家说""如果……就……""时必须放这里：
  [{""when"":{""metric"":…,""subject"":…,""op"":""<|<=|>|>="",""value"":…},""then"":{""action"":…,""target_id"":…}}]
  metric 从 vocab.metrics 选：
    enemy_count_near  附近敌军数（subject=""force""）
    ally_health_ratio 军团血量比 0~1
    resource          资源量（subject=""wood""|""mana""）
    building_hp_ratio 建筑血量比（subject=建筑 kind）
    enemy_visible     某语义点出现敌人（subject=语义点 id）
    time_elapsed      军令已执行秒数

侦察：action=""scout""，派 1 个移速最快的指定兵种沿地图侦察一圈（途经敌方矿点与主基地），遇敌情上报。

编制调整：action=""reorganize""，把单位编入另一个军团，提交即生效：
- force_id=目标军团；source_id=来源军团（""__all__""=除目标外全部）
- ratio=抽调比例 0~1（0.5=一半；0 或缺省=全部符合条件者）
- unit_filter 或 target_ref=""hero"" 限定抽调对象

# 经济计划（economy 中的对象）
{""action"":""train"",""target_id"":兵种id,""count"":数量}
{""action"":""research"",""target_id"":科技id}
{""action"":""build"",""target_id"":建筑kind,""count"":数量（可建多座时）,""worker_count"":抽调工人数}
{""action"":""assign_workers"",""resource"":""wood|mana|both"",""ratio"":0~1占比,""worker_count"":工人数}
{""action"":""repair"",""target_id"":建筑kind（可选，缺省=受损最重的一座）,""worker_count"":工人数（缺省2）}
说明：assign_workers 的 resource=""both"" 时按 ratio 采魔法矿、其余采木；worker_count 缺省=只安排空闲工人，-1=全体重排。
""依次造A和B""：每条计划设相同 batch（自定字符串）与递增 sequence，系统按序执行。

# 规则
1. 只能使用 user 消息 vocab 中列出的 id：force_id/action/target_id/兵种/科技/建筑/metric，禁止编造。
2. 玩家没指定军团时，根据上下文选最合适的；""全军""=给每个军团各下一条。
3. 玩家表达""如果……就……""时，必须写进 conditions，不要忽略。
4. 仅当语义严重不明、可能灾难性误执行时，才 clarification_needed=true 并在 question 里追问；轻微歧义自行合理推断，并在 player_reply 里说明你的理解。
5. 玩家在问问题（而不是下命令）时：orders/economy 留空，把回答写进 player_reply。
6. player_reply 是参谋回话：简短、确认要点、说明预案（如""发现主力即撤回""）。
7. 玩家口语与 vocab 词条有出入时，优先匹配 vocab 中各词条的 aliases 取最接近者，并在 player_reply 里点明你的理解。";

        const string DefaultExamples = @"玩家：""二军团去东矿，遇到主力就撤""
→ orders:[{""force_id"":""army_2"",""action"":""attack_move"",""target_id"":""enemy_east_mana"",""conditions"":[{""when"":{""metric"":""enemy_count_near"",""subject"":""force"",""op"":"">="",""value"":8},""then"":{""action"":""retreat"",""target_id"":""own_retreat_point""}}]}]

玩家：""补四个弓箭手，攻击科技优先""
→ economy:[{""action"":""train"",""target_id"":""archer"",""count"":4},{""action"":""research"",""target_id"":""human_atk""}]

玩家：""拉两个农夫去开个分矿""
→ economy:[{""action"":""build"",""target_id"":""resource_hub"",""worker_count"":2}]

玩家：""派个骑兵去侦查一圈""
→ orders:[{""force_id"":""army_1"",""action"":""scout"",""unit_filter"":""cavalry""}]

玩家：""一军团一半的兵编入二队""
→ orders:[{""force_id"":""army_2"",""action"":""reorganize"",""source_id"":""army_1"",""ratio"":0.5}]

玩家：""所有的骑兵编入二队""
→ orders:[{""force_id"":""army_2"",""action"":""reorganize"",""source_id"":""__all__"",""unit_filter"":""cavalry""}]";

        const string DefaultUserTemplate = @"【当前战场态势】
{digest}

【最近对话】
{history}

【参考示例】（仅供格式参考，不是本轮输入）
{examples}

【本轮玩家命令】
{player_text}";

        // ---------- 文件加载（热重载：按修改时间检测） ----------

        class CacheEntry { public string content; public DateTime stamp; }
        static readonly Dictionary<string, CacheEntry> cache = new();

        static string PromptDir => Path.Combine(Application.dataPath, "../prompts");

        static string Load(string name, string fallback, out bool fromFile)
        {
            fromFile = false;
            try
            {
                string p = Path.Combine(PromptDir, name);
                if (!File.Exists(p)) return fallback;
                var stamp = File.GetLastWriteTimeUtc(p);
                if (cache.TryGetValue(name, out var e) && e.stamp == stamp)
                { fromFile = true; return e.content; }
                string content = File.ReadAllText(p).Trim();
                if (content.Length == 0) return fallback;
                cache[name] = new CacheEntry { content = content, stamp = stamp };
                fromFile = true;
                return content;
            }
            catch { return fallback; }
        }

        public static string SystemPrompt() => Load("system_prompt.txt", DefaultSystem, out _);
        static string Examples() => Load("examples.txt", DefaultExamples, out _);

        /// <summary>按 user 模板拼装：{digest} {history} {examples} {player_text} 四个占位符。</summary>
        public static string UserPrompt(string playerText, string digestJson, string historyText)
        {
            string tpl = Load("user_template.txt", DefaultUserTemplate, out _);
            return tpl.Replace("{digest}", digestJson ?? "")
                      .Replace("{history}", string.IsNullOrEmpty(historyText) ? "（无）" : historyText)
                      .Replace("{examples}", Examples())
                      .Replace("{player_text}", playerText ?? "");
        }

        /// <summary>当前生效 prompt 的来源戳（llm_log 落盘用）：文件=文件名@修改时间，缺失=embedded。</summary>
        public static string SourceStamp()
        {
            string Stamp(string name, string fallback)
            {
                Load(name, fallback, out bool fromFile);
                if (!fromFile) return name + "=embedded";
                try { return name + "@" + File.GetLastWriteTime(Path.Combine(PromptDir, name)).ToString("MM-dd HH:mm"); }
                catch { return name; }
            }
            return Stamp("system_prompt.txt", DefaultSystem)
                 + " | " + Stamp("user_template.txt", DefaultUserTemplate)
                 + " | " + Stamp("examples.txt", DefaultExamples);
        }
    }
}
