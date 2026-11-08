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
        // Edit 页签
        // ------------------------------------------------------------------

        void DrawEditTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                    _list.index < 0 || _list.index >= _doc.rows.Count))
                {
                    if (GUILayout.Button(new GUIContent("Duplicate",
                            "复制选中行（支持 Shift/Ctrl 多选，整块插到选区之后）"),
                            GUILayout.Width(72f)))
                    {
                        var selected = SelectedRowIndices();
                        if (selected.Count > 0)
                        {
                            MarkStructural();
                            int insertAt = selected[selected.Count - 1] + 1;
                            for (int i = 0; i < selected.Count; i++)
                                _doc.rows.Insert(insertAt + i,
                                    _doc.rows[selected[i]].Clone());
                            _list.ClearSelection();
                            for (int i = 0; i < selected.Count; i++)
                                _list.Select(insertAt + i, true);
                            _list.index = insertAt + selected.Count - 1;
                            Bump();
                        }
                    }
                }
                // Play Mode 中不禁用：走热重播，原地生效不退出 Play Mode。
                // 只有正在切换 Play Mode 的那一小段时间按钮才灰掉。
                bool switchingPlaymode =
                    EditorApplication.isPlayingOrWillChangePlaymode &&
                    !EditorApplication.isPlaying;
                using (new EditorGUI.DisabledScope(
                    _list.index < 0 || _list.index >= _doc.rows.Count || switchingPlaymode))
                {
                    if (GUILayout.Button(new GUIContent(
                            EditorApplication.isPlaying ? "▶ 播放选中行（热）" : "▶ 从选中行播放",
                            EditorApplication.isPlaying
                                ? "热重播：直接用当前（未保存也算）文本从选中行重跑，不退出 Play Mode。Ctrl+Enter"
                                : "先自动保存，再进入 Play Mode 并从当前行或下一条有效命令开始播放。Ctrl+Enter"),
                            GUILayout.Width(126f)))
                        PlayFromSelectedRow();
                }
                bool previousChanged = GUI.changed;
                _rebuildStateBeforePlay = GUILayout.Toggle(_rebuildStateBeforePlay,
                    new GUIContent("重建前置状态", "播放前静默恢复目标行之前的舞台状态"),
                    GUILayout.Width(96f));
                bool follow = GUILayout.Toggle(_followPlayback,
                    new GUIContent("跟随播放",
                        "Play Mode 中高亮正在执行的那一行，并在它滚出可视区时自动滚过去"),
                    GUILayout.Width(72f));
                if (follow != _followPlayback)
                {
                    _followPlayback = follow;
                    EditorPrefs.SetBool(FollowPlaybackPref, follow);
                }
                GUI.changed = previousChanged;

                DrawPlaybackControls();

                using (new EditorGUI.DisabledScope(
                    _list.index < 0 || _list.index >= _doc.rows.Count))
                {
                    if (GUILayout.Button(new GUIContent("禁用/恢复",
                            "把选中行注释掉（文本里写成 \"#! 原来那行\"）临时失效，再按一次恢复。\n" +
                            "禁用后参数格照样能改，不像手写 # 注释会退化成纯文本。Ctrl+/"),
                            GUILayout.Width(72f)))
                        ToggleDisableSelection();
                }

                GUILayout.FlexibleSpace();

                // 搜索过滤：不匹配的行折成零高度（同「隐注释/空行」那套）
                GUI.SetNextControlName(SearchFieldName);
                string search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField,
                    GUILayout.Width(190f));
                if (search != _search)
                {
                    _search = search;
                    MoveSelectionOffHiddenRow();
                }
                if (_focusSearchPending && Event.current.type == EventType.Repaint)
                {
                    _focusSearchPending = false;
                    EditorGUI.FocusTextInControl(SearchFieldName);
                }
                if (!string.IsNullOrEmpty(_search))
                {
                    if (GUILayout.Button("✕", EditorStyles.miniLabel, GUILayout.Width(16f)))
                    {
                        _search = "";
                        GUIUtility.keyboardControl = 0;
                        MoveSelectionOffHiddenRow();
                    }
                    GUILayout.Label($"{CountVisibleRows()}/{_doc.rows.Count} 行",
                        EditorStyles.miniLabel);
                }
                else
                {
                    GUILayout.Label($"{_doc.rows.Count} rows", EditorStyles.miniLabel);
                }
            }

            if (_stagePreview) RebuildStageStatesIfNeeded();

            if (_pendingScrollY >= 0f)
            {
                _scroll.y = _pendingScrollY;
                _pendingScrollY = -1f;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _list.DoLayoutList();
            EditorGUILayout.EndScrollView();

            // 底部说明：默认折叠成一行，点标题展开（正文太长，展开会吃掉半个列表）
            using (new EditorGUILayout.HorizontalScope())
            {
                bool showHelp = GUILayout.Toggle(
                    _showHelp,
                    "使用说明 / 快捷键一览",   // 三角箭头由 foldout 样式自己画
                    EditorStyles.foldout,
                    GUILayout.Width(170f));
                if (showHelp != _showHelp)
                {
                    _showHelp = showHelp;
                    EditorPrefs.SetBool(ShowHelpPref, showHelp);
                }
                if (!_showHelp)
                {
                    GUILayout.Label(
                        "F5 播放 · Enter 插台词行 · Ctrl+E 命令面板 · Ctrl+F 搜索 · Ctrl+/ 禁用行",
                        EditorStyles.miniLabel);
                }
                GUILayout.FlexibleSpace();
            }

            if (_showHelp)
            {
                EditorGUILayout.HelpBox(
                    "热重载调试：F5 / Ctrl+Enter = 从选中行播放（Play Mode 中是原地热重播，不退出播放；" +
                    "编辑期则自动保存后进 Play Mode），F6 / Ctrl+Shift+Enter = 重播上次那一行，" +
                    "Ctrl+S = 保存，F8 = 暂停/继续，F10 = 单步一条命令。" +
                    "F5/F6/F8/F10/Ctrl+S 可在 Edit → Shortcuts → VN/Scenario Editor 里改键位。\n" +
                    "Enter = 在选中行下方插入台词行（自动聚焦，可直接打字），Shift+Enter = 插在上方；" +
                    "在文本框里打字时第一下 Enter 只是结束编辑，再按一次才插入。\n" +
                    "打字找命令：Ctrl+E = 命令面板（选命令 → 逐个问必填参数 → 可选参数菜单 → " +
                    "Enter 插入，Shift+Enter 插到上方，Tab 跳过参数，Esc 取消）；" +
                    "右键点行首的命令按钮 = 打字换这一行的命令（左键仍是原来的分类菜单）；" +
                    "底部 [+] 与各参数下拉也都能打字筛选。\n" +
                    "Ctrl+/ = 把选中行临时禁用/恢复（文本里写成 #! 那行，运行时当注释跳过，" +
                    "但参数格照样能改）；Ctrl+F = 跳到搜索框，只留匹配的行，" +
                    "支持 cmd:say（只要台词行）/ cmd:bg / who:星野（只要她说的话），" +
                    "空格分开的多个词是「都要满足」，清空搜索框即全部显示。\n" +
                    "Shift+click = select range, Ctrl+click = toggle select; " +
                    "drag moves all selected rows, [-] / Duplicate act on the whole selection. " +
                    "Drag handle to reorder. [+] adds after selection. \"@\" = async (do not wait). " +
                    "Popups list registered ids; pick \"custom…\" to type a free value. " +
                    "camseq waypoint lines are kept as text in this batch " +
                    "(use Tools → VN Effects → 镜头编排 Camera Sequence Editor and paste).",
                    MessageType.Info);
            }

            if (_pendingPalette && Event.current.type == EventType.Repaint)
            {
                _pendingPalette = false;
                ShowCommandPalette();
            }
        }

        /// <summary>
        /// 播放控制条（只在 Play Mode 有意义，编辑期整组灰掉）。
        /// 暂停是「命令级」的：卡在两条命令之间，已经跑起来的补间/打字机不会冻结——
        /// 想要真·定格画面请用 Unity 自带的暂停按钮。
        /// </summary>
        void DrawPlaybackControls()
        {
            VNScriptRunner runner = ResolveRunner();
            bool live = EditorApplication.isPlaying && runner != null;

            GUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(!live))
            {
                bool paused = live && runner.IsDebugPaused;
                if (GUILayout.Button(new GUIContent(paused ? "▶" : "❚❚",
                        paused ? "继续（F8）" : "暂停在下一条命令之前（F8）"),
                        GUILayout.Width(30f)))
                    runner.SetDebugPaused(!paused);

                if (GUILayout.Button(new GUIContent("⏭",
                        "单步：放行一条命令后重新暂停（F10）"), GUILayout.Width(28f)))
                    runner.RequestDebugStep();

                using (new EditorGUI.DisabledScope(
                    !live || (_playingLine <= 0 && _lastPlayedLine <= 0)))
                {
                    if (GUILayout.Button(new GUIContent("⟳",
                            "重播当前这一行（Ctrl+Shift+Enter 是重播上次播放的那一行）"),
                            GUILayout.Width(28f)))
                        ReplayPlayingLine();
                    if (GUILayout.Button(new GUIContent("⏮",
                            "退回上一条命令并从它开始播"), GUILayout.Width(28f)))
                        PlayPreviousCommand();
                }

                if (live && _playingLine > 0)
                    GUILayout.Label($"L{_playingLine}" + (paused ? " 已暂停" : ""),
                        EditorStyles.miniLabel, GUILayout.Width(74f));
            }
        }

        void PlayFromSelectedRow()
        {
            if (_list.index < 0 || _list.index >= _doc.rows.Count) return;
            // 禁用行在文本里就是注释，Runner 会从它后面的第一条有效命令开始——
            // 说一声，免得以为播放位置串了
            if (_doc.rows[_list.index].disabled)
                Debug.LogWarning("[VNScript] 选中的是被禁用的行（#!），播放会从它之后的第一条有效命令开始");
            PlayFromSourceLine(SourceLineForRow(_list.index));
        }

        /// <summary>「重播上次那一行」：没播过就退回选中行</summary>
        void ReplayLastLine()
        {
            if (_lastPlayedLine > 0) PlayFromSourceLine(_lastPlayedLine);
            else PlayFromSelectedRow();
        }

        /// <summary>正在播的那一行重来一遍（跟随高亮指到哪就重播哪）</summary>
        void ReplayPlayingLine()
        {
            PlayFromSourceLine(_playingLine > 0 ? _playingLine : _lastPlayedLine);
        }

        /// <summary>回退到当前行之前的那一条命令并从它开始播</summary>
        void PlayPreviousCommand()
        {
            int from = _playingLine > 0 ? _playingLine : _lastPlayedLine;
            if (from <= 0) return;
            var commands = VNScriptParser.Parse(_doc.GenerateText());
            int previous = -1;
            foreach (var command in commands)
            {
                if (command.line >= from) break;
                previous = command.line;
            }
            if (previous < 0)
            {
                ShowNotification(new GUIContent("已经是第一条命令"));
                return;
            }
            PlayFromSourceLine(previous);
        }

        /// <summary>
        /// 统一播放入口：校验 → 静默自动保存 → Play Mode 中热重播 / 否则冷启动 Bridge。
        /// Play Mode 中走热路径时完全不触发域重载，改一行到看到效果约等于一次 Repaint。
        /// </summary>
        void PlayFromSourceLine(int sourceLine)
        {
            if (sourceLine <= 0) return;

            foreach (var issue in _issues)
            {
                if (!issue.isError) continue;
                _tab = Tab.Issues;
                EditorUtility.DisplayDialog("无法开始调试",
                    "剧本仍有错误，请先在 Issues 页修正。", "确定");
                return;
            }

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

            AutoSaveBeforePlay();
            _lastPlayedLine = sourceLine;

            if (EditorApplication.isPlaying)
            {
                HotReplay(source, sourceLine);
                return;
            }
            VNPlayFromLineBridge.Request(source, sourceLine, _rebuildStateBeforePlay,
                ProjectRelativePath());
        }

        /// <summary>
        /// 播放前静默把改动写盘，省得「进 Play Mode 前忘了存」。
        /// 未命名（没有路径）不弹保存框，直接拿内存文本播；
        /// 磁盘已被别处改过（_externalChanged）也不写，避免静默覆盖掉别人的改动。
        /// </summary>
        void AutoSaveBeforePlay()
        {
            if (!_dirty || string.IsNullOrEmpty(_path) || _externalChanged) return;
            SaveFile(false);
        }

        /// <summary>Play Mode 中原地重播：不退出 Play Mode，不触发域重载</summary>
        void HotReplay(string source, int sourceLine)
        {
            VNScriptRunner runner = ResolveRunner();
            if (runner == null || !runner.IsInitialized)
            {
                Debug.LogError("[VNScript] 热重播失败：当前场景里找不到已初始化的 VNScriptRunner");
                ShowNotification(new GUIContent("场景里没有 VNScriptRunner"));
                return;
            }
            // 让 Runner 知道现在调试的是哪个剧本：翻译查表与 chapter/跨文件 jump 都按它算
            runner.SetDebugScript(OpenFileAsset());
            runner.PlayFromSourceLine(source, sourceLine, _rebuildStateBeforePlay);
            Repaint();
        }

        VNScriptRunner ResolveRunner()
        {
            if (!EditorApplication.isPlaying) return _runnerCache = null;
            if (_runnerCache == null)
                _runnerCache = Object.FindFirstObjectByType<VNScriptRunner>();
            return _runnerCache;
        }

        /// <summary>Runner 正在播的文件 == 本窗口打开的文件？（跨文件 jump 后行号就不通用了）</summary>
        bool IsRunnerOnOpenFile(VNScriptRunner runner)
        {
            if (string.IsNullOrEmpty(_path)) return true; // 未命名文档：只能相信是它
            string running = runner.CurrentScriptName;
            if (string.IsNullOrEmpty(running)) return true;
            return VNStoryAddress.NormalizeFile(running) ==
                   VNStoryAddress.NormalizeFile(Path.GetFileName(_path));
        }

        /// <summary>当前文件的 "Assets/..." 相对路径（不在工程内时返回 null）</summary>
        string ProjectRelativePath()
        {
            if (string.IsNullOrEmpty(_path)) return null;
            string assets = Application.dataPath.Replace('\\', '/');
            string norm = _path.Replace('\\', '/');
            return norm.StartsWith(assets) ? "Assets" + norm.Substring(assets.Length) : null;
        }

        TextAsset OpenFileAsset()
        {
            string relative = ProjectRelativePath();
            return string.IsNullOrEmpty(relative)
                ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(relative);
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

        /// <summary>
        /// SourceLineForRow 的逆运算：物理行 → UI 行号（播放跟随高亮用）。
        /// choice 的选项行 / camseq 的路径点行都算进它们所属的那一行。
        /// </summary>
        int RowForSourceLine(int sourceLine)
        {
            int line = 1;
            for (int i = 0; i < _doc.rows.Count; i++)
            {
                VNRow row = _doc.rows[i];
                int span = 1;
                if (row.options != null) span += row.options.Count;
                if (row.camLines != null) span += row.camLines.Count;
                if (sourceLine < line + span) return i;
                line += span;
            }
            return -1;
        }

        float RowHeight(VNRow r)
        {
            if (IsRowCollapsed(r)) return 0f;
            int lines = 1;
            if (EventParamLine(r)) lines += 1;   // event 的模块专属参数独占一行
            if (r.options != null) lines += r.options.Count;
            if (r.camLines != null) lines += r.camLines.Count;
            return lines * LineH2 + 6f;
        }

        /// <summary>
        /// 这一行是不是要多画一行「模块专属参数」——event 且模块 id 认得出来。
        /// 塞进同一行的话，photo 有 11 个参数，每格只剩几十像素，什么都看不清。
        /// </summary>
        static bool EventParamLine(VNRow r) =>
            r.kind == VNRowKind.Command && r.keyword == "event" &&
            VNScenarioSchema.HasEventVariant(r.Get("module"));

        /// <summary>
        /// 「隐注释/空行」开着时这一行是否折成零高度。
        ///
        /// 只认空行与 `#` 注释——VNRowKind.Raw 还兜着两种语法残留：
        /// 前面没有 choice 的孤儿 `*` 选项行、前面没有 camseq 的孤儿 `>` 路径点行。
        /// 那两种一旦藏起来就再也找不回来了（Issues 面板定位过去也是一片空白），
        /// 所以必须留在列表里显形。
        /// </summary>
        bool IsHiddenRow(VNRow r)
        {
            if (!_hideRawRows || r.kind != VNRowKind.Raw) return false;
            string text = r.raw == null ? "" : r.raw.TrimStart();
            return text.Length == 0 || text.StartsWith("#");
        }

        /// <summary>
        /// 这一行现在是不是折成了零高度——「隐注释/空行」或搜索过滤任一命中即是。
        ///
        /// 【为什么不做过滤视图】列表绑的是 _doc.rows 本体，高度归零就够了：
        /// 索引不变，行号换算 / 多选 / 拖动排序 / 删除全部零改动。
        /// 换成过滤后的子列表，「拖到被过滤掉的行之间算什么」这种问题会没完没了。
        /// </summary>
        bool IsRowCollapsed(VNRow r) => IsHiddenRow(r) || IsFilteredOut(r);

        bool IsFilteredOut(VNRow r) =>
            !string.IsNullOrEmpty(_search) && !RowMatchesSearch(r, _search);

        /// <summary>
        /// 搜索匹配：空格分开的多个词是 AND（都命中才留下），大小写无关的子串包含。
        /// 词可以带前缀限定范围：cmd:（命令名，台词行算 say）、say:（台词正文）、who:（说话者）。
        /// 不带前缀就什么都比：命令名、命令中文名、台词、说话者、所有参数值、
        /// choice 选项文本、camseq 路径点原文。
        /// </summary>
        public static bool RowMatchesSearch(VNRow r, string query)
        {
            foreach (var term in query.Split(new[] { ' ', '\t' },
                         System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (!RowMatchesTerm(r, term)) return false;
            }
            return true;
        }

        static bool RowMatchesTerm(VNRow r, string term)
        {
            string keyword = r.kind == VNRowKind.Say ? "say"
                : r.kind == VNRowKind.Command ? r.keyword : "";

            int colon = term.IndexOf(':');
            if (colon > 0)
            {
                string scope = term.Substring(0, colon).ToLowerInvariant();
                string value = term.Substring(colon + 1);
                if (value.Length == 0) return true;    // 只打了 "cmd:"，还没输值
                switch (scope)
                {
                    case "cmd":
                    case "命令":
                        return Contains(keyword, value);
                    case "say":
                    case "台词":
                        return r.kind == VNRowKind.Say && Contains(r.text, value);
                    case "who":
                    case "谁":
                        return r.kind == VNRowKind.Say && Contains(r.speaker, value);
                }
                // 不是已知前缀（比如剧本里本来就有的 "transition:Eyelid"）：整段当普通词比
            }

            if (Contains(keyword, term)) return true;
            if (CommandTranslations.TryGetValue(keyword, out string zh) && Contains(zh, term))
                return true;
            if (Contains(r.raw, term) || Contains(r.text, term) ||
                Contains(r.speaker, term) || Contains(r.expression, term)) return true;
            foreach (var kv in r.values)
                if (Contains(kv.Value, term) || Contains(kv.Key, term)) return true;
            foreach (var extra in r.extraTokens)
                if (Contains(extra, term)) return true;
            if (r.options != null)
                foreach (var o in r.options)
                    if (Contains(o.text, term) || Contains(o.condition, term) ||
                        Contains(o.costOp, term) || Contains(o.flagOp, term) ||
                        Contains(o.jump, term)) return true;
            if (r.camLines != null)
                foreach (var l in r.camLines)
                    if (Contains(l, term)) return true;
            return false;
        }

        static bool Contains(string text, string term) =>
            !string.IsNullOrEmpty(text) &&
            text.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0;

        int CountVisibleRows()
        {
            int n = 0;
            foreach (var r in _doc.rows)
                if (!IsRowCollapsed(r)) n++;
            return n;
        }

        /// <summary>
        /// 选中行「临时禁用 / 恢复」（文本里加/去掉 "#!" 前缀）。
        /// 语义照抄 VS：选区里只要还有没禁用的行，就整块禁用；全禁用了才整块恢复。
        /// Raw 行（注释/空行）跳过——它本来就不执行，加个标记只会让文本更乱。
        /// </summary>
        void ToggleDisableSelection()
        {
            var selected = SelectedRowIndices();
            if (selected.Count == 0) return;

            bool anyEnabled = false;
            foreach (int i in selected)
            {
                var r = _doc.rows[i];
                if (r.kind != VNRowKind.Raw && !r.disabled) { anyEnabled = true; break; }
            }

            MarkStructural();
            foreach (int i in selected)
            {
                var r = _doc.rows[i];
                if (r.kind == VNRowKind.Raw) continue;
                r.disabled = anyEnabled;
            }
            Bump();
            Repaint();
        }

        Rect SubLine(Rect rect, int line) => new Rect(
            rect.x + 14f, rect.y + 3f + line * LineH2, rect.width - 16f, LineH2 - 3f);

        void DrawRow(Rect rect, int index, bool active, bool focused)
        {
            if (index < 0 || index >= _doc.rows.Count) return;
            var r = _doc.rows[index];
            if (IsRowCollapsed(r)) return;   // 零高度，什么都别画

            // 播放跟随：正在执行的这一行铺一层淡蓝底 + 左侧竖条
            if (index == _playingRow)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y - 1f, rect.width, rect.height + 2f),
                    new Color(0.30f, 0.62f, 1f, 0.16f));
                EditorGUI.DrawRect(new Rect(rect.x - 3f, rect.y - 1f, 3f, rect.height + 2f),
                    new Color(0.35f, 0.70f, 1f, 0.95f));
            }

            // 校验状态圆点
            if (_rowHasError.TryGetValue(index, out bool isErr))
                EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y + 8f, 7f, 7f),
                    isErr ? new Color(0.95f, 0.3f, 0.25f) : new Color(0.95f, 0.75f, 0.2f));

            // 舞台一览小格：当前背景缩略图 + 在场角色站位色块，其余内容整体右移
            if (_stagePreview)
            {
                if (index < _stageStates.Count)
                    DrawStageCell(new Rect(rect.x + 11f, rect.y + 4f,
                        StageCellW - 14f, LineH2 - 6f), _stageStates[index]);
                rect = new Rect(rect.x + StageCellW, rect.y,
                    rect.width - StageCellW, rect.height);
            }

            var line0 = SubLine(rect, 0);

            // 临时禁用的行：整行压暗 + 左侧一条橙杠。字段照样能点能改——
            // 这正是 "#!" 相对手写 # 注释的意义（后者会退化成 Raw 纯文本）。
            var previousColor = GUI.color;
            if (r.disabled)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y - 1f, rect.width, rect.height + 2f),
                    new Color(0.55f, 0.35f, 0.12f, 0.13f));
                EditorGUI.DrawRect(new Rect(rect.x - 3f, rect.y - 1f, 3f, rect.height + 2f),
                    new Color(0.90f, 0.55f, 0.15f, 0.95f));
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
            }

            switch (r.kind)
            {
                case VNRowKind.Raw: DrawRawRow(line0, r); break;
                case VNRowKind.Say: DrawSayRow(line0, r, index); break;
                case VNRowKind.Command: DrawCommandRow(rect, line0, r, index); break;
            }

            GUI.color = previousColor;
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

        void DrawSayRow(Rect rect, VNRow r, int index)
        {
            float x = rect.x;
            var typeRect = new Rect(x, rect.y, 128f, rect.height);
            // 右键 = 打字搜索。按钮本身照画不误：IMGUI 的控件 id 是按调用顺序分配的，
            // 少画一个控件会让同一帧后面的控件全部错位
            bool searchType = ConsumeRightClick(typeRect);
            if (CategoryPopupButton(typeRect, "say（对白）", "Dialogue"))
                ShowRowTypeMenu(typeRect, r);
            if (searchType) ShowRowTypeSearch(typeRect, r);
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
            bool focusMe = _pendingFocusRow == index;
            if (focusMe) GUI.SetNextControlName(SayFocusControl);
            r.text = EditorGUI.TextField(
                new Rect(x, rect.y, rect.xMax - x - asyncW - 2f, rect.height), r.text);
            // 控件画完后再抢焦点（IMGUI 只认已经存在的控件名）
            if (focusMe && Event.current.type == EventType.Repaint)
            {
                _pendingFocusRow = -1;
                EditorGUI.FocusTextInControl(SayFocusControl);
                Repaint();
            }
            r.isAsync = GUI.Toggle(new Rect(rect.xMax - asyncW, rect.y, asyncW, rect.height),
                r.isAsync, "@", EditorStyles.miniButton);
        }

        void DrawCommandRow(Rect fullRect, Rect line0, VNRow r, int index)
        {
            float x = line0.x;

            // 关键字下拉
            var keywordRect = new Rect(x, line0.y, 128f, line0.height);
            bool searchKeyword = ConsumeRightClick(keywordRect);   // 右键 = 打字搜索
            if (CategoryPopupButton(keywordRect, CommandDisplayName(r.keyword),
                    CommandCategory(r.keyword)))
                ShowRowTypeMenu(keywordRect, r);
            if (searchKeyword) ShowRowTypeSearch(keywordRect, r);
            x += 132f;

            // event 的参数集按模块 id 变（badminton 有 vs:/target:，quiz 有 id:/count:…）
            var def = r.keyword == "event"
                ? VNScenarioSchema.Find("event", r.Get("module"))
                : VNScenarioSchema.Find(r.keyword);
            bool paramLine = EventParamLine(r);
            int baseCount = paramLine ? VNScenarioSchema.EventBaseParamCount : int.MaxValue;

            float asyncW = 26f;
            // 行尾按钮区：choice 的 "+ option" / camseq 的 "🎬 预设▾ + wp"
            float tailW = 0f;
            if (r.options != null) tailW = 78f;
            else if (r.camLines != null) tailW = CamseqHeaderButtonsWidth;
            float avail = line0.xMax - x - asyncW - tailW - 4f;

            if (def != null && def.parameters.Length > 0)
            {
                // 第一行只放通用参数（模块 id + tutorial），模块专属的挪到第二行
                DrawParams(new Rect(x, line0.y, avail, line0.height), r, def, 0, baseCount);
                if (paramLine)
                    DrawParams(SubLine(fullRect, 1), r, def, baseCount, int.MaxValue);
            }
            else if (def != null)
                GUI.Label(new Rect(x, line0.y, avail, line0.height),
                    def.hint, EditorStyles.centeredGreyMiniLabel);

            r.isAsync = GUI.Toggle(new Rect(line0.xMax - asyncW, line0.y, asyncW, line0.height),
                r.isAsync, "@", EditorStyles.miniButton);

            // ---- choice 选项行 ----
            if (r.options != null)
            {
                int firstOptionLine = paramLine ? 2 : 1;   // 模块参数行占掉了第 1 行
                for (int i = 0; i < r.options.Count; i++)
                    DrawChoiceOption(SubLine(fullRect, firstOptionLine + i), r, i);
                // header 右侧的 + option
                var addRect = new Rect(line0.xMax - asyncW - 78f, line0.y, 74f, line0.height);
                if (GUI.Button(addRect, "+ option", EditorStyles.miniButton))
                {
                    MarkStructural();
                    r.options.Add(new VNChoiceOptionRow());
                    Bump();
                }
            }

            // ---- camseq 路径点行 ----
            if (r.camLines != null)
            {
                for (int i = 0; i < r.camLines.Count; i++)
                    if (DrawCamWaypointRow(SubLine(fullRect, 1 + i), r, i)) break;
                DrawCamseqHeaderButtons(line0, asyncW, r, index);
            }
        }
    }
}
