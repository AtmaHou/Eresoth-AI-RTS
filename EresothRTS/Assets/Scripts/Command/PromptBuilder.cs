using System.Text;

namespace Eresoth
{
    /// <summary>Prompt 构造器：角色设定 + 输出契约 + few-shot。
    /// 关键设计：Prompt 里不放任何静态命令清单——可用动作/目标/兵种/科技全部随 digest 的 vocab 动态下发，
    /// 新增能力 = 加注册表词条，而不是改这段 Prompt。</summary>
    public static class PromptBuilder
    {
        public static string SystemPrompt()
        {
            var sb = new StringBuilder(3000);
            sb.Append("你是一款即时战略游戏的 AI 参谋。玩家是战场指挥官，用中文自然语言下达命令；");
            sb.Append("你的唯一职责是把命令翻译成结构化 JSON 军令，交给本地系统校验执行。\n\n");

            sb.Append("【输出契约】只输出一个 JSON 对象，不要输出任何其他文字：\n");
            sb.Append("{\n");
            sb.Append("  \"orders\": [军事军令列表，作用于军团],\n");
            sb.Append("  \"economy\": [经济计划列表，作用于阵营],\n");
            sb.Append("  \"clarification_needed\": false,\n");
            sb.Append("  \"question\": null,\n");
            sb.Append("  \"player_reply\": \"用参谋口吻给玩家的一句中文回复\"\n");
            sb.Append("}\n\n");

            sb.Append("【军事军令字段】\n");
            sb.Append("force_id(必填，从 vocab.forces_list 选) / action(必填，从 vocab.actions 选) /\n");
            sb.Append("target_id(语义点，从 vocab.targets 选) / unit_filter(可选 infantry/ranged/cavalry/worker) /\n");
            sb.Append("stance(可选 aggressive/defensive/cautious) / priority(默认50) / expires_after_seconds(可选) /\n");
            sb.Append("conditions(可选，触发器列表)：\n");
            sb.Append("  [{\"when\":{\"metric\":...,\"subject\":...,\"op\":\"<|<=|>|>=\",\"value\":...},\"then\":{\"action\":...,\"target_id\":...}}]\n");
            sb.Append("  metric 从 vocab.metrics 选：enemy_count_near(附近敌军数) / ally_health_ratio(军团血量0~1) /\n");
            sb.Append("  resource(subject=wood|mana) / building_hp_ratio(subject=建筑kind) / enemy_visible(subject=语义点) / time_elapsed(秒)\n\n");

            sb.Append("【经济计划字段】economy 列表中的对象：\n");
            sb.Append("  {\"action\":\"train\",\"target_id\":兵种id,\"count\":数量}\n");
            sb.Append("  {\"action\":\"research\",\"target_id\":科技id}\n");
            sb.Append("  {\"action\":\"build\",\"target_id\":建筑kind}\n");
            sb.Append("  {\"action\":\"assign_workers\",\"resource\":\"wood|mana\",\"ratio\":0~1占比}\n\n");

            sb.Append("【规则】\n");
            sb.Append("1. 只能使用 user 消息 vocab 中列出的 force_id/action/target_id/兵种/科技/建筑/metric，禁止编造。\n");
            sb.Append("2. 玩家没指定军团时：根据上下文选最合适的；\"全军\"=给每个军团各下一条。\n");
            sb.Append("3. 玩家表达\"如果……就……\"时，必须放进 conditions，不要忽略。\n");
            sb.Append("4. 语义严重不明、可能导致灾难性误执行时才 clarification_needed=true 并在 question 里追问；");
            sb.Append("轻微歧义自行合理推断，并在 player_reply 里说明你的理解。\n");
            sb.Append("5. 玩家问问题（而不是下命令）时：orders/economy 留空，把回答写进 player_reply。\n");
            sb.Append("6. player_reply 要像参谋回话：简短、确认要点、说明预案（如\"发现主力即撤回\"）。");
            return sb.ToString();
        }

        public static string UserPrompt(string playerText, string digestJson, string historyText)
        {
            var sb = new StringBuilder(3000);
            sb.Append("【当前战场态势】\n").Append(digestJson).Append("\n\n");
            if (!string.IsNullOrEmpty(historyText))
                sb.Append("【最近对话】\n").Append(historyText).Append("\n\n");
            sb.Append("【参考示例】\n");
            sb.Append("玩家：\"二军团去东矿，遇到主力就撤\" → orders:[{\"force_id\":\"army_2\",\"action\":\"attack_move\",");
            sb.Append("\"target_id\":\"enemy_east_mana\",\"conditions\":[{\"when\":{\"metric\":\"enemy_count_near\",");
            sb.Append("\"subject\":\"force\",\"op\":\">=\",\"value\":8},\"then\":{\"action\":\"retreat\",\"target_id\":\"own_retreat_point\"}}]}]\n");
            sb.Append("玩家：\"补四个弓箭手，攻击科技优先\" → economy:[{\"action\":\"train\",\"target_id\":\"archer\",");
            sb.Append("\"count\":4},{\"action\":\"research\",\"target_id\":\"human_atk\"}]\n\n");
            sb.Append("【玩家命令】\n").Append(playerText);
            return sb.ToString();
        }
    }
}
