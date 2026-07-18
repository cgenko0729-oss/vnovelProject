using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 剧本可视化编辑器（第一批）：
    ///   - 打开/保存 .vn.txt（文本仍是唯一真相，保存 = 逐行重新生成，格式规范化）
    ///   - 命令列表：拖动排序、增删、复制；台词/命令行全下拉参数编辑（消灭 typo）
    ///   - choice 块内嵌编辑；camseq 参数下拉 + 路径点行原样保留（下一批接镜头编辑器）
    ///   - 校验面板：id/label/语法检查，点击定位到行
    ///   - 文本预览页签、外部修改检测、Ctrl+Z/Y 撤销（约 1 秒粒度合并）
    /// 菜单：Tools → VN Effects → Scenario Editor
    /// </summary>
    public class VNScenarioEditorWindow : EditorWindow
    {
        enum Tab { Edit, Text, Issues }

        const float LineH2 = 21f;   // 一个子行的高度（含间距）

        VNScenarioDoc _doc = new VNScenarioDoc();
        string _path = "";
        System.DateTime _fileTime;
        bool _dirty;
        bool _externalChanged;
        Tab _tab;
        bool _showCategoryColors;
        bool _rebuildStateBeforePlay = true;

        ReorderableList _list;
        Vector2 _scroll;
        float _pendingScrollY = -1f;

        readonly VNScenarioSourceContext _ctx = new VNScenarioSourceContext();
        readonly List<SpritePreviewItem> _backgroundPreviews =
            new List<SpritePreviewItem>();
        readonly List<SpritePreviewItem> _cgPreviews =
            new List<SpritePreviewItem>();
        readonly List<CharacterPreviewItem> _characterPreviews =
            new List<CharacterPreviewItem>();
        List<VNIssue> _issues = new List<VNIssue>();
        readonly Dictionary<int, bool> _rowHasError = new Dictionary<int, bool>();
        List<string> _labels = new List<string>();
        List<string> _flags = new List<string>();
        string[] _flagOps = System.Array.Empty<string>();

        int _version = 1;
        int _validatedVersion = -1;

        // 自定义值编辑状态（选了 "custom…" 的参数格）
        readonly HashSet<(VNRow, string)> _customEdit = new HashSet<(VNRow, string)>();

        // 撤销（文本快照，约 1 秒粒度合并）
        readonly List<string> _undoStack = new List<string>();
        readonly List<string> _redoStack = new List<string>();
        string _frameSnapshot = "";
        int _frameSnapshotVersion = -1;
        double _lastUndoPush;

        static readonly Dictionary<string, string> CommandTranslations =
            new Dictionary<string, string>
            {
                { "bg", "背景" }, { "cg", "CG 一枚绘" }, { "weather", "天气" }, { "mood", "氛围" },
                { "transition", "转场" }, { "show", "显示角色" }, { "hide", "隐藏角色" },
                { "emote", "角色动作" }, { "move", "移动角色" }, { "portrait", "对话头像" },
                { "camera", "镜头运动" }, { "camcut", "镜头切换" }, { "camto", "镜头移动" },
                { "camseq", "镜头序列" }, { "shake", "震动" }, { "fx", "特效" },
                { "sakura", "樱花" }, { "bgm", "背景音乐" }, { "se", "音效" },
                { "voice", "语音" }, { "volume", "音量" }, { "wait", "等待" },
                { "label", "标签" }, { "jump", "跳转" }, { "flag", "变量" },
                { "if", "条件" }, { "choice", "选项" }, { "event", "事件" },
                { "chapter", "章节" }, { "quest", "任务" }, { "letterbox", "电影黑边" },
            };

        static readonly Dictionary<string, string> CategoryTranslations =
            new Dictionary<string, string>
            {
                { "Scene", "场景" }, { "Character", "角色" }, { "Camera", "镜头" },
                { "FX", "特效" }, { "Audio", "音频" }, { "Flow", "流程" },
            };

        static readonly Dictionary<string, string> TransitionTranslations =
            new Dictionary<string, string>
            {
                { "NoiseDissolve", "噪声溶解" }, { "Blinds", "百叶窗" },
                { "Tiles", "方块翻转" }, { "CircleWipe", "圆形擦除" },
                { "InkSpread", "水墨扩散" }, { "WhiteFlash", "白色闪光" },
                { "BokehOrbs", "光斑圆球" }, { "Eyelid", "眼睑闭合" },
                { "PageCurl", "卷页" }, { "Shatter", "画面碎裂" },
                { "Ripple", "水波扩散" }, { "InkBleed", "墨水晕染" },
            };

        static readonly Dictionary<string, string> EmoteTranslations =
            new Dictionary<string, string>
            {
                { "Surprise", "惊讶" }, { "Angry", "生气" }, { "Shy", "害羞" },
                { "Dejected", "沮丧" }, { "Recover", "恢复" }, { "Nod", "点头" },
                { "HeadShake", "摇头" },
            };

        const string CategoryColorPrefPrefix = "VNEffects.ScenarioEditor.CategoryColor.";
        static readonly string[] ColorCategoryIds =
            { "Dialogue", "Scene", "Character", "Camera", "FX", "Audio", "Flow" };
        static readonly Dictionary<string, string> ColorCategoryLabels =
            new Dictionary<string, string>
            {
                { "Dialogue", "对话" }, { "Scene", "场景" }, { "Character", "角色" },
                { "Camera", "镜头" }, { "FX", "特效" }, { "Audio", "音频" },
                { "Flow", "流程" },
            };
        readonly Dictionary<string, Color> _categoryColors = new Dictionary<string, Color>();

        [MenuItem("Tools/VN Effects/Scenario Editor")]
        static void Open()
        {
            var win = GetWindow<VNScenarioEditorWindow>("Scenario Editor");
            win.minSize = new Vector2(960f, 560f);
        }

        void OnEnable()
        {
            LoadCategoryColors();
            BuildList();
            RefreshSources();
        }

        void OnFocus()
        {
            RefreshSources();
            CheckExternalChange();
        }

        void BuildList()
        {
            _list = new ReorderableList(_doc.rows, typeof(VNRow), true, false, true, true)
            {
                elementHeightCallback = i => RowHeight(_doc.rows[i]),
                drawElementCallback = DrawRow,
                onAddDropdownCallback = (rect, list) => ShowAddMenu(),
                onRemoveCallback = list =>
                {
                    if (list.index < 0 || list.index >= _doc.rows.Count) return;
                    MarkStructural();
                    _doc.rows.RemoveAt(list.index);
                    list.index = Mathf.Clamp(list.index, 0, _doc.rows.Count - 1);
                    Bump();
                },
                onReorderCallback = list => { PushUndo(_frameSnapshot); Bump(); },
            };
        }

        void RebindList()
        {
            BuildList();
            _customEdit.Clear();
        }

        // ------------------------------------------------------------------
        // 数据源
        // ------------------------------------------------------------------

        void RefreshSources()
        {
            var ids = new List<string>();
            _ctx.expressions.Clear();
            _characterPreviews.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:VNCharacterDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNCharacterDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                ids.Add(def.id);
                var exprs = new List<string>();
                var previews = new List<SpritePreviewItem>();
                foreach (var e in def.expressions)
                {
                    if (string.IsNullOrEmpty(e.name)) continue;
                    exprs.Add(e.name);
                    previews.Add(new SpritePreviewItem(e.name, e.sprite));
                }
                _ctx.expressions[def.id] = exprs.ToArray();
                _characterPreviews.Add(new CharacterPreviewItem(def.id, previews));
            }
            _ctx.characterIds = ids.ToArray();

            var stage = FindFirstObjectByType<VNStage>();
            _backgroundPreviews.Clear();
            _cgPreviews.Clear();
            if (stage != null)
            {
                var bgs = new List<string>();
                foreach (var b in stage.backgrounds)
                {
                    if (b == null || string.IsNullOrEmpty(b.id)) continue;
                    bgs.Add(b.id);
                    _backgroundPreviews.Add(new SpritePreviewItem(b.id, b.sprite));
                }
                _ctx.backgroundIds = bgs.ToArray();

                var cgs = new List<string>();
                foreach (var c in stage.cgLibrary)
                {
                    if (c == null || string.IsNullOrEmpty(c.id)) continue;
                    cgs.Add(c.id);
                    _cgPreviews.Add(new SpritePreviewItem(c.id, c.sprite));
                }
                _ctx.cgIds = cgs.ToArray();
            }
            else
            {
                _ctx.backgroundIds = System.Array.Empty<string>();
                _ctx.cgIds = System.Array.Empty<string>();
            }

            var audio = FindFirstObjectByType<VNAudio>();
            if (audio != null)
            {
                // 旧混合库的条目三个通道都能用，因此并入每个候选列表
                _ctx.bgmIds = CollectAudioIds(audio.bgmLibrary, audio.library);
                _ctx.seIds = CollectAudioIds(audio.seLibrary, audio.library);
                _ctx.voiceIds = CollectAudioIds(audio.voiceLibrary, audio.library);
            }
            else
            {
                _ctx.bgmIds = System.Array.Empty<string>();
                _ctx.seIds = System.Array.Empty<string>();
                _ctx.voiceIds = System.Array.Empty<string>();
            }

            var eventRegistry = FindFirstObjectByType<VNEventRegistry>();
            _ctx.eventIds = eventRegistry != null
                ? new List<string>(eventRegistry.Ids).ToArray()
                : System.Array.Empty<string>();

            var questIds = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:VNQuestDef"))
            {
                var quest = AssetDatabase.LoadAssetAtPath<VNQuestDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (quest != null && !string.IsNullOrEmpty(quest.id))
                    questIds.Add(quest.id);
            }
            _ctx.questIds = questIds.ToArray();

            _validatedVersion = -1; // 数据源变了要重新校验
        }

        /// <summary>通道专属库 + 旧混合库合并去重后的 id 列表（保持登记顺序）</summary>
        static string[] CollectAudioIds(List<VNAudio.AudioEntry> channelLib,
            List<VNAudio.AudioEntry> legacyLib)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>();
            foreach (var lib in new[] { channelLib, legacyLib })
            {
                if (lib == null) continue;
                foreach (var e in lib)
                    if (e != null && !string.IsNullOrEmpty(e.id) && seen.Add(e.id))
                        ids.Add(e.id);
            }
            return ids.ToArray();
        }

        void ValidateIfNeeded()
        {
            if (_validatedVersion == _version) return;
            _validatedVersion = _version;
            _issues = _doc.Validate(_ctx);
            _rowHasError.Clear();
            foreach (var issue in _issues)
            {
                if (issue.isError) _rowHasError[issue.rowIndex] = true;
                else if (!_rowHasError.ContainsKey(issue.rowIndex))
                    _rowHasError[issue.rowIndex] = false;
            }
            _labels = _doc.CollectLabels();
            _flags = _doc.CollectFlags();
            var ops = new List<string>();
            foreach (var f in _flags) { ops.Add(f + "+1"); ops.Add(f + "-1"); ops.Add(f); }
            _flagOps = ops.ToArray();
        }

        // ------------------------------------------------------------------
        // 文件
        // ------------------------------------------------------------------

        void OpenFile()
        {
            if (_dirty && !EditorUtility.DisplayDialog("Unsaved changes",
                    "Discard unsaved changes?", "Discard", "Cancel")) return;
            string dir = Path.Combine(Application.dataPath, "Scenarios");
            if (!Directory.Exists(dir)) dir = Application.dataPath;
            string p = EditorUtility.OpenFilePanel("Open scenario (.vn.txt)", dir, "txt");
            if (string.IsNullOrEmpty(p)) return;
            LoadFile(p);
        }

        void LoadFile(string absolutePath)
        {
            _path = absolutePath;
            _doc = VNScenarioDoc.Parse(File.ReadAllText(absolutePath));
            _fileTime = File.GetLastWriteTimeUtc(absolutePath);
            _dirty = false;
            _externalChanged = false;
            _undoStack.Clear();
            _redoStack.Clear();
            RebindList();
            Bump();
        }

        void SaveFile(bool saveAs)
        {
            string p = _path;
            if (saveAs || string.IsNullOrEmpty(p))
            {
                string dir = Path.Combine(Application.dataPath, "Scenarios");
                if (!Directory.Exists(dir)) dir = Application.dataPath;
                p = EditorUtility.SaveFilePanel("Save scenario", dir, "NewScenario.vn", "txt");
                if (string.IsNullOrEmpty(p)) return;
            }
            File.WriteAllText(p, _doc.GenerateText(), new UTF8Encoding(false));
            _path = p;
            _fileTime = File.GetLastWriteTimeUtc(p);
            _dirty = false;
            _externalChanged = false;

            // 项目内文件刷新导入
            string assets = Application.dataPath.Replace('\\', '/');
            string norm = p.Replace('\\', '/');
            if (norm.StartsWith(assets))
                AssetDatabase.ImportAsset("Assets" + norm.Substring(assets.Length));
            ShowNotification(new GUIContent("Saved"));
        }

        void CheckExternalChange()
        {
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return;
            if (File.GetLastWriteTimeUtc(_path) == _fileTime) return;
            if (!_dirty)
            {
                LoadFile(_path); // 没有本地改动 → 静默重载
                ShowNotification(new GUIContent("Reloaded (changed on disk)"));
            }
            else _externalChanged = true;
        }

        // ------------------------------------------------------------------
        // 撤销
        // ------------------------------------------------------------------

        void Bump() { _version++; _dirty = true; }

        void PushUndo(string snapshot)
        {
            if (string.IsNullOrEmpty(snapshot)) return;
            if (_undoStack.Count > 0 && _undoStack[_undoStack.Count - 1] == snapshot) return;
            _undoStack.Add(snapshot);
            if (_undoStack.Count > 100) _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        void MarkStructural() => PushUndo(_frameSnapshot);

        void LoadFromText(string text)
        {
            _doc = VNScenarioDoc.Parse(text);
            RebindList();
            Bump();
        }

        void HandleUndoKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || !(e.control || e.command)) return;
            if (EditorGUIUtility.editingTextField) return; // 文本框内用系统自带撤销

            if (e.keyCode == KeyCode.Z && _undoStack.Count > 0)
            {
                _redoStack.Add(_doc.GenerateText());
                string s = _undoStack[_undoStack.Count - 1];
                _undoStack.RemoveAt(_undoStack.Count - 1);
                LoadFromText(s);
                e.Use();
            }
            else if (e.keyCode == KeyCode.Y && _redoStack.Count > 0)
            {
                _undoStack.Add(_doc.GenerateText());
                string s = _redoStack[_redoStack.Count - 1];
                _redoStack.RemoveAt(_redoStack.Count - 1);
                LoadFromText(s);
                e.Use();
            }
        }

        // ------------------------------------------------------------------
        // GUI
        // ------------------------------------------------------------------

        void OnGUI()
        {
            // 帧首快照（撤销用：结构操作要拿"改动前"的文本）
            if (Event.current.type == EventType.Layout && _frameSnapshotVersion != _version)
            {
                _frameSnapshot = _doc.GenerateText();
                _frameSnapshotVersion = _version;
            }

            HandleUndoKeys();
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
                GUI.changed = previousChanged;

                GUILayout.Space(8f);
                string name = string.IsNullOrEmpty(_path) ? "(untitled)" : Path.GetFileName(_path);
                GUILayout.Label(name + (_dirty ? " *" : ""), EditorStyles.miniBoldLabel);

                GUILayout.FlexibleSpace();

                // label 快速跳转
                if (_labels.Count > 0 &&
                    GUILayout.Button("Go to label ▾", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    var menu = new GenericMenu();
                    foreach (var l in _labels)
                    {
                        string label = l;
                        menu.AddItem(new GUIContent(label), false, () => SelectLabelRow(label));
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

        void FocusRow(int index)
        {
            _tab = Tab.Edit;
            _list.index = index;
            float y = 0f;
            for (int i = 0; i < index && i < _doc.rows.Count; i++)
                y += RowHeight(_doc.rows[i]);
            _pendingScrollY = Mathf.Max(0f, y - 120f);
            Repaint();
        }

        // ------------------------------------------------------------------
        // Edit 页签
        // ------------------------------------------------------------------

        void DrawEditTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                    _list.index < 0 || _list.index >= _doc.rows.Count))
                {
                    if (GUILayout.Button("Duplicate", GUILayout.Width(72f)))
                    {
                        MarkStructural();
                        _doc.rows.Insert(_list.index + 1, _doc.rows[_list.index].Clone());
                        _list.index++;
                        Bump();
                    }
                }
                using (new EditorGUI.DisabledScope(
                    _list.index < 0 || _list.index >= _doc.rows.Count ||
                    EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button(new GUIContent("▶ 从选中行播放",
                            "进入 Play Mode，并从当前行或下一条有效命令开始播放"),
                            GUILayout.Width(126f)))
                        PlayFromSelectedRow();
                }
                bool previousChanged = GUI.changed;
                _rebuildStateBeforePlay = GUILayout.Toggle(_rebuildStateBeforePlay,
                    new GUIContent("重建前置状态", "播放前静默恢复目标行之前的舞台状态"),
                    GUILayout.Width(96f));
                GUI.changed = previousChanged;
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_doc.rows.Count} rows", EditorStyles.miniLabel);
            }

            if (_pendingScrollY >= 0f)
            {
                _scroll.y = _pendingScrollY;
                _pendingScrollY = -1f;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _list.DoLayoutList();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.HelpBox(
                "Drag handle to reorder. [+] adds after selection. \"@\" = async (do not wait). " +
                "Popups list registered ids; pick \"custom…\" to type a free value. " +
                "camseq waypoint lines are kept as text in this batch " +
                "(use Tools → VN Effects → Camera Sequence Editor and paste).",
                MessageType.Info);
        }

        void PlayFromSelectedRow()
        {
            foreach (var issue in _issues)
            {
                if (!issue.isError) continue;
                _tab = Tab.Issues;
                EditorUtility.DisplayDialog("无法开始调试",
                    "剧本仍有错误，请先在 Issues 页修正。", "确定");
                return;
            }

            int sourceLine = SourceLineForRow(_list.index);
            string source = _doc.GenerateText();
            var commands = VNScriptParser.Parse(source);
            bool found = false;
            foreach (var command in commands)
            {
                if (command.line < sourceLine) continue;
                found = true;
                break;
            }
            if (!found)
            {
                EditorUtility.DisplayDialog("无法开始调试",
                    $"第 {sourceLine} 行之后没有可播放的命令。", "确定");
                return;
            }

            VNPlayFromLineBridge.Request(source, sourceLine, _rebuildStateBeforePlay);
        }

        int SourceLineForRow(int rowIndex)
        {
            int line = 1;
            for (int i = 0; i < rowIndex && i < _doc.rows.Count; i++)
            {
                VNRow row = _doc.rows[i];
                line++;
                if (row.options != null) line += row.options.Count;
                if (row.camLines != null) line += row.camLines.Count;
            }
            return line;
        }

        float RowHeight(VNRow r)
        {
            int lines = 1;
            if (r.options != null) lines += r.options.Count;
            if (r.camLines != null) lines += r.camLines.Count;
            return lines * LineH2 + 6f;
        }

        Rect SubLine(Rect rect, int line) => new Rect(
            rect.x + 14f, rect.y + 3f + line * LineH2, rect.width - 16f, LineH2 - 3f);

        void DrawRow(Rect rect, int index, bool active, bool focused)
        {
            if (index < 0 || index >= _doc.rows.Count) return;
            var r = _doc.rows[index];

            // 校验状态圆点
            if (_rowHasError.TryGetValue(index, out bool isErr))
                EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y + 8f, 7f, 7f),
                    isErr ? new Color(0.95f, 0.3f, 0.25f) : new Color(0.95f, 0.75f, 0.2f));

            var line0 = SubLine(rect, 0);
            switch (r.kind)
            {
                case VNRowKind.Raw: DrawRawRow(line0, r); break;
                case VNRowKind.Say: DrawSayRow(line0, r); break;
                case VNRowKind.Command: DrawCommandRow(rect, line0, r); break;
            }
        }

        void DrawRawRow(Rect rect, VNRow r)
        {
            var style = new GUIStyle(EditorStyles.textField)
                { fontStyle = FontStyle.Italic };
            style.normal.textColor = new Color(0.55f, 0.6f, 0.55f);
            string nv = EditorGUI.TextField(rect, r.raw, style);
            if (nv != r.raw) r.raw = nv;
            if (string.IsNullOrEmpty(r.raw))
                GUI.Label(rect, " (blank line)", EditorStyles.centeredGreyMiniLabel);
        }

        void DrawSayRow(Rect rect, VNRow r)
        {
            float x = rect.x;
            var typeRect = new Rect(x, rect.y, 128f, rect.height);
            if (CategoryPopupButton(typeRect, "say（对白）", "Dialogue"))
                ShowRowTypeMenu(typeRect, r);
            x += 132f;

            // 说话者
            r.speaker = CharacterPopup(new Rect(x, rect.y, 110f, rect.height),
                r.speaker, r.expression, (r, "say.speaker"),
                _ => r.expression = "");
            x += 114f;

            // 表情
            r.expression = ExpressionPopup(new Rect(x, rect.y, 92f, rect.height),
                r.expression, r.speaker, (r, "say.expr"));
            x += 96f;

            float asyncW = 26f;
            r.text = EditorGUI.TextField(
                new Rect(x, rect.y, rect.xMax - x - asyncW - 2f, rect.height), r.text);
            r.isAsync = GUI.Toggle(new Rect(rect.xMax - asyncW, rect.y, asyncW, rect.height),
                r.isAsync, "@", EditorStyles.miniButton);
        }

        void DrawCommandRow(Rect fullRect, Rect line0, VNRow r)
        {
            float x = line0.x;

            // 关键字下拉
            var keywordRect = new Rect(x, line0.y, 128f, line0.height);
            if (CategoryPopupButton(keywordRect, CommandDisplayName(r.keyword),
                    CommandCategory(r.keyword)))
                ShowRowTypeMenu(keywordRect, r);
            x += 132f;

            var def = VNScenarioSchema.Find(r.keyword);
            float asyncW = 26f;
            float avail = line0.xMax - x - asyncW - 4f;

            if (def != null && def.parameters.Length > 0)
                DrawParams(new Rect(x, line0.y, avail, line0.height), r, def);
            else if (def != null)
                GUI.Label(new Rect(x, line0.y, avail, line0.height),
                    def.hint, EditorStyles.centeredGreyMiniLabel);

            r.isAsync = GUI.Toggle(new Rect(line0.xMax - asyncW, line0.y, asyncW, line0.height),
                r.isAsync, "@", EditorStyles.miniButton);

            // ---- choice 选项行 ----
            if (r.options != null)
            {
                for (int i = 0; i < r.options.Count; i++)
                    DrawChoiceOption(SubLine(fullRect, 1 + i), r, i);
                // header 右侧的 + option
                var addRect = new Rect(line0.xMax - asyncW - 78f, line0.y, 74f, line0.height);
                if (GUI.Button(addRect, "+ option", EditorStyles.miniButton))
                {
                    MarkStructural();
                    r.options.Add(new VNChoiceOptionRow());
                    Bump();
                }
            }

            // ---- camseq 路径点行（原样文本） ----
            if (r.camLines != null)
            {
                for (int i = 0; i < r.camLines.Count; i++)
                {
                    var lr = SubLine(fullRect, 1 + i);
                    var delRect = new Rect(lr.xMax - 20f, lr.y, 20f, lr.height);
                    string nv = EditorGUI.TextField(
                        new Rect(lr.x + 12f, lr.y, lr.width - 34f, lr.height), r.camLines[i]);
                    if (nv != r.camLines[i]) r.camLines[i] = nv;
                    GUI.Label(new Rect(lr.x, lr.y, 12f, lr.height), "›", EditorStyles.miniLabel);
                    if (GUI.Button(delRect, "x", EditorStyles.miniButton))
                    {
                        MarkStructural();
                        r.camLines.RemoveAt(i);
                        Bump();
                        break;
                    }
                }
                var addRect = new Rect(line0.xMax - asyncW - 54f, line0.y, 50f, line0.height);
                if (GUI.Button(addRect, "+ wp", EditorStyles.miniButton))
                {
                    MarkStructural();
                    r.camLines.Add("> middle 1 1");
                    Bump();
                }
            }
        }

        void ShowRowTypeMenu(Rect rect, VNRow row)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Dialogue（对话）/say（对白）"),
                row.kind == VNRowKind.Say, () =>
                {
                    if (row.kind == VNRowKind.Say) return;
                    MarkStructural();
                    SetSayRow(row);
                });
            menu.AddSeparator("");
            foreach (var command in VNScenarioSchema.Commands)
            {
                string keyword = command.keyword;
                string path = $"{CategoryDisplayName(command.category)}/{CommandDisplayName(keyword)}";
                menu.AddItem(new GUIContent(path),
                    row.kind == VNRowKind.Command && keyword == row.keyword, () =>
                {
                    if (row.kind == VNRowKind.Command && keyword == row.keyword) return;
                    MarkStructural();
                    SetKeyword(row, keyword);
                });
            }
            menu.DropDown(rect);
        }

        static string CommandDisplayName(string keyword) =>
            CommandTranslations.TryGetValue(keyword, out string translation)
                ? $"{keyword}（{translation}）" : keyword;

        static string CategoryDisplayName(string category) =>
            CategoryTranslations.TryGetValue(category, out string translation)
                ? $"{category}（{translation}）" : category;

        static string CommandCategory(string keyword)
        {
            var definition = VNScenarioSchema.Find(keyword);
            return definition != null ? definition.category : "";
        }

        bool CategoryPopupButton(Rect rect, string label, string category)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = CategoryColor(category);
            bool clicked = GUI.Button(rect, label, EditorStyles.popup);
            GUI.backgroundColor = previous;
            return clicked;
        }

        Color CategoryColor(string category) =>
            _categoryColors.TryGetValue(category, out Color color) ? color : Color.white;

        void LoadCategoryColors()
        {
            _categoryColors.Clear();
            foreach (string category in ColorCategoryIds)
            {
                string html = EditorPrefs.GetString(CategoryColorPrefPrefix + category, "");
                if (!string.IsNullOrEmpty(html) &&
                    ColorUtility.TryParseHtmlString("#" + html, out Color saved))
                    _categoryColors[category] = saved;
                else
                    _categoryColors[category] = DefaultCategoryColor(category);
            }
        }

        void SaveCategoryColor(string category)
        {
            EditorPrefs.SetString(CategoryColorPrefPrefix + category,
                ColorUtility.ToHtmlStringRGBA(CategoryColor(category)));
        }

        static Color DefaultCategoryColor(string category)
        {
            switch (category)
            {
                case "Dialogue": return new Color(0.55f, 0.78f, 1f);
                case "Scene": return new Color(0.58f, 0.88f, 0.66f);
                case "Character": return new Color(1f, 0.68f, 0.78f);
                case "Camera": return new Color(0.65f, 0.72f, 1f);
                case "FX": return new Color(0.86f, 0.65f, 1f);
                case "Audio": return new Color(1f, 0.78f, 0.42f);
                case "Flow": return new Color(0.72f, 0.78f, 0.82f);
                default: return Color.white;
            }
        }

        void SetSayRow(VNRow row)
        {
            row.kind = VNRowKind.Say;
            row.keyword = "";
            row.values.Clear();
            row.extraTokens.Clear();
            row.options = null;
            row.camLines = null;
            row.speaker = "";
            row.expression = "";
            row.text = "";
            Bump();
        }

        void SetKeyword(VNRow r, string keyword)
        {
            r.kind = VNRowKind.Command;
            r.keyword = keyword;
            r.values.Clear();
            r.extraTokens.Clear();
            var def = VNScenarioSchema.Find(keyword);
            r.options = def != null && def.blockChoice
                ? (r.options ?? new List<VNChoiceOptionRow> { new VNChoiceOptionRow() })
                : null;
            r.camLines = def != null && def.blockCamseq
                ? (r.camLines ?? new List<string>()) : null;
            Bump();
        }

        void DrawChoiceOption(Rect rect, VNRow r, int i)
        {
            var o = r.options[i];
            float x = rect.x + 4f;
            GUI.Label(new Rect(x, rect.y, 12f, rect.height), "*");
            x += 14f;

            float tailW = 118f + 4f + 118f + 4f + 20f; // flag + jump + delete
            o.text = EditorGUI.TextField(
                new Rect(x, rect.y, rect.xMax - x - tailW - 6f, rect.height), o.text);
            x = rect.xMax - tailW;

            o.flagOp = PopupString(new Rect(x, rect.y, 118f, rect.height),
                o.flagOp, _flagOps, "(no flag)", (r, $"opt{i}.flag"));
            x += 122f;
            o.jump = PopupString(new Rect(x, rect.y, 118f, rect.height),
                o.jump, _labels.ToArray(), "(continue)", (r, $"opt{i}.jump"));
            x += 122f;

            if (GUI.Button(new Rect(rect.xMax - 20f, rect.y, 20f, rect.height), "x",
                EditorStyles.miniButton))
            {
                MarkStructural();
                r.options.RemoveAt(i);
                Bump();
            }
        }

        // ---- 参数区 ----

        void DrawParams(Rect rect, VNRow r, VNCommandDef def)
        {
            // 计算总权重与标签宽
            float totalWeight = 0f;
            float labelTotal = 0f;
            foreach (var p in def.parameters)
            {
                totalWeight += p.weight;
                labelTotal += LabelWidth(p);
            }
            float fieldAvail = rect.width - labelTotal - def.parameters.Length * 4f;
            float x = rect.x;

            foreach (var p in def.parameters)
            {
                float lw = LabelWidth(p);
                if (lw > 0f)
                {
                    GUI.Label(new Rect(x, rect.y, lw, rect.height), p.label,
                        EditorStyles.miniLabel);
                    x += lw;
                }
                float w = Mathf.Max(34f, fieldAvail * p.weight / totalWeight);
                DrawParamField(new Rect(x, rect.y, w, rect.height), r, p);
                x += w + 4f;
            }
        }

        static float LabelWidth(VNParamDef p) =>
            string.IsNullOrEmpty(p.label) ? 0f : Mathf.Min(64f, p.label.Length * 7f + 6f);

        void DrawParamField(Rect rect, VNRow r, VNParamDef p)
        {
            string v = r.Get(p.id);
            string[] options = OptionsFor(r, p);

            if (r.keyword == "show" && p.source == VNParamSource.Character)
            {
                string character = CharacterPopup(rect, v, r.Get("expr"), (r, p.id),
                    _ => r.Set("expr", ""));
                if (character != v) r.Set(p.id, character);
                return;
            }

            if (r.keyword == "show" && p.source == VNParamSource.Expression)
            {
                string expression = ExpressionPopup(rect, v, r.Get(p.dependsOn), (r, p.id));
                if (expression != v) r.Set(p.id, expression);
                return;
            }

            if (p.source == VNParamSource.Background)
            {
                string background = BackgroundPopup(rect, v, (r, p.id));
                if (background != v) r.Set(p.id, background);
                return;
            }

            if (p.source == VNParamSource.Cg)
            {
                // "off" 走普通文本（关闭 CG 不需要缩略图浏览器）
                if (v == "off")
                {
                    string typed = EditorGUI.TextField(rect, v);
                    if (typed != v) r.Set(p.id, typed);
                    return;
                }
                string cg = CgPopup(rect, v, (r, p.id));
                if (cg != v) r.Set(p.id, cg);
                return;
            }

            if (options == null)
            {
                // 自由文本 / 数字
                bool bad = p.source == VNParamSource.Number &&
                           !string.IsNullOrEmpty(v) && !float.TryParse(v, out _);
                var prev = GUI.color;
                if (bad) GUI.color = new Color(1f, 0.55f, 0.5f);
                string nv = EditorGUI.TextField(rect, v);
                GUI.color = prev;
                if (nv != v) r.Set(p.id, nv);
                if (string.IsNullOrEmpty(nv) && !string.IsNullOrEmpty(p.defaultValue))
                    GUI.Label(rect, " " + p.defaultValue, EditorStyles.centeredGreyMiniLabel);
                return;
            }

            string[] displayOptions = null;
            if (IsTransitionParameter(r, p))
                displayOptions = BuildTranslatedOptions(options, TransitionTranslations);
            else if (IsEmoteParameter(r, p))
                displayOptions = BuildTranslatedOptions(options, EmoteTranslations);
            string nv2 = PopupString(rect, v, options, "-", (r, p.id), displayOptions);
            if (nv2 != v) r.Set(p.id, nv2);
        }

        static bool IsTransitionParameter(VNRow row, VNParamDef parameter) =>
            (row.keyword == "bg" && parameter.id == "transition") ||
            (row.keyword == "cg" && parameter.id == "transition") ||
            (row.keyword == "transition" && parameter.id == "type");

        static bool IsEmoteParameter(VNRow row, VNParamDef parameter) =>
            row.keyword == "emote" && parameter.id == "emote";

        static string[] BuildTranslatedOptions(string[] options,
            Dictionary<string, string> translations)
        {
            var display = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
                display[i] = translations.TryGetValue(options[i], out string translation)
                    ? $"{options[i]}（{translation}）" : options[i];
            return display;
        }

        string[] OptionsFor(VNRow r, VNParamDef p)
        {
            switch (p.source)
            {
                case VNParamSource.Options: return p.options;
                case VNParamSource.Character: return _ctx.characterIds;
                case VNParamSource.Expression:
                    return _ctx.expressions.TryGetValue(r.Get(p.dependsOn), out var e)
                        ? e : System.Array.Empty<string>();
                case VNParamSource.Background: return _ctx.backgroundIds;
                case VNParamSource.Cg: return _ctx.cgIds;
                case VNParamSource.AudioBgm: return _ctx.bgmIds;
                case VNParamSource.AudioSe:
                    if (r.keyword == "se" && p.id == "a")
                    {
                        var withStop = new List<string> { "stop" };
                        withStop.AddRange(_ctx.seIds);
                        return withStop.ToArray();
                    }
                    return _ctx.seIds;
                case VNParamSource.AudioVoice: return _ctx.voiceIds;
                case VNParamSource.EventId: return _ctx.eventIds;
                case VNParamSource.QuestId: return _ctx.questIds;
                case VNParamSource.Label: return _labels.ToArray();
                case VNParamSource.Flag: return _flags.ToArray();
                default: return null; // Text / Number → 文本框
            }
        }

        /// <summary>下拉 + "custom…" 自由输入的通用控件。emptyLabel 对应空值。</summary>
        string PopupString(Rect rect, string value, string[] options, string emptyLabel,
            (VNRow, string) key, string[] displayOptions = null)
        {
            bool custom = _customEdit.Contains(key) ||
                          (!string.IsNullOrEmpty(value) &&
                           System.Array.IndexOf(options, value) < 0);
            if (custom)
            {
                var tRect = new Rect(rect.x, rect.y, rect.width - 16f, rect.height);
                string nv = EditorGUI.TextField(tRect, value);
                if (GUI.Button(new Rect(rect.xMax - 15f, rect.y, 15f, rect.height), "▾",
                    EditorStyles.miniButton))
                {
                    _customEdit.Remove(key);
                    if (System.Array.IndexOf(options, nv) < 0) nv = "";
                    GUI.changed = true;
                }
                return nv;
            }

            var display = new string[options.Length + 2];
            display[0] = emptyLabel;
            for (int i = 0; i < options.Length; i++)
                display[i + 1] = displayOptions != null && displayOptions.Length == options.Length
                    ? displayOptions[i] : options[i];
            display[display.Length - 1] = "custom…";

            int idx = string.IsNullOrEmpty(value) ? 0
                : System.Array.IndexOf(options, value) + 1;
            int nidx = EditorGUI.Popup(rect, idx, display);
            if (nidx != idx)
            {
                if (nidx == display.Length - 1)
                {
                    _customEdit.Add(key);
                    return value;
                }
                return nidx == 0 ? "" : options[nidx - 1];
            }
            return value;
        }

        string BackgroundPopup(Rect rect, string value, (VNRow, string) key)
            => SpritePopup(rect, value, key, _backgroundPreviews,
                SpriteFor(_backgroundPreviews, value), "-", "未选择背景",
                "清除选择", "没有匹配的背景", null);

        string CgPopup(Rect rect, string value, (VNRow, string) key)
            => SpritePopup(rect, value, key, _cgPreviews,
                SpriteFor(_cgPreviews, value), "-", "未选择 CG",
                "清除选择", "没有匹配的 CG", null);

        string CharacterPopup(Rect rect, string value, string expression,
            (VNRow, string) key, System.Action<string> afterSelect)
        {
            var items = new List<SpritePreviewItem>();
            foreach (var character in _characterPreviews)
                items.Add(new SpritePreviewItem(character.id, character.DefaultSprite));
            return SpritePopup(rect, value, key, items,
                CharacterSprite(value, expression), "(narration)", "未选择角色（旁白）",
                "设为旁白", "没有匹配的角色", afterSelect);
        }

        string ExpressionPopup(Rect rect, string value, string characterId,
            (VNRow, string) key)
        {
            CharacterPreviewItem character =
                _characterPreviews.Find(item => item.id == characterId);
            var items = character != null
                ? new List<SpritePreviewItem>(character.expressions)
                : new List<SpritePreviewItem>();
            return SpritePopup(rect, value, key, items,
                CharacterSprite(characterId, value), "(default)", "使用默认表情",
                "默认表情", "没有匹配的表情", null);
        }

        Sprite CharacterSprite(string characterId, string expression)
        {
            CharacterPreviewItem character =
                _characterPreviews.Find(item => item.id == characterId);
            if (character == null) return null;
            Sprite selected = SpriteFor(character.expressions, expression);
            return selected != null ? selected : character.DefaultSprite;
        }

        static Sprite SpriteFor(List<SpritePreviewItem> items, string id)
        {
            SpritePreviewItem item = items.Find(candidate => candidate.id == id);
            return item != null ? item.sprite : null;
        }

        string SpritePopup(Rect rect, string value, (VNRow, string) key,
            List<SpritePreviewItem> items, Sprite inlineSprite, string emptyLabel,
            string emptyTooltip, string clearLabel, string noMatchesLabel,
            System.Action<string> afterSelect)
        {
            bool registered = string.IsNullOrEmpty(value) ||
                items.Exists(item => item.id == value);
            bool custom = _customEdit.Contains(key) || !registered;
            if (custom)
            {
                var textRect = new Rect(rect.x, rect.y, rect.width - 16f, rect.height);
                string edited = EditorGUI.TextField(textRect, value);
                if (GUI.Button(new Rect(rect.xMax - 15f, rect.y, 15f, rect.height), "▾",
                    EditorStyles.miniButton))
                {
                    _customEdit.Remove(key);
                    if (!items.Exists(item => item.id == edited)) edited = "";
                    GUI.changed = true;
                }
                return edited;
            }

            bool openPicker = false;
            Rect popupRect = rect;
            if (rect.width >= 76f)
            {
                float previewWidth = Mathf.Min(42f, rect.height * 2.2f);
                var previewRect = new Rect(rect.x, rect.y, previewWidth, rect.height);
                popupRect = new Rect(previewRect.xMax + 2f, rect.y,
                    rect.width - previewWidth - 2f, rect.height);

                EditorGUI.DrawRect(previewRect, new Color(0.08f, 0.08f, 0.08f, 1f));
                string tooltip = string.IsNullOrEmpty(value) ? emptyTooltip : value;
                if (inlineSprite != null)
                {
                    DrawSpritePreview(new Rect(previewRect.x + 1f, previewRect.y + 1f,
                        previewRect.width - 2f, previewRect.height - 2f), inlineSprite);
                    string path = AssetDatabase.GetAssetPath(inlineSprite);
                    if (!string.IsNullOrEmpty(path)) tooltip += "\n" + path;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    GUI.Label(previewRect, "!", EditorStyles.centeredGreyMiniLabel);
                    tooltip += "\n没有可用的 Sprite 预览";
                }

                if (GUI.Button(previewRect, new GUIContent("", tooltip), GUIStyle.none))
                    openPicker = true;
            }

            string label = string.IsNullOrEmpty(value) ? emptyLabel : value;
            if (GUI.Button(popupRect, label, EditorStyles.popup)) openPicker = true;
            if (openPicker)
                ShowSpritePicker(rect, value, key, items, clearLabel, noMatchesLabel,
                    afterSelect);
            return value;
        }

        void ShowSpritePicker(Rect activatorRect, string value, (VNRow, string) key,
            List<SpritePreviewItem> items, string clearLabel, string noMatchesLabel,
            System.Action<string> afterSelect)
        {
            PopupWindow.Show(activatorRect, new SpritePickerPopup(
                new List<SpritePreviewItem>(items), value, clearLabel, noMatchesLabel,
                selected =>
                {
                    if (PopupValue(key) == selected) return;
                    PushUndo(_doc.GenerateText());
                    SetPopupValue(key, selected);
                    _customEdit.Remove(key);
                    afterSelect?.Invoke(selected);
                    Bump();
                    Repaint();
                },
                () =>
                {
                    _customEdit.Add(key);
                    Repaint();
                }));
        }

        static string PopupValue((VNRow, string) key)
        {
            switch (key.Item2)
            {
                case "say.speaker": return key.Item1.speaker;
                case "say.expr": return key.Item1.expression;
                default: return key.Item1.Get(key.Item2);
            }
        }

        static void SetPopupValue((VNRow, string) key, string value)
        {
            switch (key.Item2)
            {
                case "say.speaker":
                    key.Item1.speaker = value;
                    key.Item1.Set(key.Item2, ""); // 清理由旧版错误回调写入的普通参数
                    break;
                case "say.expr":
                    key.Item1.expression = value;
                    key.Item1.Set(key.Item2, "");
                    break;
                default:
                    key.Item1.Set(key.Item2, value);
                    break;
            }
        }

        static void DrawSpritePreview(Rect rect, Sprite sprite)
        {
            Rect source = sprite.textureRect;
            float aspect = source.width / Mathf.Max(1f, source.height);
            Rect fitted = rect;
            if (aspect > rect.width / rect.height)
            {
                fitted.height = rect.width / aspect;
                fitted.y += (rect.height - fitted.height) * 0.5f;
            }
            else
            {
                fitted.width = rect.height * aspect;
                fitted.x += (rect.width - fitted.width) * 0.5f;
            }

            Texture2D texture = sprite.texture;
            var uv = new Rect(source.x / texture.width, source.y / texture.height,
                source.width / texture.width, source.height / texture.height);
            GUI.DrawTextureWithTexCoords(fitted, texture, uv, true);
        }

        sealed class SpritePreviewItem
        {
            public readonly string id;
            public readonly Sprite sprite;

            public SpritePreviewItem(string id, Sprite sprite)
            {
                this.id = id;
                this.sprite = sprite;
            }
        }

        sealed class CharacterPreviewItem
        {
            public readonly string id;
            public readonly List<SpritePreviewItem> expressions;

            public CharacterPreviewItem(string id, List<SpritePreviewItem> expressions)
            {
                this.id = id;
                this.expressions = expressions;
            }

            public Sprite DefaultSprite => expressions.Count > 0 ? expressions[0].sprite : null;
        }

        sealed class SpritePickerPopup : PopupWindowContent
        {
            const float CellWidth = 154f;
            const float CellHeight = 116f;
            const float PreviewHeight = 82f;

            readonly List<SpritePreviewItem> _items;
            readonly string _selected;
            readonly string _clearLabel;
            readonly string _noMatchesLabel;
            readonly System.Action<string> _onSelect;
            readonly System.Action _onCustom;
            readonly List<SpritePreviewItem> _filtered =
                new List<SpritePreviewItem>();
            Vector2 _scroll;
            string _search = "";

            public SpritePickerPopup(List<SpritePreviewItem> items, string selected,
                string clearLabel, string noMatchesLabel,
                System.Action<string> onSelect, System.Action onCustom)
            {
                _items = items;
                _selected = selected;
                _clearLabel = clearLabel;
                _noMatchesLabel = noMatchesLabel;
                _onSelect = onSelect;
                _onCustom = onCustom;
            }

            public override Vector2 GetWindowSize() => new Vector2(520f, 430f);

            public override void OnGUI(Rect rect)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUI.SetNextControlName("BackgroundSearch");
                    _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                    if (GUILayout.Button(_clearLabel, EditorStyles.toolbarButton,
                            GUILayout.Width(72f)))
                    {
                        _onSelect("");
                        editorWindow.Close();
                        return;
                    }
                    if (GUILayout.Button("custom…", EditorStyles.toolbarButton,
                            GUILayout.Width(66f)))
                    {
                        _onCustom();
                        editorWindow.Close();
                        return;
                    }
                }

                BuildFilteredList();
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width - 12f) / CellWidth));
                int rows = Mathf.CeilToInt(_filtered.Count / (float)columns);
                Rect grid = GUILayoutUtility.GetRect(rect.width - 12f,
                    Mathf.Max(40f, rows * CellHeight));

                if (_filtered.Count == 0)
                {
                    GUI.Label(grid, _noMatchesLabel, EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    for (int i = 0; i < _filtered.Count; i++)
                    {
                        int column = i % columns;
                        int row = i / columns;
                        var cell = new Rect(grid.x + column * CellWidth,
                            grid.y + row * CellHeight, CellWidth - 6f, CellHeight - 6f);
                        DrawItem(cell, _filtered[i]);
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            void BuildFilteredList()
            {
                _filtered.Clear();
                foreach (var item in _items)
                {
                    if (string.IsNullOrEmpty(_search) ||
                        item.id.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        _filtered.Add(item);
                }
            }

            void DrawItem(Rect rect, SpritePreviewItem item)
            {
                bool selected = item.id == _selected;
                EditorGUI.DrawRect(rect, selected
                    ? new Color(0.25f, 0.55f, 0.9f, 0.42f)
                    : new Color(0f, 0f, 0f, 0.16f));

                var previewRect = new Rect(rect.x + 4f, rect.y + 4f,
                    rect.width - 8f, PreviewHeight);
                EditorGUI.DrawRect(previewRect, new Color(0.08f, 0.08f, 0.08f, 1f));
                if (item.sprite != null)
                    DrawSpritePreview(previewRect, item.sprite);
                else
                    GUI.Label(previewRect, "无预览", EditorStyles.centeredGreyMiniLabel);

                var labelRect = new Rect(rect.x + 4f, previewRect.yMax + 3f,
                    rect.width - 8f, 20f);
                GUI.Label(labelRect, selected ? $"✓  {item.id}" : item.id,
                    EditorStyles.centeredGreyMiniLabel);

                if (GUI.Button(rect, new GUIContent("", item.id), GUIStyle.none))
                {
                    _onSelect(item.id);
                    editorWindow.Close();
                }
            }

        }

        // ---- 添加菜单 ----

        void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Say line"), false, () => InsertRow(NewSayRow()));
            menu.AddSeparator("");
            foreach (var c in VNScenarioSchema.Commands)
            {
                var keyword = c.keyword;
                menu.AddItem(new GUIContent($"{c.category}/{keyword}"), false,
                    () => InsertRow(NewCommandRow(keyword)));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Comment (#)"), false,
                () => InsertRow(new VNRow { kind = VNRowKind.Raw, raw = "# " }));
            menu.AddItem(new GUIContent("Blank line"), false,
                () => InsertRow(new VNRow { kind = VNRowKind.Raw, raw = "" }));
            menu.ShowAsContext();
        }

        VNRow NewSayRow() => new VNRow { kind = VNRowKind.Say };

        VNRow NewCommandRow(string keyword)
        {
            var r = new VNRow { kind = VNRowKind.Command };
            SetKeyword(r, keyword);
            if (r.camLines != null && r.camLines.Count == 0)
                r.camLines.Add("> middle 1 1");
            return r;
        }

        void InsertRow(VNRow row)
        {
            MarkStructural();
            int at = _list.index >= 0 && _list.index < _doc.rows.Count
                ? _list.index + 1 : _doc.rows.Count;
            _doc.rows.Insert(at, row);
            _list.index = at;
            Bump();
            Repaint();
        }

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

    [InitializeOnLoad]
    static class VNPlayFromLineBridge
    {
        const string PendingKey = "VNEffects.PlayFromLine.Pending";
        const string SourceKey = "VNEffects.PlayFromLine.Source";
        const string LineKey = "VNEffects.PlayFromLine.Line";
        const string RebuildKey = "VNEffects.PlayFromLine.Rebuild";
        static int _remainingAttempts;

        static VNPlayFromLineBridge()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void Request(string source, int sourceLine, bool rebuildState)
        {
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(SourceKey, source);
            SessionState.SetInt(LineKey, Mathf.Max(1, sourceLine));
            SessionState.SetBool(RebuildKey, rebuildState);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode &&
                SessionState.GetBool(PendingKey, false))
            {
                _remainingAttempts = 180;
                EditorApplication.update -= TryStartRunner;
                EditorApplication.update += TryStartRunner;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= TryStartRunner;
            }
        }

        static void TryStartRunner()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= TryStartRunner;
                return;
            }

            var runner = Object.FindFirstObjectByType<VNScriptRunner>();
            if (runner == null || !runner.IsInitialized)
            {
                if (--_remainingAttempts > 0) return;
                Debug.LogError("[VNScript] 从选中行播放失败：找不到已初始化的 VNScriptRunner");
                ClearRequest();
                EditorApplication.update -= TryStartRunner;
                return;
            }

            string source = SessionState.GetString(SourceKey, "");
            int line = SessionState.GetInt(LineKey, 1);
            bool rebuildState = SessionState.GetBool(RebuildKey, true);
            ClearRequest();
            EditorApplication.update -= TryStartRunner;
            runner.PlayFromSourceLine(source, line, rebuildState);
        }

        static void ClearRequest()
        {
            SessionState.EraseBool(PendingKey);
            SessionState.EraseString(SourceKey);
            SessionState.EraseInt(LineKey);
            SessionState.EraseBool(RebuildKey);
        }
    }
}
