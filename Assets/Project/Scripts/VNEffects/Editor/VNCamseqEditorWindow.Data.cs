using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DG.Tweening;
using DG.Tweening.Core.Easing;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects.EditorTools
{
    public partial class VNCamseqEditorWindow
    {
        // ==================================================================
        // 与剧本编辑器的双向绑定
        // ==================================================================

        // 剧本编辑器每帧都要问「这行在编排吗」，逐行 FindObjectsOfTypeAll 太费；
        // 绑定关系本来就唯一，缓存成静态的。域重载后清空，下一次 SyncLink 立刻补回来。
        static VNScenarioEditorWindow s_linkEditor;
        static int s_linkRow = -1;

        /// <summary>剧本编辑器某行的「编排」按钮：打开窗口并绑定到那一行</summary>
        public static void OpenLinked(VNScenarioEditorWindow editor, int rowIndex)
        {
            var win = GetWindow<VNCamseqEditorWindow>("镜头编排");

            // 手动回写模式下手上还有没应用的稿，切走就没了——先问一句
            if (win.HasLink && win._linkedRow != rowIndex && !win._liveApply && win.LinkDirty)
            {
                if (EditorUtility.DisplayDialog("还有未应用的改动",
                        $"第 {win._linkedRow + 1} 行的编排还没写回剧本。要先应用再切过去吗？",
                        "应用后切换", "丢弃改动"))
                    win.ApplyToLink();
            }

            win.BindTo(editor, rowIndex, forceLoad: true);
            win.Focus();
        }

        /// <summary>剧本编辑器画「编排」按钮时问：这一行是不是正被编排？</summary>
        public static bool IsLinkedTo(VNScenarioEditorWindow editor, int rowIndex) =>
            s_linkEditor == editor && s_linkRow == rowIndex && rowIndex >= 0;

        void BindTo(VNScenarioEditorWindow editor, int rowIndex, bool forceLoad)
        {
            bool changed = _linkedEditor != editor || _linkedRow != rowIndex;
            _linkedEditor = editor;
            _linkedRow = rowIndex;
            s_linkEditor = editor;
            s_linkRow = rowIndex;
            if (changed || forceLoad) LoadFromLink();
        }

        bool HasLink => _linkedEditor != null && _linkedRow >= 0 &&
                        _linkedEditor.IsCamseqRow(_linkedRow);

        /// <summary>当前编排内容与剧本那一行不一致（手动回写模式下的脏标记）</summary>
        bool LinkDirty => HasLink && GenerateText() != _lastAppliedText;

        /// <summary>
        /// 每帧对齐绑定关系：没绑过就找一个打开着的剧本编辑器；未锁定时跟随它的选中行。
        /// 手动回写模式下一旦有未应用的改动就自动上锁——宁可停在原地，也不能悄悄切走丢稿。
        /// </summary>
        void SyncLink()
        {
            if (_linkedEditor == null)
            {
                var windows = Resources.FindObjectsOfTypeAll<VNScenarioEditorWindow>();
                if (windows.Length == 0) return;
                _linkedEditor = windows[0];
                _linkedRow = -1;
                // 从菜单打开、手上已经摆了点的：自动上锁，绝不让自动跟随覆盖掉现成的稿。
                // 用户点剧本行的「编排」或这里的「从剧本重载」才会真正接管。
                if (_points.Count > 0) _linkLocked = true;
            }

            if (!_liveApply && LinkDirty) _linkLocked = true;
            if (_linkLocked)
            {
                // 锁着也要让剧本编辑器的按钮高亮保持正确
                s_linkEditor = _linkedEditor;
                s_linkRow = _linkedRow;
                return;
            }

            int selected = _linkedEditor.SelectedCamseqRow();
            if (selected >= 0 && selected != _linkedRow) BindTo(_linkedEditor, selected, false);
            s_linkEditor = _linkedEditor;
            s_linkRow = _linkedRow;
        }

        /// <summary>从绑定的剧本行读回内容（覆盖当前编排）</summary>
        void LoadFromLink()
        {
            if (!HasLink) return;
            if (!_linkedEditor.TryGetCamseqText(_linkedRow, out string text)) return;
            _pasteText = text;
            ParseText(silent: true);
            _lastAppliedText = GenerateText();
            _scrub = 0f;
            ResetUndo();   // 换了一行 = 换了一段历史
            Repaint();
        }

        // ==================================================================
        // 撤销 / 重做
        // ==================================================================

        bool CanUndo => _undoStack.Count > 0 || GenerateText() != _undoBaseline;
        bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// 每拍看一眼内容变没变。变了要等「鼠标松开 + 静默一小会儿」才落一步，
        /// 拖滑条那 200 帧、连着敲的几个字符就自然合并成一次撤销。
        /// </summary>
        void TrackUndo()
        {
            string current = GenerateText();
            if (current == _undoBaseline) { _undoPending = null; return; }

            // 还在改（值一直在变）/ 鼠标还按着（拖点、拖滑条）→ 不切分
            if (_undoPending != current || GUIUtility.hotControl != 0 ||
                _dragMode != DragMode.None)
            {
                _undoPending = current;
                _undoPendingTime = EditorApplication.timeSinceStartup;
                return;
            }
            if (EditorApplication.timeSinceStartup - _undoPendingTime < UndoIdleSeconds) return;
            CommitUndo();
        }

        /// <summary>把还没落栈的改动立刻切成一步（清空/套预设这种大动作前先调，免得跟上一次微调粘一起）</summary>
        void CommitUndo()
        {
            string current = GenerateText();
            if (current == _undoBaseline) return;
            _undoStack.Add(_undoBaseline);
            if (_undoStack.Count > UndoDepth) _undoStack.RemoveAt(0);
            _redoStack.Clear();
            _undoBaseline = current;
            _undoPending = null;
        }

        /// <summary>换绑定行 / 从剧本重载后重开一段历史：绝不让 Ctrl+Z 撤回上一行的内容</summary>
        void ResetUndo()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _undoBaseline = GenerateText();
            _undoPending = null;
        }

        void PerformUndo()
        {
            CommitUndo();                       // 手上没落栈的改动也算一步
            if (_undoStack.Count == 0) return;
            int last = _undoStack.Count - 1;
            string snapshot = _undoStack[last];
            _undoStack.RemoveAt(last);
            _redoStack.Add(GenerateText());
            RestoreSnapshot(snapshot);
        }

        void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            int last = _redoStack.Count - 1;
            string snapshot = _redoStack[last];
            _redoStack.RemoveAt(last);
            _undoStack.Add(GenerateText());
            RestoreSnapshot(snapshot);
        }

        void RestoreSnapshot(string text)
        {
            ParseTextFrom(text, silent: true);
            _undoBaseline = GenerateText();
            _undoPending = null;
            // 焦点还留在数字框里的话，框里显示的仍是撤销前那串字符 → 强制丢焦点重画
            GUIUtility.keyboardControl = 0;
            if (_list != null) _list.index = Mathf.Min(_list.index, _points.Count - 1);
            Repaint();
        }

        // 快捷键走 ShortcutManager（窗口作用域）：本窗口有焦点时优先于全局 Undo，
        // 键位可在 Edit → Shortcuts 里改。撤销范围只有这个窗口，与场景编辑互不干扰。
        [Shortcut("VN/Camseq Editor/Undo", typeof(VNCamseqEditorWindow),
            KeyCode.Z, ShortcutModifiers.Action)]
        static void ShortcutUndo(ShortcutArguments args)
        {
            if (args.context is VNCamseqEditorWindow w) w.PerformUndo();
        }

        [Shortcut("VN/Camseq Editor/Redo", typeof(VNCamseqEditorWindow),
            KeyCode.Y, ShortcutModifiers.Action)]
        static void ShortcutRedo(ShortcutArguments args)
        {
            if (args.context is VNCamseqEditorWindow w) w.PerformRedo();
        }

        [Shortcut("VN/Camseq Editor/Redo (Alt)", typeof(VNCamseqEditorWindow),
            KeyCode.Z, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        static void ShortcutRedoAlt(ShortcutArguments args)
        {
            if (args.context is VNCamseqEditorWindow w) w.PerformRedo();
        }

        /// <summary>把当前编排写回剧本行</summary>
        void ApplyToLink()
        {
            if (!HasLink) return;
            string text = GenerateText();
            if (text == _lastAppliedText) return;
            if (_linkedEditor.ApplyCamseqText(_linkedRow, text))
            {
                _lastAppliedText = text;
                _linkLocked = false;   // 应用完就重新跟随
            }
        }

        /// <summary>
        /// 绑定状态条：只留「哪一行 + 有没有未应用改动」，其余全部收进 ⚙ 菜单。
        /// 原来 7 个控件平铺在一条 21px 的 toolbar 上，而其中
        /// 「从剧本重载 / 底图 / 立绘」都是一天点一次的东西，天天占着视线。
        /// </summary>
        void DrawLinkBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (!HasLink)
                {
                    GUILayout.Label(_linkedEditor == null
                        ? "未连接剧本编辑器（打开 Scenario Editor 后点 camseq 行的「编排」）"
                        : "剧本里选中的不是 camseq 行", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    DrawSettingsButton();   // 没绑定剧本时也要能改底图/立绘
                    return;
                }

                // 未应用改动没有独立按钮了（应用在 ⚙ 菜单里），所以状态文字必须够显眼：
                // 橙色 + 加粗 + 「（未应用）」三重提示，漏看就等于改动没进剧本
                bool dirty = LinkDirty;
                var prevContent = GUI.contentColor;
                if (dirty) GUI.contentColor = new Color(1f, 0.75f, 0.25f);
                if (GUILayout.Button(
                        new GUIContent($"◆ {_linkedEditor.ScenarioDisplayName} 第 {_linkedRow + 1} 行"
                                       + (dirty ? "（未应用）" : "")
                                       + (_linkLocked ? "  [已锁定]" : ""),
                            "点一下把剧本编辑器里的这一行滚到眼前"),
                        dirty ? EditorStyles.boldLabel : EditorStyles.label,
                        GUILayout.Width(280f)))
                    _linkedEditor.FocusRow(_linkedRow);
                GUI.contentColor = prevContent;

                GUILayout.FlexibleSpace();
                DrawSettingsButton();
            }
        }

        void DrawSettingsButton()
        {
            var r = GUILayoutUtility.GetRect(new GUIContent("⚙ 设置 ▾"),
                EditorStyles.toolbarButton, GUILayout.Width(70f));
            if (GUI.Button(r, new GUIContent("⚙ 设置 ▾",
                    "绑定方式（跟随/实时回写/应用/重载）与画布底图、立绘显示"),
                    EditorStyles.toolbarButton))
                ShowSettingsMenu(r);
        }

        /// <summary>绑定与画布设置：全部低频操作收在这里，勾选态即当前状态</summary>
        void ShowSettingsMenu(Rect rect)
        {
            var menu = new GenericMenu();

            if (HasLink)
            {
                menu.AddItem(new GUIContent("跟随剧本选中行"), !_linkLocked, () =>
                {
                    _linkLocked = !_linkLocked;
                    Repaint();
                });
                menu.AddItem(new GUIContent("实时回写剧本"), _liveApply, () =>
                {
                    _liveApply = !_liveApply;
                    if (_liveApply) ApplyToLink();
                    Repaint();
                });
                menu.AddSeparator("");
                if (LinkDirty)
                    menu.AddItem(new GUIContent("应用回剧本"), false, () =>
                    {
                        ApplyToLink();
                        Repaint();
                    });
                else
                    menu.AddDisabledItem(new GUIContent("应用回剧本（没有未应用的改动）"));
                menu.AddItem(new GUIContent("从剧本重载（丢弃这里的改动）"), false, () =>
                {
                    LoadFromLink();
                    _linkLocked = false;
                    Repaint();
                });
                menu.AddSeparator("");
            }

            // GenericMenu 把 '/' 当子菜单分隔符，素材 id 里真带斜杠会被切成两级
            string follow = _rowStage != null && _rowStage.backdrop != null
                ? $"底图/跟随剧本（{MenuSafe(_rowStage.cgId ?? _rowStage.bgId)}）"
                : "底图/跟随剧本";
            menu.AddItem(new GUIContent(follow), string.IsNullOrEmpty(_bgOverrideId), () =>
            {
                _bgOverrideId = "";
                Repaint();
            });
            foreach (var id in BackgroundIds())
            {
                string picked = id;
                menu.AddItem(new GUIContent("底图/" + MenuSafe(id)), _bgOverrideId == picked, () =>
                {
                    _bgOverrideId = picked;
                    Repaint();
                });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("显示立绘"), _showPortraits, () =>
            {
                _showPortraits = !_showPortraits;
                Repaint();
            });
            menu.DropDown(rect);
        }

        static string MenuSafe(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('/', '_');

        /// <summary>绑定行的舞台快照（背景 / CG / 在场角色），没绑定返回 null</summary>
        VNRowStageInfo RowStage
        {
            get
            {
                if (!HasLink)
                {
                    _rowStage = null;
                    _rowStageRow = -1;
                    return null;
                }
                int version = _linkedEditor.DocVersion;
                if (_rowStage == null || _rowStageRow != _linkedRow ||
                    _rowStageVersion != version)
                {
                    _linkedEditor.TryGetRowStage(_linkedRow, out _rowStage);
                    _rowStageRow = _linkedRow;
                    _rowStageVersion = version;
                }
                return _rowStage;
            }
        }

        void OnEnable()
        {
            // minSize 必须在这里设，不能只写在 Open()/OpenLinked() 里：
            // 它是窗口自己序列化的属性，已经开着的窗口不会因为那两行改动而更新，
            // 结果就是「代码里写了 360，窗口还是拖不到 720 以下」
            minSize = new Vector2(460f, 300f);

            _list = new ReorderableList(_points, typeof(Waypoint), true, true, true, true)
            {
                drawHeaderCallback = r => GUI.Label(r,
                    "路径点（拖手柄排序 | 时长 0 = 瞬切 | ⋯ = 缓动 / 叠化 / 停留 / 震屏）"),
                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 6f,
                drawElementCallback = DrawElement,
                onAddCallback = l => _points.Add(new Waypoint()),
            };
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);

            // 纯外观偏好 → EditorPrefs（关窗重开、换项目窗口都还在）
            _guides = (Guides)EditorPrefs.GetInt(GuidesPrefKey, (int)DefaultGuides);
            _canvasHeight = EditorPrefs.GetFloat(CanvasHeightPrefKey, 0f);
            _leftColumnWidth = EditorPrefs.GetFloat(ColumnWidthPrefKey, 0f);
            _textFold = EditorPrefs.GetBool(TextFoldPrefKey, false);
            // 域重载后 _undoBaseline 是序列化带回来的，别覆盖掉；只有全新窗口才初始化
            if (string.IsNullOrEmpty(_undoBaseline)) _undoBaseline = GenerateText();

            // 域重载会把 DontSave 的临时立绘销毁，但列表本身没序列化——
            // 重编译时场景预览还开着的话，这里按记录的状态把舞台重新摆一遍。
            if (_scenePreviewing)
            {
                _stagedRow = -1;
                ApplyStageToScene();
            }
        }

        void OnDisable()
        {
            StopScenePreview();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            // 进出 Play 前后都还原场景，避免把预览状态序列化进场景/运行副本
            StopScenePreview();
        }

        void Update()
        {
            TrackUndo();
            if (_playing)
            {
                double now = EditorApplication.timeSinceStartup;
                _scrub += (float)(now - _lastUpdateTime);
                _lastUpdateTime = now;
                if (_scrub >= TotalDuration())
                {
                    _scrub = TotalDuration();
                    _playing = false;
                }
                Repaint();
            }
            if (_scenePreviewing)
            {
                // 绑定跟到别的行了 → 舞台要跟着换
                if (HasLink && _stagedRow != _linkedRow) ApplyStageToScene();
                ApplySceneState();
            }
        }
    }
}
