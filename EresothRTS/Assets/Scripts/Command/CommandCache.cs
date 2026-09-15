using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Eresoth
{
    /// <summary>指令缓存：玩家标记"成功"的指令落盘 command_cache.json（项目根，可手动编辑），
    /// 记录输入文本与对应的格式化指令 JSON。命中规则：输入 Trim 后完全一致 → 直接回放缓存结果，
    /// 跳过 LLM 调用（省延迟、省 token）。标记"失败"会移除对应条目，防止错误指令被拦截。
    /// 注意：缓存回放不感知当前战局，军团名等引用是标记那一刻的快照，跨局使用前请确认。</summary>
    public static class CommandCache
    {
        [Serializable]
        public class Entry
        {
            public string input;    // 玩家指令原文（Trim 后），匹配键
            public string json;     // 格式化指令（LLM 输出原文或本地兜底序列化）
            public string ts;       // 最近一次写入时间
            public int hits;        // 命中回放次数（勾选常用指令时按它排序）
        }

        [Serializable]
        class CacheFile { public List<Entry> entries = new(); }

        const int MaxEntries = 500;
        static readonly List<Entry> entries = new();
        static bool loaded;

        static string FilePath => Path.Combine(Application.dataPath, "../command_cache.json");

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                var f = JsonUtility.FromJson<CacheFile>(File.ReadAllText(FilePath));
                if (f?.entries != null) entries.AddRange(f.entries);
            }
            catch { /* 缓存文件不存在或损坏：从空开始 */ }
        }

        /// <summary>查缓存：命中返回缓存的格式化 JSON（并累计命中数），未命中返回 null。</summary>
        public static string Lookup(string input)
        {
            EnsureLoaded();
            input = Normalize(input);
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].input == input)
                {
                    entries[i].hits++;
                    if (entries[i].hits % 5 == 0) Save();   // 命中计数定期落盘，降低写文件频率
                    return entries[i].json;
                }
            return null;
        }

        /// <summary>写入/更新缓存条目（标记成功时调用）。</summary>
        public static void Put(string input, string json)
        {
            EnsureLoaded();
            input = Normalize(input);
            var e = entries.Find(x => x.input == input);
            if (e != null)
            {
                e.json = json;
                e.ts = Now();
            }
            else
            {
                entries.Add(new Entry { input = input, json = json, ts = Now(), hits = 0 });
                if (entries.Count > MaxEntries) entries.RemoveAt(0);
            }
            Save();
        }

        /// <summary>移除缓存条目（标记失败时调用）。</summary>
        public static void Remove(string input)
        {
            EnsureLoaded();
            if (entries.RemoveAll(x => x.input == Normalize(input)) > 0) Save();
        }

        /// <summary>全部条目按命中次数降序（常用指令配置界面用）。</summary>
        public static List<Entry> EntriesByHits()
        {
            EnsureLoaded();
            var list = new List<Entry>(entries);
            list.Sort((a, b) => b.hits.CompareTo(a.hits));
            return list;
        }

        static string Normalize(string s) => s == null ? "" : s.Trim();
        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(new CacheFile { entries = entries }, true)); }
            catch { /* 写盘失败不影响游戏 */ }
        }
    }
}
