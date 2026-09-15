using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Eresoth
{
    /// <summary>常用指令列表：开始界面配置（手动输入 / 默认样例 / 从指令缓存勾选），
    /// 落盘 favorite_commands.json（项目根，可手动编辑）。对局内指挥台"高频命令"区优先展示，
    /// 点击即填入输入框。上限 12 条。</summary>
    public static class FavoriteCommands
    {
        public const int Max = 12;

        [Serializable]
        class FavFile { public List<string> favorites = new(); }

        static List<string> items = new();
        static bool loaded;

        public static string FilePath => Path.Combine(Application.dataPath, "../favorite_commands.json");

        /// <summary>当前已配置的常用指令（首次访问时从本机文件加载）。</summary>
        public static List<string> Items
        {
            get { EnsureLoaded(); return items; }
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                var f = JsonUtility.FromJson<FavFile>(File.ReadAllText(FilePath));
                if (f?.favorites != null) items = f.favorites;
            }
            catch { /* 文件不存在或损坏：从空列表开始 */ }
        }

        /// <summary>添加一条常用指令：去重、去空白、超限返回 false。</summary>
        public static bool Add(string command)
        {
            EnsureLoaded();
            command = command?.Trim();
            if (string.IsNullOrEmpty(command) || items.Contains(command) || items.Count >= Max) return false;
            items.Add(command);
            Save();
            return true;
        }

        public static void RemoveAt(int index)
        {
            EnsureLoaded();
            if (index < 0 || index >= items.Count) return;
            items.RemoveAt(index);
            Save();
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(new FavFile { favorites = items }, true)); }
            catch { /* 写盘失败不影响游戏 */ }
        }
    }
}
