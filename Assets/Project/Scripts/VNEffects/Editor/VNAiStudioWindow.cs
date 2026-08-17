using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VNEffects;

namespace VNEffectsEditor
{
    /// <summary>
    /// AI 试聊台：不进 Play Mode 就能改人格、试聊、看提示词、调记忆。
    /// 菜单 Tools → VN Effects → AI → AI Talk Studio。
    ///
    /// 【三栏各自解决什么】
    ///   左  改参数——改的是**草稿**（内存副本），资产不动，满意了才「写回资产」
    ///   中  聊天流——可点选项、可自由输入任意回复、可重跑本轮、可从任意轮重新分岔
    ///   右  system prompt 实时预览——**不发请求、不花钱、不等 1.5 秒**，
    ///       改左栏一个字右栏立刻重拼。调提示词的主力在这一栏
    ///
    /// 【域重载】
    ///   窗口状态走 [SerializeField] + ISerializationCallbackReceiver 活下来
    ///   （项目惯例，同 VNScenarioEditorWindow）。会话历史靠轮次记录重建，
    ///   见 VNAiStudioSession.Rebuild。**正在飞的那个请求救不回来**，
    ///   重载后会显示为「已中断」，重跑一轮即可。
    ///   ★ 加新窗口状态时必须同时改 OnBeforeSerialize 和 OnEnable。
    /// </summary>
    public class VNAiStudioWindow : EditorWindow, ISerializationCallbackReceiver
    {
        [MenuItem("Tools/VN Effects/AI/AI Talk Studio", false, 430)]
        public static void Open()
        {
            var w = GetWindow<VNAiStudioWindow>("AI 试聊台");
            w.minSize = new Vector2(980, 520);
            if (w._persona == null && Selection.activeObject is VNAiPersonaDef p)
                w.BindPersona(p);
            w.Show();
        }

        /// <summary>人格资产上右键 → 直接开窗口并绑定它。</summary>
        [MenuItem("CONTEXT/VNAiPersonaDef/在 AI 试聊台里打开")]
        static void OpenFromContext(MenuCommand cmd)
        {
            var w = GetWindow<VNAiStudioWindow>("AI 试聊台");
            w.minSize = new Vector2(980, 520);
            w.BindPersona(cmd.context as VNAiPersonaDef);
            w.Show();
        }

        // ──────────────── 状态 ────────────────

        [SerializeField] VNAiPersonaDef _persona;

        // 情境（对应剧本 event aitalk 的那几个参数）
        [SerializeField] string _topic = "";
        [SerializeField] string _place = "放学后的空教室，夕阳照进来";
        [SerializeField] string _playerName = "我";
        [SerializeField] string _statName = "好感";
        [SerializeField] int _affection = 40;
        [SerializeField] int _maxTurns = 8;
        [SerializeField] int _optionOverride;          // 0 = 按人格的扩展开关

        // 记忆
        [SerializeField] string _presetName = "";
        [SerializeField] bool _injectMemory = true;    // 这次要不要把记忆喂进 prompt
        [SerializeField] bool _writeMemory = true;     // 结束时要不要发总结请求
        [SerializeField] bool _writeLog = true;

        // 草稿与会话的序列化载体
        [SerializeField] string _draftJson;
        [SerializeField] List<VNAiStudioTurn> _turnsBackup = new List<VNAiStudioTurn>();
        [SerializeField] bool _sessionWasLive;
        [SerializeField] bool _interrupted;            // 域重载打断了一个在飞的请求

        // UI
        [SerializeField] string _freeInput = "";
        [SerializeField] int _inspectTurn = -1;        // 右栏在看第几轮（-1 = 预览当前）
        [SerializeField] bool _foldContext = true, _foldPersona = true, _foldMemory = true;
        [SerializeField] bool _foldPrompt = true, _foldJson, _foldSchema, _foldDiag = true;

        VNAiStudioDraft _draft;
        VNAiStudioSession _session;
        VNAiStudioMemoryPreset _preset;
        VNAiConversation.VNAiSessionSummary _pendingSummary;   // 等你决定收不收
        string _summaryError;
        VNAiEditorCoroutine _importing;

        Vector2 _leftScroll, _midScroll, _rightScroll;
        float _leftW = 340f, _rightW = 400f;
        int _dragging;                                  // 0 无 / 1 左分隔条 / 2 右分隔条

        const string PrefLeft = "VNAiStudio.LeftWidth";
        const string PrefRight = "VNAiStudio.RightWidth";
        const float MinPane = 240f;

        // ──────────────── 生命周期 ────────────────

        void OnEnable()
        {
            _leftW = EditorPrefs.GetFloat(PrefLeft, 340f);
            _rightW = EditorPrefs.GetFloat(PrefRight, 400f);

            _draft = new VNAiStudioDraft();
            if (_persona != null) _draft.Restore(_persona, _draftJson);

            _session = new VNAiStudioSession { onChanged = Repaint };
            if (_turnsBackup != null && _turnsBackup.Count > 0)
            {
                _session.turns.AddRange(_turnsBackup);
                _session.RecomputeTotalsFromTurns();   // 会话对象是新建的，累计数要补回来
                if (_sessionWasLive && _draft.IsValid)
                    _session.Rebuild(_draft.Draft, BuildContext);
            }

            if (!string.IsNullOrEmpty(_presetName)) _preset = VNAiStudioMemory.Load(_presetName);

            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _importing?.Stop();
            _session?.Abort();
            _draft?.Dispose();
            EditorPrefs.SetFloat(PrefLeft, _leftW);
            EditorPrefs.SetFloat(PrefRight, _rightW);
        }

        public void OnBeforeSerialize()
        {
            // ★ 加新窗口状态时这里和 OnEnable 要一起改
            _draftJson = _draft != null ? _draft.ToJson() : null;
            _turnsBackup = _session != null
                ? new List<VNAiStudioTurn>(_session.turns) : new List<VNAiStudioTurn>();
            _sessionWasLive = _session != null && _session.IsLive;
            // 在飞的请求活不过域重载：先标记，重载后提示可重跑
            if (_session != null && _session.IsBusy) _interrupted = true;
        }

        public void OnAfterDeserialize() { }

        void OnEditorUpdate()
        {
            if (_session != null && _session.IsBusy) Repaint();
            if (_importing != null && _importing.IsRunning) Repaint();
        }

        void BindPersona(VNAiPersonaDef p)
        {
            if (p == null || p == _persona) return;
            _persona = p;
            _draft.Bind(p);
            _session.Clear();
            _pendingSummary = null;
            _interrupted = false;
        }

        // ──────────────── 主布局 ────────────────

        void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            {
                DrawLeftPane();
                Splitter(1);
                DrawMiddlePane();
                Splitter(2);
                DrawRightPane();
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                EditorGUI.BeginChangeCheck();
                var picked = (VNAiPersonaDef)EditorGUILayout.ObjectField(
                    _persona, typeof(VNAiPersonaDef), false, GUILayout.Width(200));
                if (EditorGUI.EndChangeCheck()) BindPersona(picked);

                DrawProviderPicker();

                if (_draft != null && _draft.IsDirty)
                {
                    var c = GUI.color;
                    GUI.color = new Color(1f, 0.85f, 0.4f);
                    GUILayout.Label($"● 草稿已改 {_draft.ChangedFields.Count} 项",
                                    EditorStyles.toolbarButton, GUILayout.Width(110));
                    GUI.color = c;

                    if (GUILayout.Button("写回资产", EditorStyles.toolbarButton, GUILayout.Width(64)))
                        _draft.ApplyToAsset();
                    if (GUILayout.Button("还原", EditorStyles.toolbarButton, GUILayout.Width(40)))
                        _draft.RevertFromAsset();
                }

                GUILayout.FlexibleSpace();

                if (_session != null && _session.turns.Count > 0)
                {
                    GUILayout.Label(
                        $"{_session.turns.Count}/{_maxTurns} 轮   {_session.TotalSeconds:0.0}s   " +
                        $"{(_session.TotalPromptTokens + _session.TotalOutputTokens) / 1000f:0.0}k tok   " +
                        $"好感 {_session.AffectionTotal:+#;-#;0}",
                        EditorStyles.miniLabel);

                    // 成本单独上色：thinking 调成 High 时它会翻好几倍，而试聊台
                    // 天生鼓励反复重跑，不盯着这个数字很容易一下午烧掉几块钱
                    var cost = GUI.color;
                    if (_session.TotalCostUsd >= 0.05) GUI.color = new Color(1f, 0.5f, 0.4f);
                    GUILayout.Label($"≈${_session.TotalCostUsd:0.0000}", EditorStyles.miniLabel);
                    GUI.color = cost;
                }

                if (_draft != null && _draft.IsValid &&
                    _draft.Draft.thinking != VNAiThinking.Minimal)
                {
                    var c = GUI.color;
                    GUI.color = new Color(1f, 0.5f, 0.4f);
                    GUILayout.Label($"⚠ thinking={_draft.Draft.thinking}（贵约 6 倍）",
                                    EditorStyles.miniLabel);
                    GUI.color = c;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 工具栏上的供应商 / 模型下拉。**改的是草稿**，所以不写回资产就能
        /// 「同一句话用 Gemini 和 DeepSeek 各跑一遍」对比效果和成本——
        /// 这正是试聊台存在的意义（不进 Play Mode 调参）。
        /// 想固化到人格资产，点旁边的「写回资产」。
        /// </summary>
        void DrawProviderPicker()
        {
            if (_draft == null || !_draft.IsValid) return;

            var d = _draft.Draft;

            EditorGUI.BeginChangeCheck();
            var choice = (VNAiProviderChoice)EditorGUILayout.EnumPopup(
                d.provider, EditorStyles.toolbarPopup, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
            {
                d.provider = choice;
                // 换家之后旧模型名一定不属于新那家，留着必 400。留空 = 用新家的默认模型
                if (!string.IsNullOrWhiteSpace(d.model) &&
                    VNAiProviders.TryFromModelName(d.model, out VNAiProvider owner) &&
                    owner != d.ResolveProvider())
                    d.model = "";
                _draft.NotifyExternalEdit();
            }

            // 模型名直接可填，方便临时换 flash ⇄ pro 对比
            EditorGUI.BeginChangeCheck();
            string model = EditorGUILayout.TextField(
                d.model, EditorStyles.toolbarTextField, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
            {
                d.model = model;
                _draft.NotifyExternalEdit();
            }
            if (string.IsNullOrWhiteSpace(d.model))
                GUILayout.Label($"（{d.ResolveModel()}）", EditorStyles.miniLabel,
                                GUILayout.Width(150));
        }

        void Splitter(int id)
        {
            var r = GUILayoutUtility.GetRect(4f, 4f, GUILayout.Width(4f),
                                             GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                _dragging = id;
                e.Use();
            }
            else if (_dragging == id)
            {
                if (e.type == EventType.MouseDrag)
                {
                    float max = position.width - MinPane * 2f - 8f;
                    if (id == 1) _leftW = Mathf.Clamp(_leftW + e.delta.x, MinPane, max);
                    else _rightW = Mathf.Clamp(_rightW - e.delta.x, MinPane, max);
                    Repaint();
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _dragging = 0;
                    EditorPrefs.SetFloat(PrefLeft, _leftW);
                    EditorPrefs.SetFloat(PrefRight, _rightW);
                    e.Use();
                }
            }
        }

        // ──────────────── 左栏：配置 ────────────────

        void DrawLeftPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_leftW));
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            {
                if (_persona == null)
                {
                    EditorGUILayout.HelpBox(
                        "先在上面选一套 AI 人格资产（VNAiPersonaDef）。\n" +
                        "工程里还没有的话：Create → VN → AI Persona。",
                        MessageType.Info);
                }
                else
                {
                    DrawContextSection();
                    EditorGUILayout.Space(4);
                    DrawPersonaSection();
                    EditorGUILayout.Space(4);
                    DrawMemorySection();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawContextSection()
        {
            _foldContext = EditorGUILayout.Foldout(_foldContext, "情境（相当于剧本参数）", true,
                                                   EditorStyles.foldoutHeader);
            if (!_foldContext) return;

            EditorGUI.indentLevel++;
            _topic = EditorGUILayout.TextField(
                new GUIContent("topic 话题", "只在第 1 轮注入"), _topic);
            _place = EditorGUILayout.TextField("place 场景", _place);
            _playerName = EditorGUILayout.TextField("me 我的称呼", _playerName);
            _maxTurns = EditorGUILayout.IntSlider("turns 轮数上限", _maxTurns, 1, 30);

            EditorGUILayout.BeginHorizontal();
            _statName = EditorGUILayout.TextField("stat 属性名", _statName);
            _affection = EditorGUILayout.IntSlider(_affection, 0, 100);
            EditorGUILayout.EndHorizontal();

            // 好感翻成人话的档位与游戏内共用同一份实现，窗口里调好的分寸进游戏才一致
            string affectionText = VNAiTalkModule.BuildAffectionText(_statName, _affection);
            if (!string.IsNullOrEmpty(affectionText))
                EditorGUILayout.LabelField(" ", $"→ {affectionText}", EditorStyles.miniLabel);

            _optionOverride = EditorGUILayout.IntSlider(
                new GUIContent("options 覆盖", "0 = 按人格的扩展选项开关"),
                _optionOverride, 0, VNAiPersonaDef.MaxOptions);
            if (_optionOverride > 0 && _optionOverride < VNAiPersonaDef.MinOptions)
                _optionOverride = VNAiPersonaDef.MinOptions;

            // 这一条是最常配错的：turns 超过 historyTurns，她后半程就真的看不见开头了
            if (_draft.IsValid && _maxTurns > _draft.Draft.historyTurns)
                EditorGUILayout.HelpBox(
                    $"轮数上限({_maxTurns}) > 历史保留({_draft.Draft.historyTurns})：" +
                    $"第 {_draft.Draft.historyTurns + 1} 轮起最早的对话会被裁掉，她会「忘记」开头。",
                    MessageType.Warning);

            EditorGUI.indentLevel--;
        }

        void DrawPersonaSection()
        {
            _foldPersona = EditorGUILayout.Foldout(_foldPersona, "人格草稿（改这里不动资产）", true,
                                                   EditorStyles.foldoutHeader);
            if (!_foldPersona) return;

            if (_draft.IsDirty)
            {
                var sb = new System.Text.StringBuilder("相对资产改了：\n");
                foreach (string s in _draft.ChangedFields) sb.Append("• ").AppendLine(s);
                EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.None);
            }

            var errors = _draft.IsValid ? _draft.Draft.Validate() : new List<string>();
            if (errors.Count > 0)
                EditorGUILayout.HelpBox("配置有问题：\n• " + string.Join("\n• ", errors),
                                        MessageType.Error);

            EditorGUI.indentLevel++;
            _draft.DrawFields();
            EditorGUI.indentLevel--;
        }

        // ──────────────── 左栏：记忆 ────────────────

        void DrawMemorySection()
        {
            _foldMemory = EditorGUILayout.Foldout(_foldMemory, "记忆（跨场）", true,
                                                  EditorStyles.foldoutHeader);
            if (!_foldMemory) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            {
                var names = VNAiStudioMemory.ListPresets();
                int cur = names.IndexOf(_presetName);
                var display = new List<string> { "（不用记忆）" };
                display.AddRange(names);

                int picked = EditorGUILayout.Popup("预设", cur + 1, display.ToArray());
                if (picked != cur + 1)
                {
                    _presetName = picked <= 0 ? "" : names[picked - 1];
                    _preset = string.IsNullOrEmpty(_presetName)
                        ? null : VNAiStudioMemory.Load(_presetName);
                }

                if (GUILayout.Button("＋", GUILayout.Width(24))) CreatePreset();
                if (_preset != null)
                {
                    if (GUILayout.Button("保存", GUILayout.Width(40)))
                        VNAiStudioMemory.Save(_preset);
                    if (GUILayout.Button("×", GUILayout.Width(22)) &&
                        EditorUtility.DisplayDialog("删除记忆预设",
                            $"确定删除「{_preset.name}」？", "删除", "取消"))
                    {
                        VNAiStudioMemory.Delete(_preset.name);
                        _preset = null;
                        _presetName = "";
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // 两个开关刻意分开：前者答「记忆对她有什么影响」（勾掉再跑一遍就能直接对比），
            // 后者答「这场要不要沉淀下来」（会多发一次总结请求，约 $0.001）
            _injectMemory = EditorGUILayout.ToggleLeft(
                new GUIContent("把记忆注入 prompt", "勾掉再跑一遍，就能直接对比有记忆和没记忆的差别"),
                _injectMemory);
            _writeMemory = EditorGUILayout.ToggleLeft(
                new GUIContent("结束时做总结（写记忆/日记）", "多发一次请求，约 $0.001；结果先给你看，收不收你定"),
                _writeMemory);
            _writeLog = EditorGUILayout.ToggleLeft(
                new GUIContent("结束时写日志到 AiTalkLogs/Editor/", "格式与游戏内日志完全一致"),
                _writeLog);

            if (_injectMemory && _draft.IsValid && !_draft.Draft.enableMemory)
                EditorGUILayout.HelpBox(
                    "人格的 enableMemory 是关的：窗口里能注入，但游戏内不会。",
                    MessageType.Warning);

            if (_preset != null) DrawMemoryEntries();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("导入 ▾", GUILayout.Width(70))) ShowImportMenu();
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        void DrawMemoryEntries()
        {
            if (_preset.entries.Count == 0)
            {
                EditorGUILayout.LabelField("（这套预设还没有条目）", EditorStyles.miniLabel);
            }

            for (int i = 0; i < _preset.entries.Count; i++)
            {
                var e = _preset.entries[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"#{i + 1}  {e.personaId}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("×", GUILayout.Width(20)))
                    {
                        _preset.entries.RemoveAt(i);
                        VNAiStudioMemory.Save(_preset);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();

                    e.summary = EditorGUILayout.TextField("摘要", e.summary);
                    e.place = EditorGUILayout.TextField("场景", e.place);
                    e.topics = DrawStringList("话题", e.topics);
                    e.facts = DrawStringList("关键事实", e.facts);
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("＋ 手写一条记忆"))
            {
                _preset.entries.Add(new VNAiMemoryEntry
                {
                    personaId = _persona != null ? _persona.id : "",
                    characterId = _persona != null && _persona.character != null
                        ? _persona.character.id : "",
                    savedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    summary = "",
                });
            }
        }

        /// <summary>逗号分隔的一行编辑，比 ReorderableList 省地方，改起来也快。</summary>
        static List<string> DrawStringList(string label, List<string> list)
        {
            string joined = list != null ? string.Join("、", list) : "";
            string edited = EditorGUILayout.TextField(
                new GUIContent(label, "用「、」分隔"), joined);
            if (edited == joined) return list;

            var result = new List<string>();
            foreach (string s in edited.Split('、', ',', '，'))
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
            return result;
        }

        void CreatePreset()
        {
            string name = "预设 " + (VNAiStudioMemory.ListPresets().Count + 1);
            _preset = new VNAiStudioMemoryPreset { name = name };
            if (VNAiStudioMemory.Save(_preset)) _presetName = name;
        }

        void ShowImportMenu()
        {
            var menu = new GenericMenu();

            foreach (var log in VNAiStudioMemory.ListLogs())
            {
                var captured = log;
                menu.AddItem(new GUIContent("从对话日志/" + log.display.Replace('/', '·')),
                             false, () => ImportFromLog(captured));
            }
            if (VNAiStudioMemory.ListLogs().Count == 0)
                menu.AddDisabledItem(new GUIContent("从对话日志/（AiTalkLogs 里没有日志）"));

            var slots = VNAiStudioMemory.ListSaveSlots();
            foreach (int slot in slots)
            {
                int captured = slot;
                menu.AddItem(new GUIContent($"从游戏存档/槽位 {captured}"), false,
                             () => ImportFromSave(captured));
            }
            if (slots.Count == 0)
                menu.AddDisabledItem(new GUIContent("从游戏存档/（没有存档）"));

            menu.ShowAsContext();
        }

        void ImportFromLog(VNAiStudioMemory.LogFile log)
        {
            if (!EnsurePreset()) return;
            if (!_draft.IsValid) return;

            // 日志里没有 summary/topics/facts（总结不写进日志），所以要发一次请求
            // 把那场对话重新总结出来，否则导进来的是一条空壳条目
            _importing = VNAiEditorCoroutine.Start(
                VNAiStudioMemory.ImportFromLogCo(log, _draft.Draft, _playerName,
                    (entry, err) =>
                    {
                        if (entry == null)
                        {
                            Debug.LogError($"[VNAiStudio] 从日志导入失败：{err}");
                        }
                        else
                        {
                            _preset.entries.Add(entry);
                            VNAiStudioMemory.Save(_preset);
                            Debug.Log($"[VNAiStudio] 已从日志导入一条记忆：{entry.summary}");
                        }
                        Repaint();
                    }));
        }

        void ImportFromSave(int slot)
        {
            if (!EnsurePreset()) return;
            var list = VNAiStudioMemory.ImportFromSave(slot);
            if (list.Count == 0)
            {
                Debug.LogWarning($"[VNAiStudio] 存档槽 {slot} 里没有 AI 记忆");
                return;
            }
            _preset.entries.AddRange(list);
            VNAiStudioMemory.Save(_preset);
            Debug.Log($"[VNAiStudio] 已从存档槽 {slot} 导入 {list.Count} 条记忆");
        }

        bool EnsurePreset()
        {
            if (_preset != null) return true;
            CreatePreset();
            return _preset != null;
        }

        // ──────────────── 中栏：聊天流 ────────────────

        void DrawMiddlePane()
        {
            EditorGUILayout.BeginVertical();
            {
                DrawSessionBar();

                _midScroll = EditorGUILayout.BeginScrollView(_midScroll);
                {
                    if (_session.turns.Count == 0)
                    {
                        EditorGUILayout.HelpBox(
                            _persona == null
                                ? "选一套人格资产就能开始。"
                                : "点上面的「开始试聊」，她会先开口说第一句。\n\n" +
                                  "· 点候选回复 = 顺着那个语气往下聊\n" +
                                  "· 底下的输入框 = 想说什么说什么（调提示词时最好用）\n" +
                                  "· 每一轮右上角「↺ 从这重来」= 丢掉后面的轮次重新分岔",
                            MessageType.Info);
                    }

                    for (int i = 0; i < _session.turns.Count; i++) DrawTurn(i);

                    if (_session.IsBusy)
                        EditorGUILayout.LabelField("　…她正在想…", EditorStyles.centeredGreyMiniLabel);

                    if (_interrupted && !_session.IsBusy)
                        EditorGUILayout.HelpBox(
                            "上一个请求被域重载打断了（改代码 / 进 Play Mode 会触发）。" +
                            "对话历史还在，直接继续发或「重跑本轮」即可。",
                            MessageType.Warning);

                    DrawPendingSummary();
                }
                EditorGUILayout.EndScrollView();

                DrawInputBar();
            }
            EditorGUILayout.EndVertical();
        }

        void DrawSessionBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                bool canStart = _persona != null && _draft.IsValid && !_session.IsBusy;
                using (new EditorGUI.DisabledScope(!canStart))
                {
                    if (GUILayout.Button(_session.turns.Count == 0 ? "开始试聊" : "重新开始",
                                         EditorStyles.toolbarButton, GUILayout.Width(70)))
                        StartSession();
                }

                using (new EditorGUI.DisabledScope(_session.turns.Count == 0 || _session.IsBusy))
                {
                    if (GUILayout.Button(new GUIContent("↻ 重跑本轮", "同样的输入再发一次，看输出的方差"),
                                         EditorStyles.toolbarButton, GUILayout.Width(80)))
                        _session.RerollLast();

                    if (GUILayout.Button("结束并总结", EditorStyles.toolbarButton, GUILayout.Width(80)))
                        EndSession();
                }

                GUILayout.FlexibleSpace();

                if (_session.turns.Count > 0 &&
                    GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    _session.Clear();
                    _pendingSummary = null;
                    _interrupted = false;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawTurn(int index)
        {
            var t = _session.turns[index];

            if (!string.IsNullOrEmpty(t.playerSaid))
            {
                EditorGUILayout.LabelField($"{_playerName}：{t.playerSaid}",
                                           EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    string who = _draft.IsValid ? _draft.Draft.DisplayName : "她";
                    GUILayout.Label($"{who}（第 {index + 1} 轮）", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(new GUIContent("prompt", "右栏看这一轮实际发出的 system prompt"),
                                         EditorStyles.miniButton, GUILayout.Width(52)))
                    {
                        _inspectTurn = index;
                        _foldPrompt = true;
                    }
                    using (new EditorGUI.DisabledScope(_session.IsBusy))
                    {
                        if (GUILayout.Button(new GUIContent("↺ 从这重来", "丢掉这一轮和后面的，重新分岔"),
                                             EditorStyles.miniButton, GUILayout.Width(72)))
                        {
                            string said = t.playerSaid;
                            if (_session.BranchFrom(index)) _freeInput = said ?? "";
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                var style = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 12 };
                if (t.degraded) style.normal.textColor = new Color(1f, 0.6f, 0.5f);
                EditorGUILayout.LabelField(t.reply, style);

                var meta = new List<string>();
                if (!string.IsNullOrEmpty(t.emotion)) meta.Add("表情 " + t.emotion);
                if (!string.IsNullOrEmpty(t.mark)) meta.Add("漫符 " + t.mark);
                if (t.affectionDelta != 0) meta.Add($"好感 {t.affectionDelta:+#;-#;0}");
                if (t.shouldEnd) meta.Add("AI 想收尾");
                meta.Add($"{t.seconds:0.0}s");
                meta.Add($"{t.promptTokens}+{t.outputTokens}" +
                         (t.thoughtsTokens > 0 ? $"+思考{t.thoughtsTokens}" : "") + " tok");
                meta.Add($"${t.costUsd:0.0000}");
                if (t.reply != null && _draft.IsValid && t.reply.Length > _draft.Draft.maxReplyChars)
                    meta.Add($"⚠ {t.reply.Length} 字（上限 {_draft.Draft.maxReplyChars}）");
                EditorGUILayout.LabelField(string.Join("　·　", meta), EditorStyles.miniLabel);

                if (!string.IsNullOrEmpty(t.failure))
                    EditorGUILayout.HelpBox($"{t.failure}：{t.errorMessage}\n（这一轮走了兜底台词）",
                                            MessageType.Error);

                // 候选回复：点了就顺着那个语气往下聊。不是最后一轮也能点——
                // 那等于「从这里换个走向重来」，后面的轮次会被丢掉
                for (int k = 0; k < t.optionTexts.Count; k++)
                {
                    string tone = k < t.optionTones.Count ? t.optionTones[k] : "";
                    bool isPicked = t.pickedIndex == k;

                    var c = GUI.color;
                    if (isPicked) GUI.color = new Color(0.6f, 0.9f, 1f);
                    using (new EditorGUI.DisabledScope(_session.IsBusy))
                    {
                        if (GUILayout.Button($"{k + 1}. [{tone}] {t.optionTexts[k]}",
                                             EditorStyles.miniButton))
                            _session.Pick(index, k);
                    }
                    GUI.color = c;
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        void DrawPendingSummary()
        {
            if (!string.IsNullOrEmpty(_summaryError))
                EditorGUILayout.HelpBox("总结失败：" + _summaryError, MessageType.Error);

            if (_pendingSummary == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.Label("这一场的总结（收下之后才会进记忆池）", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("摘要", _pendingSummary.summary,
                                           EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("话题", string.Join("、", _pendingSummary.topics),
                                           EditorStyles.wordWrappedMiniLabel);
                if (_pendingSummary.facts.Count > 0)
                    EditorGUILayout.LabelField("关键事实", string.Join("；", _pendingSummary.facts),
                                               EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(_pendingSummary.diary))
                {
                    EditorGUILayout.LabelField("日记（主角口吻）", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(_pendingSummary.diary,
                                               EditorStyles.wordWrappedLabel);
                }

                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("收下（存进记忆预设）")) AcceptSummary();
                    if (GUILayout.Button("丢掉", GUILayout.Width(60))) _pendingSummary = null;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        void DrawInputBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                using (new EditorGUI.DisabledScope(!_session.IsLive || _session.IsBusy))
                {
                    GUI.SetNextControlName("VNAiStudioInput");
                    _freeInput = EditorGUILayout.TextField(_freeInput);

                    if (GUILayout.Button(new GUIContent("发送", "Ctrl+Enter"),
                                         EditorStyles.toolbarButton, GUILayout.Width(48)))
                        SendFree();
                }
            }
            EditorGUILayout.EndHorizontal();

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return &&
                (e.control || e.command) && _session.IsLive && !_session.IsBusy)
            {
                SendFree();
                e.Use();
            }
        }

        void SendFree()
        {
            if (string.IsNullOrWhiteSpace(_freeInput)) return;
            _session.SendFreeform(_freeInput);
            _freeInput = "";
            GUI.FocusControl(null);
        }

        // ──────────────── 右栏：prompt / JSON / 诊断 ────────────────

        void DrawRightPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_rightW));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            {
                if (_persona == null || !_draft.IsValid)
                {
                    EditorGUILayout.LabelField("（选了人格之后这里会显示 system prompt）",
                                               EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    DrawPromptPreview();
                    DrawRawJson();
                    DrawSchema();
                    DrawDiagnostics();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawPromptPreview()
        {
            bool inspecting = _inspectTurn >= 0 && _inspectTurn < _session.turns.Count;
            string prompt = inspecting
                ? _session.turns[_inspectTurn].systemPrompt
                : BuildPreviewPrompt();

            EditorGUILayout.BeginHorizontal();
            _foldPrompt = EditorGUILayout.Foldout(
                _foldPrompt,
                inspecting ? $"system prompt（第 {_inspectTurn + 1} 轮实发）" : "system prompt（实时预览）",
                true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{(prompt != null ? prompt.Length : 0)} 字", EditorStyles.miniLabel);
            if (inspecting && GUILayout.Button("回到预览", EditorStyles.miniButton, GUILayout.Width(60)))
                _inspectTurn = -1;
            if (GUILayout.Button("复制", EditorStyles.miniButton, GUILayout.Width(40)))
                EditorGUIUtility.systemCopyBuffer = prompt ?? "";
            EditorGUILayout.EndHorizontal();

            if (!_foldPrompt) return;

            // 只读文本框：不用 TextArea 的话长文本没法选中复制其中一段
            EditorGUILayout.SelectableLabel(prompt ?? "",
                EditorStyles.textArea,
                GUILayout.ExpandHeight(false),
                GUILayout.Height(EditorStyles.textArea.CalcHeight(
                    new GUIContent(prompt ?? ""), _rightW - 24f)));
        }

        /// <summary>
        /// 不发请求就能看到的那一份：把当前左栏的参数按真实规则拼一遍。
        /// 改一个字这里立刻变——调 boundaries / speechStyle 的主力就是它。
        /// </summary>
        string BuildPreviewPrompt()
        {
            if (!_draft.IsValid) return "";
            var p = _draft.Draft;
            return VNAiConversation.BuildSystemInstruction(
                p, BuildContext(_session.turns.Count),
                p.ResolveEmotions(), p.ResolveMarks(), p.ResolveTones(_optionOverride));
        }

        void DrawRawJson()
        {
            var t = LastOrInspected();
            _foldJson = EditorGUILayout.Foldout(_foldJson, "本轮原始 JSON", true,
                                                EditorStyles.foldoutHeader);
            if (!_foldJson) return;
            EditorGUILayout.SelectableLabel(t != null ? t.rawJson ?? "（无）" : "（还没有）",
                                            EditorStyles.textArea, GUILayout.Height(120));
        }

        void DrawSchema()
        {
            _foldSchema = EditorGUILayout.Foldout(_foldSchema, "responseSchema", true,
                                                  EditorStyles.foldoutHeader);
            if (!_foldSchema || !_draft.IsValid) return;

            var p = _draft.Draft;
            string schema = VNAiConversation.BuildSchema(
                p.ResolveEmotions(), p.ResolveMarks(), p.ResolveTones(_optionOverride));
            EditorGUILayout.SelectableLabel(schema, EditorStyles.textArea, GUILayout.Height(100));
            EditorGUILayout.LabelField(
                $"表情 {p.ResolveEmotions().Count} 种 · 漫符 {p.ResolveMarks().Count} 种 · " +
                $"语气 {p.ResolveTones(_optionOverride).Count} 档",
                EditorStyles.miniLabel);
        }

        void DrawDiagnostics()
        {
            _foldDiag = EditorGUILayout.Foldout(_foldDiag, "诊断", true, EditorStyles.foldoutHeader);
            if (!_foldDiag) return;

            // key 要查**草稿当前选的那家**——试聊台随时可以切供应商
            var provider = _draft.IsValid ? _draft.Draft.ResolveProvider() : VNAiProviders.GlobalDefault;
            if (VNAiKey.TryGet(provider, out _, out string source))
                EditorGUILayout.LabelField("key", "✔ " + source, EditorStyles.miniLabel);
            else
                EditorGUILayout.HelpBox(
                    $"没找到 {VNAiProviders.DisplayName(provider)} 的 API key。" +
                    $"环境变量 {VNAiProviders.EnvVarFor(provider)}，" +
                    $"或项目根放 {VNAiProviders.KeyFileFor(provider)}。", MessageType.Error);

            if (_draft.IsValid)
            {
                EditorGUILayout.LabelField("供应商", VNAiProviders.DisplayName(provider),
                                           EditorStyles.miniLabel);
                EditorGUILayout.LabelField("模型", _draft.Draft.ResolveModel(), EditorStyles.miniLabel);
                if (!VNAiProviders.SupportsResponseSchema(provider))
                    EditorGUILayout.HelpBox(
                        "这家不支持硬 schema（responseSchema），格式改用提示词约束 —— " +
                        "右栏 system prompt 末尾多出的「输出格式」段就是它。\n" +
                        "偶发的格式错误由解析层降级兜底，右栏「原始 JSON」能看到实际返回。",
                        MessageType.Info);
                EditorGUILayout.LabelField("记忆",
                    _injectMemory && _preset != null
                        ? $"注入 {VNAiStudioMemory.For(_preset, CharacterId(), _draft.Draft.memoryCapacity).Count} 条"
                        : "不注入",
                    EditorStyles.miniLabel);
            }

            var t = LastOrInspected();
            if (t != null && !string.IsNullOrEmpty(t.failure))
                EditorGUILayout.HelpBox($"最近一次失败：{t.failure}\n{t.errorMessage}",
                                        MessageType.Warning);
        }

        VNAiStudioTurn LastOrInspected()
        {
            if (_inspectTurn >= 0 && _inspectTurn < _session.turns.Count)
                return _session.turns[_inspectTurn];
            return _session.turns.Count > 0 ? _session.turns[_session.turns.Count - 1] : null;
        }

        // ──────────────── 会话动作 ────────────────

        void StartSession()
        {
            if (!_draft.IsValid) return;

            var errors = _draft.Draft.Validate();
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("人格配置有问题",
                    "先修好这些再试聊：\n\n• " + string.Join("\n• ", errors), "好");
                return;
            }
            var provider = _draft.Draft.ResolveProvider();
            if (!VNAiKey.TryGet(provider, out _, out _))
            {
                EditorUtility.DisplayDialog("没有 API key", VNAiKey.MissingKeyMessage(provider), "好");
                return;
            }

            _pendingSummary = null;
            _summaryError = null;
            _interrupted = false;
            _inspectTurn = -1;
            _session.Begin(_draft.Draft, BuildContext);
        }

        /// <summary>
        /// 收场。**要做总结时，日志等总结回来再写**——总结是一次独立请求（约 $0.001），
        /// 先写日志就会漏掉它，日志里的成本便少算一整次。
        /// 不做总结时没什么可等的，立刻写。
        /// </summary>
        void EndSession()
        {
            if (_session.turns.Count == 0) return;

            if (_writeMemory)
            {
                _summaryError = null;
                _session.RequestSummary(BuildContext(_session.turns.Count), _playerName,
                    (summary, err) =>
                    {
                        _pendingSummary = summary;
                        _summaryError = err;
                        if (_writeLog) ExportLog();   // 此时 LastSummaryResult 已就位
                        Repaint();
                    });
            }
            else if (_writeLog)
            {
                ExportLog();
            }
        }

        void ExportLog()
        {
            string path = VNAiStudioLog.Export(_session, _draft.Draft, DescribeKwargs(),
                                               _maxTurns, "（试聊）",
                                               _writeMemory ? _session.LastSummaryResult : null);
            if (!string.IsNullOrEmpty(path)) Debug.Log($"[VNAiStudio] 试聊日志：{path}");
        }

        void AcceptSummary()
        {
            if (_pendingSummary == null || !EnsurePreset()) return;

            _preset.entries.Add(new VNAiMemoryEntry
            {
                personaId = _persona != null ? _persona.id : "",
                characterId = CharacterId(),
                place = _place,
                savedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                summary = _pendingSummary.summary,
                topics = _pendingSummary.topics,
                facts = _pendingSummary.facts,
                affectionDelta = _session.AffectionTotal,
                turns = _session.turns.Count,
            });
            VNAiStudioMemory.Save(_preset);
            _pendingSummary = null;
        }

        // ──────────────── 上下文组装 ────────────────

        /// <summary>
        /// 每一轮的 VNAiContext。与 VNAiTalkModule 的组装规则保持一致：
        /// topic 只在第 1 轮注入、turnsLeft 逐轮递减、好感翻成人话。
        /// 差别只有记忆——那边看人格的 enableMemory，这边看窗口开关
        /// （就是为了能一键对比「有记忆 vs 没记忆」）。
        /// </summary>
        VNAiContext BuildContext(int turnIndex)
        {
            var ctx = new VNAiContext
            {
                playerName = _playerName,
                place = _place,
                affectionText = VNAiTalkModule.BuildAffectionText(_statName, _affection),
                topic = turnIndex == 0 ? _topic : null,
                turnsLeft = Mathf.Max(0, _maxTurns - turnIndex),
            };

            if (_injectMemory && _preset != null && _draft.IsValid)
            {
                int cap = _draft.Draft.memoryCapacity;
                ctx.memory = VNAiStudioMemory.BuildContext(_preset, CharacterId(), cap);
                ctx.pastTopics = VNAiStudioMemory.TopicsOf(_preset, CharacterId(), cap);
            }
            return ctx;
        }

        string CharacterId() =>
            _draft.IsValid && _draft.Draft.character != null ? _draft.Draft.character.id : "";

        /// <summary>写进日志文件头的那一行情境参数（对应剧本的 kwargs）。</summary>
        string DescribeKwargs()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_topic)) parts.Add("topic:" + _topic);
            if (!string.IsNullOrWhiteSpace(_place)) parts.Add("place:" + _place);
            if (!string.IsNullOrWhiteSpace(_playerName)) parts.Add("me:" + _playerName);
            if (!string.IsNullOrWhiteSpace(_statName)) parts.Add($"{_statName}:{_affection}");
            parts.Add("turns:" + _maxTurns);
            if (_optionOverride > 0) parts.Add("options:" + _optionOverride);
            parts.Add(_injectMemory && _preset != null ? "记忆:" + _preset.name : "记忆:无");
            return string.Join(" ", parts);
        }

    }
}
