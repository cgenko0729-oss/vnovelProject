using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 极简单行输入弹窗（Unity 没有内置的「带输入框的 DisplayDialog」）。
    /// 用 ShowModalUtility 阻塞到用户确认/取消，返回值 = 输入内容，取消返回 null。
    /// </summary>
    public class VNTextPromptWindow : EditorWindow
    {
        const string FocusControl = "VNTextPrompt.Field";

        string _label = "";
        string _value = "";
        string _result;
        bool _focused;

        /// <summary>弹窗并阻塞；返回 null = 取消或留空</summary>
        public static string Prompt(string title, string label, string defaultValue)
        {
            var window = CreateInstance<VNTextPromptWindow>();
            window.titleContent = new GUIContent(title);
            window._label = label;
            window._value = defaultValue ?? "";
            window.position = new Rect(
                Screen.currentResolution.width * 0.5f - 170f,
                Screen.currentResolution.height * 0.5f - 40f, 340f, 84f);
            window.ShowModalUtility();
            return string.IsNullOrEmpty(window._result) ? null : window._result;
        }

        void OnGUI()
        {
            var e = Event.current;
            bool submit = e.type == EventType.KeyDown &&
                          (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            bool cancel = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;

            GUILayout.Space(8f);
            GUILayout.Label(_label, EditorStyles.boldLabel);
            GUI.SetNextControlName(FocusControl);
            _value = EditorGUILayout.TextField(_value);
            if (!_focused && e.type == EventType.Repaint)
            {
                _focused = true;
                EditorGUI.FocusTextInControl(FocusControl);
            }

            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("取消", GUILayout.Width(70f)) || cancel)
                {
                    _result = null;
                    Close();
                    return;
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_value)))
                {
                    if (GUILayout.Button("确定", GUILayout.Width(70f)) ||
                        (submit && !string.IsNullOrWhiteSpace(_value)))
                    {
                        _result = _value.Trim();
                        Close();
                        return;
                    }
                }
            }
        }
    }
}
