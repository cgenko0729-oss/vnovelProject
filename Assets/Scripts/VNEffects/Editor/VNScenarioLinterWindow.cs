using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 剧本校验结果窗口：Tools → VN Effects → Lint Scenarios。
    /// 双击一行直接打开对应 .vn.txt 并定位到出错行。
    /// 分析逻辑全在 VNScenarioLinter，本窗口只负责显示与交互。
    /// </summary>
    public class VNScenarioLinterWindow : EditorWindow
    {
        [MenuItem("Tools/VN Effects/Lint Scenarios %#l", priority = 100)]
        public static void Open()
        {
            var window = GetWindow<VNScenarioLinterWindow>("剧本校验");
            window.minSize = new Vector2(560f, 320f);
            window.Run();
        }

        List<VNLintIssue> _issues = new List<VNLintIssue>();
        Vector2 _scroll;
        bool _showError = true, _showWarning = true, _showInfo = false;
        string _filter = "";
        double _lastRunAt;
        int _lastClicked = -1;
        double _lastClickTime;

        void Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _issues = VNScenarioLinter.LintAll();
            sw.Stop();
            _lastRunAt = EditorApplication.timeSinceStartup;

            int e = _issues.Count(i => i.severity == VNLintSeverity.Error);
            int w = _issues.Count(i => i.severity == VNLintSeverity.Warning);
            int n = _issues.Count(i => i.severity == VNLintSeverity.Info);
            Debug.Log($"[VNLint] 校验完成（{sw.ElapsedMilliseconds} ms）：" +
                      $"错误 {e} / 警告 {w} / 提示 {n}");
            Repaint();
        }

        void OnGUI()
        {
            DrawToolbar();

            var shown = _issues.Where(Visible).ToList();

            if (_issues.Count == 0 && _lastRunAt > 0)
            {
                EditorGUILayout.HelpBox("没有发现问题。", MessageType.Info);
                return;
            }
            if (shown.Count == 0)
            {
                EditorGUILayout.HelpBox("当前筛选条件下没有条目（试试勾上 Info，或清空搜索）。",
                    MessageType.None);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            string lastFile = null;
            for (int i = 0; i < shown.Count; i++)
            {
                var issue = shown[i];
                if (issue.file != lastFile)
                {
                    lastFile = issue.file;
                    EditorGUILayout.Space(6f);
                    EditorGUILayout.LabelField(issue.file + ".vn.txt", EditorStyles.boldLabel);
                }
                DrawIssue(issue, i);
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("重新校验", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                Run();

            int e = _issues.Count(i => i.severity == VNLintSeverity.Error);
            int w = _issues.Count(i => i.severity == VNLintSeverity.Warning);
            int n = _issues.Count(i => i.severity == VNLintSeverity.Info);

            _showError = GUILayout.Toggle(_showError, $"错误 {e}",
                EditorStyles.toolbarButton, GUILayout.Width(72f));
            _showWarning = GUILayout.Toggle(_showWarning, $"警告 {w}",
                EditorStyles.toolbarButton, GUILayout.Width(72f));
            _showInfo = GUILayout.Toggle(_showInfo, $"提示 {n}",
                EditorStyles.toolbarButton, GUILayout.Width(72f));

            GUILayout.Space(8f);
            _filter = GUILayout.TextField(_filter, EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(120f));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        bool Visible(VNLintIssue i)
        {
            if (i.severity == VNLintSeverity.Error && !_showError) return false;
            if (i.severity == VNLintSeverity.Warning && !_showWarning) return false;
            if (i.severity == VNLintSeverity.Info && !_showInfo) return false;
            if (string.IsNullOrEmpty(_filter)) return true;
            return (i.message + i.file + i.code).IndexOf(
                _filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void DrawIssue(VNLintIssue issue, int index)
        {
            var rect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(Icon(issue.severity), GUILayout.Width(20f), GUILayout.Height(18f));
            GUILayout.Label($"第 {issue.line} 行", GUILayout.Width(64f));
            GUILayout.Label(issue.message, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(issue.hint))
            {
                var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
                style.normal.textColor = new Color(0.62f, 0.66f, 0.72f);
                EditorGUILayout.LabelField(issue.hint, style);
            }

            EditorGUILayout.BeginHorizontal();
            var codeStyle = new GUIStyle(EditorStyles.miniLabel);
            codeStyle.normal.textColor = new Color(0.45f, 0.48f, 0.55f);
            GUILayout.Label(issue.code, codeStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("打开", EditorStyles.miniButton, GUILayout.Width(48f)))
                OpenAt(issue);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            // 双击整块也能打开
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition))
            {
                bool doubleClick = _lastClicked == index &&
                                   EditorApplication.timeSinceStartup - _lastClickTime < 0.4;
                _lastClicked = index;
                _lastClickTime = EditorApplication.timeSinceStartup;
                if (doubleClick) { OpenAt(issue); ev.Use(); }
            }
        }

        static void OpenAt(VNLintIssue issue)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(issue.assetPath);
            if (asset == null) return;
            // 用外部编辑器打开并定位行号（.txt 走系统默认编辑器）
            AssetDatabase.OpenAsset(asset, Mathf.Max(1, issue.line));
            EditorGUIUtility.PingObject(asset);
        }

        static Texture Icon(VNLintSeverity severity)
        {
            switch (severity)
            {
                case VNLintSeverity.Error: return EditorGUIUtility.IconContent("console.erroricon.sml").image;
                case VNLintSeverity.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml").image;
                default: return EditorGUIUtility.IconContent("console.infoicon.sml").image;
            }
        }
    }
}
