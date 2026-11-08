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
        // GUI
        // ------------------------------------------------------------------

        void OnGUI()
        {
            if (Event.current.type == EventType.Layout)
            {
                ApplyPendingInsert();
                ApplyPendingNewRow();
            }

            // 帧首快照（撤销用：结构操作要拿"改动前"的文本）
            if (Event.current.type == EventType.Layout && _frameSnapshotVersion != _version)
            {
                _frameSnapshot = _doc.GenerateText();
                _frameSnapshotVersion = _version;
                _docText = _frameSnapshot;  // 域重载存活的兜底同步
                // 行数变了，跟随高亮的 UI 行号要跟着重算
                if (_playingLine > 0) _playingRow = RowForSourceLine(_playingLine);
            }

            HandleUndoKeys();
            HandleShortcutKeys();  // 必须在 HandleInsertKeys 之前：都盯着 Enter
            HandleInsertKeys();
            ValidateIfNeeded();
            DrawToolbar();
            if (_showCategoryColors)
            {
                bool previousChanged = GUI.changed;
                DrawCategoryColorSettings();
                GUI.changed = previousChanged;
            }

            if (_externalChanged)
            {
                EditorGUILayout.HelpBox(
                    "File changed on disk while you have unsaved edits.", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reload from disk (discard my edits)"))
                        LoadFile(_path);
                    if (GUILayout.Button("Keep mine (will overwrite on save)"))
                    {
                        _externalChanged = false;
                        _fileTime = File.GetLastWriteTimeUtc(_path);
                    }
                }
            }

            switch (_tab)
            {
                case Tab.Edit: DrawEditTab(); break;
                case Tab.Text: DrawTextTab(); break;
                case Tab.Issues: DrawIssuesTab(); break;
            }

            if (GUI.changed)
            {
                Bump();
                double now = EditorApplication.timeSinceStartup;
                if (now - _lastUndoPush > 1.0)
                {
                    PushUndo(_frameSnapshot);
                    _lastUndoPush = now;
                }
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Open", EditorStyles.toolbarButton, GUILayout.Width(46f)))
                    OpenFile();
                using (new EditorGUI.DisabledScope(!_dirty && !string.IsNullOrEmpty(_path)))
                {
                    if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                        SaveFile(false);
                }
                if (GUILayout.Button("Save As", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                    SaveFile(true);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_path)))
                {
                    if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                    {
                        if (!_dirty || EditorUtility.DisplayDialog("Reload",
                                "Discard unsaved changes?", "Discard", "Cancel"))
                            LoadFile(_path);
                    }
                }
                if (GUILayout.Button("Refresh Sources", EditorStyles.toolbarButton, GUILayout.Width(104f)))
                    RefreshSources();
                bool previousChanged = GUI.changed;
                _showCategoryColors = GUILayout.Toggle(_showCategoryColors, "分类颜色",
                    EditorStyles.toolbarButton, GUILayout.Width(64f));
                bool stagePreview = GUILayout.Toggle(_stagePreview,
                    new GUIContent("舞台一览",
                        "每行左侧显示当前背景缩略图与在场角色的站位色块（按文件顺序推算）"),
                    EditorStyles.toolbarButton, GUILayout.Width(64f));
                if (stagePreview != _stagePreview)
                {
                    _stagePreview = stagePreview;
                    EditorPrefs.SetBool(StagePreviewPref, stagePreview);
                }
                bool hideRaw = GUILayout.Toggle(_hideRawRows,
                    new GUIContent("隐注释/空行",
                        "把空行与 # 注释折成零高度，只剩台词和命令。\n" +
                        "行号与所有编辑操作不受影响；想改注释就关掉它，或去 Text 页签改。"),
                    EditorStyles.toolbarButton, GUILayout.Width(78f));
                if (hideRaw != _hideRawRows)
                {
                    _hideRawRows = hideRaw;
                    EditorPrefs.SetBool(HideRawPref, hideRaw);
                    if (hideRaw) MoveSelectionOffHiddenRow();
                }
                GUI.changed = previousChanged;

                GUILayout.Space(8f);
                string name = string.IsNullOrEmpty(_path) ? "(untitled)" : Path.GetFileName(_path);
                GUILayout.Label(name + (_dirty ? " *" : ""), EditorStyles.miniBoldLabel);

                GUILayout.FlexibleSpace();

                // label 快速跳转（本文件 + Assets/Scenarios 下的跨文件地址）
                if ((_labels.Count > 0 || _ctx.qualifiedLabelIds.Length > 0) &&
                    GUILayout.Button("Go to label ▾", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    var menu = new GenericMenu();
                    foreach (var l in _labels)
                    {
                        string label = l;
                        menu.AddItem(new GUIContent(label), false, () => SelectLabelRow(label));
                    }
                    if (_labels.Count > 0 && _ctx.qualifiedLabelIds.Length > 0)
                        menu.AddSeparator("");
                    foreach (string address in _ctx.qualifiedLabelIds)
                    {
                        string capturedAddress = address;
                        menu.AddItem(new GUIContent("跨文件/" + address), false,
                            () => SelectStoryAddress(capturedAddress));
                    }
                    menu.ShowAsContext();
                }

                int errors = 0, warns = 0;
                foreach (var i in _issues) { if (i.isError) errors++; else warns++; }
                DrawTabButton(Tab.Edit, "Edit");
                DrawTabButton(Tab.Text, "Text");
                DrawTabButton(Tab.Issues, $"Issues ({errors}E/{warns}W)");
            }
        }

        /// <summary>
        /// 刚开启隐藏时选中行正好被藏了：往下挪到第一个可见行。
        /// 否则 Duplicate / [-] / 播放会作用在一个看不见的行上。
        /// </summary>
        void MoveSelectionOffHiddenRow()
        {
            int index = _list.index;
            if (index < 0 || index >= _doc.rows.Count) return;
            if (!IsRowCollapsed(_doc.rows[index])) return;

            for (int i = index; i < _doc.rows.Count; i++)
            {
                if (IsRowCollapsed(_doc.rows[i])) continue;
                _list.ClearSelection();
                _list.index = i;
                _list.Select(i, true);
                return;
            }
            // 后面全是隐藏行，往前找
            for (int i = index - 1; i >= 0; i--)
            {
                if (IsRowCollapsed(_doc.rows[i])) continue;
                _list.ClearSelection();
                _list.index = i;
                _list.Select(i, true);
                return;
            }
        }

        void DrawCategoryColorSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("主下拉分类颜色", EditorStyles.miniBoldLabel,
                        GUILayout.Width(92f));
                    foreach (string category in ColorCategoryIds)
                        DrawCategoryColorField(category);
                    if (GUILayout.Button("恢复默认", GUILayout.Width(64f)))
                    {
                        foreach (string category in ColorCategoryIds)
                        {
                            _categoryColors[category] = DefaultCategoryColor(category);
                            SaveCategoryColor(category);
                        }
                        Repaint();
                    }
                }
            }
        }

        void DrawCategoryColorField(string category)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(86f)))
            {
                GUILayout.Label(ColorCategoryLabels[category], EditorStyles.centeredGreyMiniLabel);
                EditorGUI.BeginChangeCheck();
                Color value = EditorGUILayout.ColorField(GUIContent.none,
                    CategoryColor(category), true, false, false, GUILayout.Width(82f));
                if (EditorGUI.EndChangeCheck())
                {
                    _categoryColors[category] = value;
                    SaveCategoryColor(category);
                    Repaint();
                }
            }
        }

        void DrawTabButton(Tab tab, string label)
        {
            bool on = GUILayout.Toggle(_tab == tab, label, EditorStyles.toolbarButton,
                GUILayout.Width(label.Length * 7f + 18f));
            if (on) _tab = tab;
        }

        void SelectLabelRow(string label)
        {
            for (int i = 0; i < _doc.rows.Count; i++)
            {
                var r = _doc.rows[i];
                if (r.kind == VNRowKind.Command && r.keyword == "label" && r.Get("name") == label)
                {
                    FocusRow(i);
                    return;
                }
            }
        }

        void SelectStoryAddress(string address)
        {
            if (!VNStoryAddress.TryParse(address, out string file, out string label, out _))
                return;
            if (file == null)
            {
                SelectLabelRow(label);
                return;
            }
            if (_dirty && !EditorUtility.DisplayDialog("Unsaved changes",
                    "Discard unsaved changes and open the target scenario?", "Discard", "Cancel"))
                return;
            if (!_ctx.scenarioPaths.TryGetValue(VNStoryAddress.NormalizeFile(file), out string path))
                return;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            LoadFile(Path.GetFullPath(Path.Combine(projectRoot, path)));
            SelectLabelRow(label);
        }

        /// <summary>把某一行切到编辑页、选中并滚进视野（Issues 面板 / 镜头编排窗口共用）</summary>
        public void FocusRow(int index)
        {
            _tab = Tab.Edit;
            _list.index = index;
            float y = 0f;
            for (int i = 0; i < index && i < _doc.rows.Count; i++)
                y += RowHeight(_doc.rows[i]);
            _pendingScrollY = Mathf.Max(0f, y - 120f);
            Repaint();
        }
    }
}
