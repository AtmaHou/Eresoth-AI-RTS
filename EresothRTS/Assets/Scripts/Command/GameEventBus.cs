using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>事件严重级别：Info=普通动态，Warning=需要注意，Critical=必须立即响应（如基地遇袭）。</summary>
    public enum EventSeverity { Info, Warning, Critical }

    /// <summary>战场事件类型。条件触发、战报、态势摘要三方共用同一张词表。</summary>
    public enum GameEventType
    {
        EnemySpotted,       // 发现敌人
        BaseUnderAttack,    // 主基地遇袭（Critical）
        ResourceDepleted,   // 资源点枯竭
        ForceEngaged,       // 军团接战
        ForceRetreated,     // 军团撤退
        OrderCompleted,     // 军令完成
        OrderFailed,        // 军令失败（detail 带原因）
        BuildingDestroyed,  // 建筑被摧毁
        TechCompleted,      // 科技研究完成
        TargetLost,         // 目标消失
        ResourceShortage,   // 资源不足
        ManualOverride      // 手动接管/命令被覆盖
    }

    /// <summary>一条战场事件：时间、位置、归属视角、严重级别、相关对象 ID、人类可读描述。</summary>
    public class GameEvent
    {
        public GameEventType type;
        public Team team;           // 事件归属方（谁的视角）：主基地被打 = 被打的一方
        public Vector3 pos;
        public float time;          // Time.time
        public EventSeverity severity;
        public string subjectId;    // 相关实体/军团/军令 ID，可空
        public string detail;       // 人类可读描述（战报直接用）
    }

    /// <summary>全局事件总线：静态发布/订阅 + 环形日志（容量 200）。
    /// 条件触发（ForceController）、战报（GameHUD）、态势摘要（S3）共用此总线。</summary>
    public static class GameEventBus
    {
        public const int LogCapacity = 200;

        static readonly List<GameEvent> log = new();

        /// <summary>订阅事件。注意在 OnDestroy 中退订，避免场景重载后重复回调。</summary>
        public static event System.Action<GameEvent> OnEvent;

        /// <summary>发布事件：入环形日志并通知订阅者。任何模块都可调用。</summary>
        public static void Publish(GameEvent e)
        {
            if (e == null) return;
            if (log.Count >= LogCapacity) log.RemoveAt(0);
            log.Add(e);
            OnEvent?.Invoke(e);
        }

        /// <summary>便捷发布：减少调用方样板代码。</summary>
        public static void Publish(GameEventType type, Team team, Vector3 pos,
            EventSeverity severity = EventSeverity.Info, string subjectId = null, string detail = null)
        {
            Publish(new GameEvent
            {
                type = type, team = team, pos = pos, time = Time.time,
                severity = severity, subjectId = subjectId, detail = detail
            });
        }

        /// <summary>查某阵营 sinceTime 以来的事件（按时间升序）。战报与态势摘要用。</summary>
        public static List<GameEvent> Recent(Team team, float sinceTime)
        {
            var list = new List<GameEvent>();
            for (int i = 0; i < log.Count; i++)
                if (log[i].team == team && log[i].time >= sinceTime) list.Add(log[i]);
            return list;
        }

        /// <summary>全部日志（HUD 调试面板用，按时间升序）。</summary>
        public static IReadOnlyList<GameEvent> All => log;

        /// <summary>清空日志：新一局开始时调用。</summary>
        public static void ClearLog() => log.Clear();
    }
}
