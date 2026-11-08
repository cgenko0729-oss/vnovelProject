using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public partial class VNScenarioEditorWindow
    {
        // ------------------------------------------------------------------
        // Text / Issues 页签
        // ------------------------------------------------------------------

        void DrawTextTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy to clipboard", GUILayout.Width(130f)))
                {
                    EditorGUIUtility.systemCopyBuffer = _doc.GenerateText();
                    ShowNotification(new GUIContent("Copied"));
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("read-only preview of what Save will write",
                    EditorStyles.miniLabel);
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextArea(_doc.GenerateText(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void DrawIssuesTab()
        {
            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No issues. ✔", MessageType.Info);
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var issue in _issues)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(issue.isError ? "✕" : "⚠",
                        GUILayout.Width(18f));
                    string rowDesc = issue.rowIndex >= 0 && issue.rowIndex < _doc.rows.Count
                        ? RowSummary(_doc.rows[issue.rowIndex]) : "?";
                    GUILayout.Label($"Row {issue.rowIndex + 1} [{rowDesc}]: {issue.message}",
                        EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("Select", GUILayout.Width(52f)))
                        FocusRow(issue.rowIndex);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static string RowSummary(VNRow r)
        {
            switch (r.kind)
            {
                case VNRowKind.Say:
                    return string.IsNullOrEmpty(r.speaker) ? "narration" : r.speaker;
                case VNRowKind.Command: return r.keyword;
                default:
                    return r.raw.Length > 14 ? r.raw.Substring(0, 14) + "…" : r.raw;
            }
        }
    }
}
