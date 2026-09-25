using UnityEngine;

namespace Eresoth
{
    /// <summary>支持中文输入法的极简输入框。IMGUI 的 GUI.TextField 在编辑器 Game 视图及部分
    /// Windows 构建里收不到 IME 合成串（表现为只能输英文/字母），这里改为手动管理文本：
    /// 合成串（Input.compositionString）实时拼在末尾显示，选词提交时写入文本。
    /// 编辑能力刻意保持最小：末尾追加 / 退格 / Ctrl+V 粘贴，足够命令输入场景；
    /// 不支持光标定位与多行，需要富文本编辑的场景不要用本控件。
    /// 焦点走 GUIUtility.keyboardControl + GetControlID（与原生 TextField 一致）：
    /// GUI.FocusControl/GetNameOfFocusedControl 依赖 SetNextControlName 注册的命名控件表，
    /// 纯自定义控件没有注册过名字，那两个 API 会静默失效（点了没光标、敲不进字）。</summary>
    public static class ImeTextField
    {
        static string prevComp = "";   // 上一帧 IME 合成串（跨控件隔离）
        static string lastCtrl = "";
        static int lastDrawnId = -1;   // Draw（OnGUI 内）算出的控件 ID，供 Update 等非 OnGUI 上下文查焦点
        static string lastDrawnName = "";

        static int IdOf(string ctrlName) => GUIUtility.GetControlID(ctrlName.GetHashCode(), FocusType.Keyboard);

        /// <summary>控件是否持有键盘焦点。Update 等非 OnGUI 上下文禁止调 GetControlID（会抛 ArgumentException），
        /// 这里用 Draw 时缓存的 ID 比较（keyboardControl 本身任意时刻可读）。</summary>
        public static bool IsFocused(string ctrlName)
            => lastDrawnName == ctrlName && lastDrawnId >= 0 && GUIUtility.keyboardControl == lastDrawnId;

        /// <summary>聚焦指定控件（FocusControl 对未注册的自定义控件名无效，用本方法代替）。</summary>
        public static void Focus(string ctrlName)
        {
            if (lastDrawnName == ctrlName && lastDrawnId >= 0) GUIUtility.keyboardControl = lastDrawnId;
            else if (Event.current != null) GUIUtility.keyboardControl = IdOf(ctrlName);
        }

        /// <summary>绘制输入框并处理输入，text 为已确认文本，返回新的已确认文本。
        /// placeholder：空且未聚焦时的灰色提示语；聚焦时显示闪烁光标（合成串期间常亮）。</summary>
        public static string Draw(Rect r, string text, string ctrlName, GUIStyle style, int maxLen = 200, string placeholder = null)
        {
            var e = Event.current;
            int id = IdOf(ctrlName);
            lastDrawnId = id; lastDrawnName = ctrlName;   // 供 IsFocused 在非 OnGUI 上下文使用
            bool focused = GUIUtility.keyboardControl == id;
            if (e.type == EventType.MouseDown)
            {
                if (r.Contains(e.mousePosition))
                {
                    GUIUtility.keyboardControl = id;
                    e.Use();
                }
                else if (focused) GUIUtility.keyboardControl = 0;   // 点到输入框外：失焦，游戏快捷键恢复
                focused = GUIUtility.keyboardControl == id;
            }
            // 聚焦时把键盘路由给输入法（手动模式），失焦恢复自动，避免游戏快捷键被 IME 吞掉
            Input.imeCompositionMode = focused ? IMECompositionMode.On : IMECompositionMode.Auto;
            if (lastCtrl != ctrlName) { prevComp = ""; lastCtrl = ctrlName; }

            // 合成提交：合成串从非空变为空（空格/数字选词上屏）时，把上一轮合成内容写入文本
            string comp = Input.compositionString ?? "";
            if (prevComp.Length > 0 && comp.Length == 0) text += prevComp;
            prevComp = comp;

            if (focused && e.type == EventType.KeyDown)
            {
                if (comp.Length > 0)
                {
                    e.Use();   // 合成中：按键全部交给输入法，避免误触发发送/镜头快捷键
                }
                else if (e.control && e.keyCode == KeyCode.V)
                {
                    string clip = GUIUtility.systemCopyBuffer;
                    if (!string.IsNullOrEmpty(clip))
                    {
                        text += clip;
                        if (text.Length > maxLen) text = text.Substring(0, maxLen);
                    }
                    e.Use();
                }
                else if ((e.keyCode == KeyCode.Backspace || e.character == '\b') && text.Length > 0)
                {
                    text = text.Substring(0, text.Length - 1);
                    e.Use();
                }
                else if (e.character > 0 && e.character != '\r' && e.character != '\n')
                {
                    if (text.Length < maxLen) text += e.character;
                    e.Use();
                }
            }

            // 显示：输入框底色（聚焦更亮）+ 占位提示 + 末尾光标
            GUI.color = focused ? new Color(1f, 1f, 1f, .18f) : new Color(1f, 1f, 1f, .10f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Box(r, GUIContent.none);

            float charW = Mathf.Max(6f, style.fontSize * 0.52f);
            int fit = Mathf.Max(4, (int)((r.width - 14) / charW));
            string shown = text + comp;
            bool showPh = placeholder != null && shown.Length == 0 && !focused;
            string disp = showPh ? placeholder
                : shown.Length > fit ? "…" + shown.Substring(shown.Length - fit) : shown;
            // 光标：合成串期间常亮，空闲按 0.65s 周期闪烁
            bool blinkOn = focused && (comp.Length > 0 || Time.unscaledTime % 1f < 0.65f);
            string cur = showPh ? "" : blinkOn ? "▏" : "";
            GUI.color = showPh ? new Color(.62f, .62f, .66f) : Color.white;
            GUI.Label(new Rect(r.x + 4, r.y + 2, r.width - 8, r.height - 4), disp + cur, style);
            GUI.color = Color.white;
            return text;
        }
    }
}
