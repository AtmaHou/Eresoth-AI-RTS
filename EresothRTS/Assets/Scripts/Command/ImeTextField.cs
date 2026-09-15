using UnityEngine;

namespace Eresoth
{
    /// <summary>支持中文输入法的极简输入框。IMGUI 的 GUI.TextField 在编辑器 Game 视图及部分
    /// Windows 构建里收不到 IME 合成串（表现为只能输英文/字母），这里改为手动管理文本：
    /// 合成串（Input.compositionString）实时拼在末尾显示，选词提交时写入文本。
    /// 编辑能力刻意保持最小：末尾追加 / 退格 / Ctrl+V 粘贴，足够命令输入场景；
    /// 不支持光标定位与多行，需要富文本编辑的场景不要用本控件。</summary>
    public static class ImeTextField
    {
        static string prevComp = "";   // 上一帧 IME 合成串（跨控件隔离）
        static string lastCtrl = "";

        /// <summary>绘制输入框并处理输入，text 为已确认文本，返回新的已确认文本。</summary>
        public static string Draw(Rect r, string text, string ctrlName, GUIStyle style, int maxLen = 200)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                GUI.FocusControl(ctrlName);
                e.Use();
            }

            bool focused = GUI.GetNameOfFocusedControl() == ctrlName;
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

            // 显示：内容超出框宽只展示末尾（光标恒在末尾），聚焦时画光标
            float charW = Mathf.Max(6f, style.fontSize * 0.52f);
            int fit = Mathf.Max(4, (int)((r.width - 14) / charW));
            string shown = text + comp;
            string disp = shown.Length > fit ? "…" + shown.Substring(shown.Length - fit) : shown;
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 4, r.y + 2, r.width - 8, r.height - 4), disp + (focused ? "▏" : ""), style);
            return text;
        }
    }
}
