using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public partial class VNTutorialEditorWindow
    {
        // ==================================================================
        // OnGUI
        // ==================================================================

        void OnGUI()
        {
            HandleObjectPicker();
            DrawToolbar();

            if (_def == null)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.HelpBox(
                    "先在工具栏选一篇教程，或点「新建」。\n" +
                    "（资产放在 Assets/VNEffects/Tutorials/，新建时会自动登记进 VNGameConfig 的教程库）",
                    MessageType.Info);
                return;
            }

            var body = new Rect(0f, ToolbarH, position.width, position.height - ToolbarH - StatusH);
            var left = new Rect(body.x, body.y, LeftWidth, body.height);
            var right = new Rect(body.xMax - RightWidth, body.y, RightWidth, body.height);
            var center = new Rect(left.xMax + 4f, body.y + 4f,
                right.x - left.xMax - 8f, body.height - 8f);

            DrawStepList(left);
            DrawCanvas(center);
            DrawProperties(right);
            DrawStatus(new Rect(0f, position.height - StatusH, position.width, StatusH));
            HandleShortcuts();
        }

        // ------------------------------------------------------------------
        // 工具栏
        // ------------------------------------------------------------------

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int cur = System.Array.IndexOf(_allDefs, _def);
                int pick = EditorGUILayout.Popup(cur, _allDefNames, EditorStyles.toolbarPopup,
                    GUILayout.Width(200f));
                if (pick != cur && pick >= 0 && pick < _allDefs.Length) SetDef(_allDefs[pick]);

                if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                    CreateNewDef();
                using (new EditorGUI.DisabledScope(_def == null))
                    if (GUILayout.Button("定位", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                        EditorGUIUtility.PingObject(_def);

                GUILayout.Space(10f);
                GUILayout.Label("文字语言", EditorStyles.miniLabel, GUILayout.Width(50f));
                for (int i = 0; i < LangNames.Length; i++)
                {
                    bool on = GUILayout.Toggle(_lang == i, LangNames[i], EditorStyles.toolbarButton,
                        GUILayout.Width(28f));
                    if (on && _lang != i)
                    {
                        _lang = i;
                        EditorPrefs.SetInt(LangPref, i);
                    }
                }

                GUILayout.Space(10f);
                if (GUILayout.Button("底图 ▾", EditorStyles.toolbarDropDown, GUILayout.Width(60f)))
                    ShowBackdropMenu();
                bool showCard = GUILayout.Toggle(_showCard, "卡片", EditorStyles.toolbarButton,
                    GUILayout.Width(40f));
                if (showCard != _showCard)
                {
                    _showCard = showCard;
                    EditorPrefs.SetBool(ShowCardPref, showCard);
                }

                GUILayout.FlexibleSpace();

                GUILayout.Label(EditorApplication.isPlaying ? "● Play Mode（可拾取 / 真机预览）" : "Edit Mode",
                    EditorStyles.miniLabel);
                GUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(_undo.Count == 0))
                    if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                        PerformUndo();
                using (new EditorGUI.DisabledScope(_redo.Count == 0))
                    if (GUILayout.Button("重做", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                        PerformRedo();
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                {
                    AssetDatabase.SaveAssets();
                    ShowNotification(new GUIContent("已保存"));
                }
            }
        }

        void ShowBackdropMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(EditorApplication.isPlaying
                ? "抓当前 Game 视图（含 HUD / 面板）" : "抓当前 Game 视图（Edit Mode：只有主 Canvas）"),
                false, CaptureBackdrop);
            menu.AddItem(new GUIContent("从项目里选图片…"), false, () =>
                EditorGUIUtility.ShowObjectPicker<Texture2D>(null, false, "", ImagePickerId));
            menu.AddItem(new GUIContent("从磁盘选 PNG…"), false, () =>
            {
                string path = EditorUtility.OpenFilePanel("选底图", VNTutorialBackdrop.Dir, "png,jpg");
                if (!string.IsNullOrEmpty(path)) SetBackdrop(path);
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("打开底图文件夹"), false, () =>
            {
                Directory.CreateDirectory(VNTutorialBackdrop.Dir);
                EditorUtility.RevealInFinder(VNTutorialBackdrop.Dir);
            });
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_backdropPath)))
                menu.AddItem(new GUIContent("清除底图"), false, () => SetBackdrop(""));
            menu.ShowAsContext();
        }

        void HandleObjectPicker()
        {
            var e = Event.current;
            if (e.type != EventType.ExecuteCommand) return;
            if (EditorGUIUtility.GetObjectPickerControlID() != ImagePickerId) return;
            if (e.commandName == "ObjectSelectorUpdated" || e.commandName == "ObjectSelectorClosed")
            {
                var obj = EditorGUIUtility.GetObjectPickerObject();
                if (obj != null) SetBackdrop(AssetDatabase.GetAssetPath(obj));
                e.Use();
            }
        }

        void CaptureBackdrop()
        {
            string baseName = _def != null ? DisplayId(_def) : "backdrop";
            if (EditorApplication.isPlaying)
            {
                var picker = VNTutorialPicker.Ensure();
                if (picker == null) return;
                // 预览层盖在上面会一起被抓进去，先藏一帧
                var player = VNTutorialPlayer.Instance;
                bool hid = player != null && player.IsEditorPreview;
                if (hid) player.EditorPreviewSetVisible(false);
                picker.Capture(tex =>
                {
                    if (hid && player != null) player.EditorPreviewSetVisible(true);
                    if (tex == null) { ShowNotification(new GUIContent("抓屏失败")); return; }
                    string path = VNTutorialBackdrop.SavePng(tex, baseName);
                    DestroyImmediate(tex);
                    SetBackdrop(path);
                });
            }
            else
            {
                var tex = VNTutorialBackdrop.CaptureEditMode(out string error);
                if (tex == null)
                {
                    ShowNotification(new GUIContent("抓屏失败：" + error));
                    return;
                }
                string path = VNTutorialBackdrop.SavePng(tex, baseName);
                DestroyImmediate(tex);
                SetBackdrop(path);
            }
        }

        void SetBackdrop(string path)
        {
            _backdropPath = path ?? "";
            if (_def != null) EditorPrefs.SetString(BackdropKey(_def), _backdropPath);
            LoadBackdrop();
            Repaint();
        }

        void LoadBackdrop()
        {
            if (_backdrop != null && _backdropLoadedFrom == _backdropPath) return;
            // 磁盘读进来的贴图是 DontSave 的临时对象，换图时释放；项目资产不能 Destroy
            if (_backdrop != null && !string.IsNullOrEmpty(_backdropLoadedFrom) &&
                !_backdropLoadedFrom.StartsWith("Assets/"))
                DestroyImmediate(_backdrop);
            _backdrop = VNTutorialBackdrop.Load(_backdropPath);
            _backdropLoadedFrom = _backdropPath;
        }

        // ------------------------------------------------------------------
        // 左：步骤列表
        // ------------------------------------------------------------------

        void DrawStepList(Rect rect)
        {
            GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f));
            EditorGUILayout.LabelField($"步骤（{_def.steps.Count}）", EditorStyles.boldLabel);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            for (int i = 0; i < _def.steps.Count; i++)
            {
                var s = _def.steps[i];
                var row = GUILayoutUtility.GetRect(10f, 44f, GUILayout.ExpandWidth(true));
                bool selected = i == _selected;
                EditorGUI.DrawRect(row, selected
                    ? new Color(0.24f, 0.42f, 0.7f, 0.55f)
                    : (i % 2 == 0 ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.05f)));
                if (_listDragging && i == _dragStep)
                    EditorGUI.DrawRect(row, new Color(1f, 0.85f, 0.3f, 0.25f));

                var line1 = new Rect(row.x + 6f, row.y + 4f, row.width - 12f, 18f);
                var line2 = new Rect(row.x + 6f, row.y + 23f, row.width - 12f, 16f);
                string summary = s == null ? "(空)" : Summary(s);
                GUI.Label(line1, $"{i + 1}. {summary}", EditorStyles.boldLabel);
                GUI.Label(line2, s == null ? "" : TargetSummary(s), EditorStyles.miniLabel);

                HandleListRow(row, i);
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 新步骤 ▾")) ShowTemplateMenu();
                using (new EditorGUI.DisabledScope(Current == null))
                {
                    if (GUILayout.Button("复制", GUILayout.Width(44f))) DuplicateStep();
                    if (GUILayout.Button("删除", GUILayout.Width(44f))) DeleteStep(_selected);
                }
            }
            EditorGUILayout.LabelField("拖动行排序 · Ctrl+D 复制 · 右键更多", EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndArea();
        }

        void HandleListRow(Rect row, int index)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when row.Contains(e.mousePosition):
                    if (e.button == 1)
                    {
                        _selected = index;
                        ShowRowContextMenu(index);
                        e.Use();
                        return;
                    }
                    if (e.button == 0)
                    {
                        _selected = index;
                        _dragStep = index;
                        _listDragging = false;
                        GUI.FocusControl(null);
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseDrag when _dragStep >= 0 && row.Contains(e.mousePosition) && index != _dragStep:
                    if (!_listDragging)
                    {
                        Snapshot();
                        _listDragging = true;
                    }
                    MoveStep(_dragStep, index);
                    _dragStep = index;
                    e.Use();
                    break;

                case EventType.MouseUp when _dragStep >= 0:
                    _dragStep = -1;
                    _listDragging = false;
                    Repaint();
                    break;
            }
        }

        void ShowRowContextMenu(int index)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("复制这一步"), false, DuplicateStep);
            if (index > 0) menu.AddItem(new GUIContent("上移"), false, () => { Snapshot(); MoveStep(index, index - 1); });
            else menu.AddDisabledItem(new GUIContent("上移"));
            if (index < _def.steps.Count - 1) menu.AddItem(new GUIContent("下移"), false, () => { Snapshot(); MoveStep(index, index + 1); });
            else menu.AddDisabledItem(new GUIContent("下移"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除"), false, () => DeleteStep(index));
            menu.ShowAsContext();
        }

        void ShowTemplateMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("空白步骤"), false, () => InsertStep(Template(TemplateKind.Blank)));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("开场图文页（不挖洞，卡片居中）"), false, () => InsertStep(Template(TemplateKind.Opening)));
            menu.AddItem(new GUIContent("按钮 / 面板讲解（圆角矩形洞）"), false, () => InsertStep(Template(TemplateKind.Button)));
            menu.AddItem(new GUIContent("角色 / 头像（椭圆洞）"), false, () => InsertStep(Template(TemplateKind.Portrait)));
            menu.AddItem(new GUIContent("结尾页（不挖洞）"), false, () => InsertStep(Template(TemplateKind.Ending)));
            menu.ShowAsContext();
        }

        static string Summary(VNTutorialStep s)
        {
            string t = !string.IsNullOrEmpty(s.title) ? s.title : s.body;
            if (string.IsNullOrEmpty(t)) return "(未填文字)";
            t = t.Replace("\n", " ");
            return t.Length > 14 ? t.Substring(0, 14) + "…" : t;
        }

        static string TargetSummary(VNTutorialStep s)
        {
            if (!string.IsNullOrEmpty(s.anchor)) return "锚点 " + s.anchor;
            if (s.HasArea) return (s.shape == VNTutorialHole.Ellipse ? "椭圆 " : "矩形 ") +
                                  $"{s.area.x:0.00},{s.area.y:0.00} {s.area.width:0.00}×{s.area.height:0.00}";
            return "整屏压暗（不挖洞）";
        }
    }
}
