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
        // camseq：路径点行 + header 上的镜头编排 / 预设入口
        // ------------------------------------------------------------------

        // 「编排」40 + 「预设▾」52 + 「+ wp」50 + 间距（emoji 在 IMGUI 默认字体里是方块，用汉字）
        const float CamseqHeaderButtonsWidth = 40f + 4f + 52f + 4f + 50f + 4f;

        void DrawCamseqHeaderButtons(Rect line0, float asyncW, VNRow r, int index)
        {
            float x = line0.xMax - asyncW - CamseqHeaderButtonsWidth + 4f;

            bool linked = VNCamseqEditorWindow.IsLinkedTo(this, index);
            var prevBg = GUI.backgroundColor;
            if (linked) GUI.backgroundColor = new Color(1f, 0.85f, 0.35f);
            if (GUI.Button(new Rect(x, line0.y, 40f, line0.height),
                    new GUIContent("编排", linked
                        ? "镜头编排窗口正在编辑这一行（再点一次把窗口调到前面）"
                        : "在镜头编排窗口里可视化编辑这段运镜（画布拖点、时间轴预览，改动回写本行）"),
                    EditorStyles.miniButton))
                VNCamseqEditorWindow.OpenLinked(this, index);
            GUI.backgroundColor = prevBg;
            x += 44f;

            var presetRect = new Rect(x, line0.y, 52f, line0.height);
            if (GUI.Button(presetRect,
                    new GUIContent("预设▾", "套用内置运镜模板或已保存的预设（会覆盖本行的全部路径点）"),
                    EditorStyles.miniButton))
                ShowCamseqPresetMenu(presetRect, r, index);
            x += 56f;

            if (GUI.Button(new Rect(x, line0.y, 50f, line0.height),
                    new GUIContent("+ wp", "追加一个路径点"), EditorStyles.miniButton))
            {
                MarkStructural();
                r.camLines.Add(new VNCamWaypoint
                {
                    point = "middle", zoom = 1.4f, duration = 0.8f,
                }.Format());
                Bump();
            }
        }

        /// <summary>
        /// 画一行路径点。能解析的走字段化控件，解析不了的退回纯文本框并标黄
        /// （旧剧本的手写行、未来新语法都不会被吞掉）。返回 true = 本行被删掉，
        /// 调用方要立刻停止遍历。
        /// </summary>
        bool DrawCamWaypointRow(Rect rect, VNRow r, int i)
        {
            GUI.Label(new Rect(rect.x, rect.y, 12f, rect.height), "›", EditorStyles.miniLabel);
            var delRect = new Rect(rect.xMax - 20f, rect.y, 20f, rect.height);
            var body = new Rect(rect.x + 12f, rect.y, rect.xMax - 24f - rect.x - 12f, rect.height);

            if (VNCamWaypoint.TryParse(r.camLines[i], out var wp))
            {
                if (DrawCamWaypointFields(body, r, i, wp))
                    r.camLines[i] = wp.Format();
            }
            else
            {
                // 解析不了：原样文本 + 黄底提醒，鼠标悬停说明为什么
                var style = new GUIStyle(EditorStyles.textField);
                style.normal.textColor = new Color(0.95f, 0.8f, 0.25f);
                EditorGUI.DrawRect(body, new Color(0.85f, 0.65f, 0.1f, 0.12f));
                string nv = EditorGUI.TextField(body, r.camLines[i], style);
                if (nv != r.camLines[i]) r.camLines[i] = nv;
                GUI.Label(body, new GUIContent("", "这一行认不出来，暂按纯文本保留。\n" +
                    "语法：> 目标点 [zoom] [时长] [ease:名] [xfade:秒] [hold:秒] " +
                    "[shake:等级|强度,秒数]\n" +
                    "改成合法写法后会自动变回字段化控件。"));
            }

            if (GUI.Button(delRect, "x", EditorStyles.miniButton))
            {
                MarkStructural();
                r.camLines.RemoveAt(i);
                Bump();
                return true;
            }
            return false;
        }

        /// <summary>字段化的一行；返回 true = 有改动，调用方负责写回文本</summary>
        bool DrawCamWaypointFields(Rect rect, VNRow r, int i, VNCamWaypoint wp)
        {
            EditorGUI.BeginChangeCheck();
            float x = rect.x;

            // 点位类型（由 point token 反推；切换时给一个合理的初值）
            var kind = wp.Kind;
            var newKind = (VNCamPointKind)EditorGUI.Popup(
                new Rect(x, rect.y, 56f, rect.height), (int)kind, CamPointKindNames);
            x += 60f;
            if (newKind != kind)
            {
                wp.point = newKind == VNCamPointKind.Anchor ? "middle"
                    : newKind == VNCamPointKind.Coords ? "0,0"
                    : newKind == VNCamPointKind.Stay ? VNCamWaypointDef.StayToken
                    : (_ctx.characterIds.Length > 0 ? _ctx.characterIds[0] : "");
                // 切到「原地」默认时长 0（画面纹丝不动）；切出去时给回常用默认值
                if (newKind == VNCamPointKind.Stay) wp.duration = 0f;
                else if (kind == VNCamPointKind.Stay) wp.duration = VNCamWaypoint.DefaultDuration;
                kind = newKind;
            }

            // 尾部固定宽度：zoom / 秒 / ease / xfade / hold / 震
            const float tailW = 34f + 48f + 4f + 20f + 42f + 4f + 76f + 4f + 34f + 40f
                                + 4f + 32f + 40f + 4f + 24f + 118f;
            float targetW = Mathf.Max(90f, rect.xMax - x - tailW - 4f);
            DrawCamPointTarget(new Rect(x, rect.y, targetW, rect.height), r, i, wp, kind);
            x = rect.xMax - tailW;

            GUI.Label(new Rect(x, rect.y, 34f, rect.height),
                new GUIContent("zoom", "取景倍率：1 = 全图，越大越推近"), EditorStyles.miniLabel);
            x += 34f;
            if (kind == VNCamPointKind.Stay)
            {
                // 原地点的 zoom 沿用上一个点，这里给个禁用占位——留个能敲的框只会误导
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.TextField(new Rect(x, rect.y, 48f, rect.height), "沿用");
            }
            else
            {
                wp.zoom = EditorGUI.FloatField(new Rect(x, rect.y, 48f, rect.height), wp.zoom);
            }
            x += 52f;

            GUI.Label(new Rect(x, rect.y, 20f, rect.height),
                new GUIContent("秒", "移动到本点的时长；0 = 瞬切"), EditorStyles.miniLabel);
            x += 20f;
            wp.duration = EditorGUI.FloatField(new Rect(x, rect.y, 42f, rect.height), wp.duration);
            x += 46f;

            // 缓动/锚点/部位一律用同步 Popup：SpritePopup 那套是异步回调，
            // 会把选中值写进 VNRow.values（路径点存的是 camLines 文本，两条路径不能混）
            wp.ease = OptionalPopup(new Rect(x, rect.y, 76f, rect.height),
                wp.ease, VNScenarioSchema.EaseNames, "(默认缓动)");
            x += 80f;

            GUI.Label(new Rect(x, rect.y, 34f, rect.height),
                new GUIContent("xfade", "叠化到本点的秒数（>0 时代替平移/瞬切）"),
                EditorStyles.miniLabel);
            x += 34f;
            wp.fade = Mathf.Max(0f,
                EditorGUI.FloatField(new Rect(x, rect.y, 40f, rect.height), wp.fade));
            x += 44f;

            GUI.Label(new Rect(x, rect.y, 32f, rect.height),
                new GUIContent("hold", "到达本点后停留的秒数（0 = 不停，直接走下一段）"),
                EditorStyles.miniLabel);
            x += 32f;
            wp.hold = Mathf.Max(0f,
                EditorGUI.FloatField(new Rect(x, rect.y, 40f, rect.height), wp.hold));
            x += 44f;

            GUI.Label(new Rect(x, rect.y, 24f, rect.height),
                new GUIContent("震", VNCamShakeUi.Tooltip), EditorStyles.miniLabel);
            x += 24f;
            wp.shake = VNCamShakeUi.Draw(new Rect(x, rect.y, 118f, rect.height), wp.shake);

            return EditorGUI.EndChangeCheck();
        }

        void DrawCamPointTarget(Rect rect, VNRow r, int i, VNCamWaypoint wp, VNCamPointKind kind)
        {
            switch (kind)
            {
                case VNCamPointKind.Stay:
                    // 沿用上一个点，没有可编辑的目标——写句人话比留个空框强
                    GUI.Label(rect, new GUIContent("沿用上一个点（位置与 zoom 都不变）",
                        "原地：镜头一动不动，专门用来在序列中间插一段震动或停顿。\n" +
                        "时长写 0 就是完全静止；不能当第一个路径点（没有上一个点可沿用）。"),
                        EditorStyles.miniLabel);
                    break;

                case VNCamPointKind.Anchor:
                {
                    int at = Mathf.Max(0,
                        System.Array.IndexOf(VNCamWaypoint.Anchors, wp.point.ToLower()));
                    at = EditorGUI.Popup(rect, at, CamAnchorDisplayNames);
                    wp.point = VNCamWaypoint.Anchors[at];
                    break;
                }

                case VNCamPointKind.Character:
                {
                    VNCamWaypoint.SplitCharacter(wp.point, out string id, out string part);
                    float half = Mathf.Max(60f, rect.width * 0.58f);
                    // 角色 id 走 PopupString：它是同步返回的，custom… 还能手打场景里没有的 id
                    string newId = PopupString(new Rect(rect.x, rect.y, half, rect.height),
                        id, _ctx.characterIds, "(选角色)", (r, $"wp{i}.char"));
                    string newPart = OptionalPopup(
                        new Rect(rect.x + half + 4f, rect.y, rect.width - half - 4f, rect.height),
                        part, VNCamWaypoint.CharacterParts, "(整体)");
                    wp.point = VNCamWaypoint.JoinCharacter(newId, newPart);
                    break;
                }

                case VNCamPointKind.Coords:
                {
                    VNCamWaypoint.SplitCoords(wp.point, out float px, out float py);
                    float half = (rect.width - 22f) * 0.5f;
                    GUI.Label(new Rect(rect.x, rect.y, 10f, rect.height), "x",
                        EditorStyles.miniLabel);
                    px = EditorGUI.FloatField(
                        new Rect(rect.x + 10f, rect.y, half, rect.height), px);
                    GUI.Label(new Rect(rect.x + half + 14f, rect.y, 10f, rect.height), "y",
                        EditorStyles.miniLabel);
                    py = EditorGUI.FloatField(
                        new Rect(rect.x + half + 24f, rect.y, half - 2f, rect.height), py);
                    wp.point = VNCamWaypoint.JoinCoords(px, py);
                    break;
                }
            }
        }

        /// <summary>「(留空) + 候选」的同步下拉；返回 "" 表示选了留空项</summary>
        static string OptionalPopup(Rect rect, string value, string[] options, string emptyLabel)
        {
            var display = new string[options.Length + 1];
            display[0] = emptyLabel;
            for (int i = 0; i < options.Length; i++) display[i + 1] = options[i];

            int index = string.IsNullOrEmpty(value)
                ? 0 : System.Array.IndexOf(options, value) + 1;
            if (index < 0) index = 0;   // 认不出的值当留空显示（TryParse 已挡掉大部分）
            int picked = EditorGUI.Popup(rect, index, display);
            return picked <= 0 ? "" : options[picked - 1];
        }

        static readonly string[] CamPointKindNames = { "锚点", "角色", "坐标", "原地" };

        static readonly string[] CamAnchorDisplayNames =
        {
            "topleft（左上）", "top（上）", "topright（右上）",
            "left（左）", "middle（中）", "right（右）",
            "bottomleft（左下）", "bottom（下）", "bottomright（右下）",
        };

        void ShowCamseqPresetMenu(Rect rect, VNRow r, int index)
        {
            var menu = new GenericMenu();
            string character = _ctx.characterIds.Length > 0 ? _ctx.characterIds[0] : null;

            foreach (var entry in VNCamseqTemplates.All)
            {
                string text = VNCamseqTemplates.Resolve(entry.text, character);
                menu.AddItem(new GUIContent($"内置模板/{entry.name}"), false,
                    () => ApplyCamseqTextToRow(index, text, "套用模板"));
            }

            var library = LoadCamseqPresetLibrary();
            if (library != null && library.presets.Count > 0)
            {
                menu.AddSeparator("");
                foreach (var preset in library.presets)
                {
                    if (preset == null || string.IsNullOrEmpty(preset.name)) continue;
                    string text = preset.camseqText;
                    menu.AddItem(new GUIContent($"我的预设/{preset.name}"), false,
                        () => ApplyCamseqTextToRow(index, text, "套用预设"));
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("把本行存为预设…"), false, () => SaveRowAsCamseqPreset(r));
            menu.DropDown(rect);
        }

        static VNCamseqPresetLibrary LoadCamseqPresetLibrary() =>
            AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(
                VNCamseqEditorWindow.LibraryPath);

        void SaveRowAsCamseqPreset(VNRow r)
        {
            if (r.camLines == null || r.camLines.Count == 0)
            {
                ShowNotification(new GUIContent("这一行还没有路径点"));
                return;
            }
            string name = VNTextPromptWindow.Prompt("存为镜头预设", "预设名称", "");
            if (string.IsNullOrEmpty(name)) return;
            VNCamseqEditorWindow.SavePreset(name, CamseqTextOfRow(r));
            ShowNotification(new GUIContent($"已保存预设「{name}」"));
        }

        /// <summary>把一行 camseq（含 header 参数）拍成完整的 camseq 文本</summary>
        static string CamseqTextOfRow(VNRow r)
        {
            var header = new Dictionary<string, string>();
            foreach (var key in VNCamseqText.HeaderKeys)
            {
                string v = r.Get(key);
                if (!string.IsNullOrEmpty(v)) header[key] = v;
            }
            return VNCamseqText.Join(header, r.camLines ?? new List<string>());
        }

        /// <summary>套用一段 camseq 文本到指定行（预设/模板/镜头窗口回写共用）</summary>
        void ApplyCamseqTextToRow(int index, string camseqText, string what)
        {
            if (!ApplyCamseqText(index, camseqText))
            {
                ShowNotification(new GUIContent($"{what}失败：目标行不是 camseq"));
                return;
            }
            ShowNotification(new GUIContent(what + "完成"));
        }

        // ---- 供镜头编排窗口调用的公开接口 ----

        /// <summary>当前打开文件的显示名（镜头窗口的绑定条用）</summary>
        public string ScenarioDisplayName =>
            string.IsNullOrEmpty(_path) ? "(未命名)" : Path.GetFileName(_path);

        /// <summary>文档改动计数：镜头窗口靠它发现「剧本那边被改了」</summary>
        public int DocVersion => _version;

        public bool IsCamseqRow(int index) =>
            index >= 0 && index < _doc.rows.Count &&
            _doc.rows[index].kind == VNRowKind.Command &&
            _doc.rows[index].keyword == "camseq" &&
            _doc.rows[index].camLines != null;

        /// <summary>当前选中行是 camseq 时返回它的行号，否则 -1（自动跟随用）</summary>
        public int SelectedCamseqRow()
        {
            int index = _list != null ? _list.index : -1;
            return IsCamseqRow(index) ? index : -1;
        }

        /// <summary>
        /// 某一行的舞台推算快照（镜头编排窗口拿它画底图和立绘）。
        /// 数据源就是行左侧「舞台一览」那格用的同一套逐行推算，
        /// 所以两边看到的背景/在场角色永远一致。
        /// 注意它是**按文件顺序的近似**：jump / choice 分支不展开，
        /// camseq 落在分支里时可能推不准——镜头窗口那边留了手动覆盖的口子。
        /// </summary>
        public bool TryGetRowStage(int index, out VNRowStageInfo info)
        {
            info = null;
            if (index < 0 || index >= _doc.rows.Count) return false;
            RebuildStageStatesIfNeeded();
            if (index >= _stageStates.Count) return false;

            var state = _stageStates[index];
            info = new VNRowStageInfo
            {
                bgId = state.bg,
                cgId = state.cg,
                backdrop = state.cg != null
                    ? SpriteFor(_cgPreviews, state.cg)
                    : state.bg != null ? SpriteFor(_backgroundPreviews, state.bg) : null,
            };
            // CG 盖着且没 keepChars 时立绘本来就看不见，别画上去误导取景
            if (state.cg == null || state.cgKeepChars)
                foreach (var c in state.chars)
                {
                    var preview = _characterPreviews.Find(p => p.id == c.id);
                    info.characters.Add(new VNRowStageChar
                    {
                        id = c.id,
                        slot = c.slot,
                        sprite = preview != null ? preview.DefaultSprite : null,
                    });
                }
            return true;
        }

        /// <summary>读出某一行的完整 camseq 文本</summary>
        public bool TryGetCamseqText(int index, out string text)
        {
            text = null;
            if (!IsCamseqRow(index)) return false;
            text = CamseqTextOfRow(_doc.rows[index]);
            return true;
        }

        /// <summary>
        /// 把一段 camseq 文本写回指定行（header 参数 + 路径点行整体替换）。
        /// 撤销走和手动编辑一样的 1 秒节流，免得镜头窗口实时回写时刷爆撤销栈。
        /// </summary>
        public bool ApplyCamseqText(int index, string camseqText)
        {
            if (!IsCamseqRow(index)) return false;
            if (!VNCamseqText.TrySplit(camseqText, out var header, out var lines)) return false;

            var r = _doc.rows[index];
            if (CamseqTextOfRow(r) == VNCamseqText.Join(header, lines)) return true; // 无变化

            PushUndoThrottled();
            foreach (var key in VNCamseqText.HeaderKeys)
                r.Set(key, header.TryGetValue(key, out string v) ? v : "");
            r.camLines.Clear();
            r.camLines.AddRange(lines);
            Bump();
            Repaint();
            return true;
        }

        /// <summary>外部改动的撤销入口：与 OnGUI 末尾同一套 1 秒节流</summary>
        void PushUndoThrottled()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastUndoPush <= 1.0) return;
            PushUndo(_doc.GenerateText());
            _lastUndoPush = now;
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

        /// <summary>
        /// 右键点行首命令按钮 = 打字换命令。GUI.Button 只吃左键，所以右键要自己收；
        /// ReorderableList 的选中/拖动也只认左键，这里 Use() 掉不会抢它的事件。
        /// </summary>
        static bool ConsumeRightClick(Rect rect)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 1 ||
                !rect.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        /// <summary>右键行首按钮：打字换这一行的命令（左键那套分类菜单原样保留）</summary>
        void ShowRowTypeSearch(Rect rect, VNRow row)
        {
            PopupWindow.Show(rect, new VNSearchPopup(
                "换成哪个命令？（打字筛选）", BuildCommandItems(true, false),
                item =>
                {
                    if (item.value == "say")
                    {
                        if (row.kind == VNRowKind.Say) return;
                        MarkStructural();
                        SetSayRow(row);
                    }
                    else
                    {
                        if (row.kind == VNRowKind.Command && row.keyword == item.value) return;
                        MarkStructural();
                        SetKeyword(row, item.value);
                    }
                    Repaint();
                }));
        }

        /// <summary>底部 [+]：打字挑要加的行（命令 / 台词 / 注释 / 空行）</summary>
        void ShowAddSearch(Rect rect)
        {
            PopupWindow.Show(rect, new VNSearchPopup(
                "加一行什么？（打字筛选）", BuildCommandItems(true, true),
                item =>
                {
                    _pendingNewRow = NewRowForSearchValue(item.value);
                    _pendingNewRowAbove = false;
                    Repaint();
                }));
        }

        void ShowCommandPalette()
        {
            var activator = new Rect(position.width * 0.5f - 270f, 70f, 540f, 0f);
            PopupWindow.Show(activator, new VNCommandPalette(
                BuildCommandItems(true, false),
                BuildParamCandidates,
                NewRowForSearchValue,
                (row, above) =>
                {
                    _pendingNewRow = row;
                    _pendingNewRowAbove = above;
                    Repaint();
                }));
        }

        /// <summary>搜索候选的 value → 新行（"say" / "#" / "" 是三个特殊值）</summary>
        VNRow NewRowForSearchValue(string value)
        {
            switch (value)
            {
                case "say": return NewSayRow();
                case "#": return new VNRow { kind = VNRowKind.Raw, raw = "# " };
                case "": return new VNRow { kind = VNRowKind.Raw, raw = "" };
                default: return NewCommandRow(value);
            }
        }

        /// <summary>命令候选表：数据源就是 Schema，以后加新命令自动出现在搜索里</summary>
        List<VNSearchItem> BuildCommandItems(bool includeSay, bool includeRaw)
        {
            var items = new List<VNSearchItem>();
            if (includeSay)
                items.Add(new VNSearchItem
                {
                    value = "say",
                    title = "say（对白）",
                    subtitle = "普通台词行",
                    searchExtra = "Dialogue 对话 台词 duibai",
                    accent = CategoryColor("Dialogue"),
                });

            foreach (var command in VNScenarioSchema.Commands)
                items.Add(new VNSearchItem
                {
                    value = command.keyword,
                    title = CommandDisplayName(command.keyword),
                    subtitle = FirstLine(command.hint),
                    searchExtra = CategoryDisplayName(command.category),
                    accent = CategoryColor(command.category),
                });

            if (includeRaw)
            {
                items.Add(new VNSearchItem
                    { value = "#", title = "# 注释", searchExtra = "comment 注释" });
                items.Add(new VNSearchItem
                    { value = "", title = "（空行）", searchExtra = "blank 空行" });
            }
            return items;
        }

        /// <summary>命令面板问参数时的候选；返回 null = 这个参数是自由文本/数字，直接打</summary>
        List<VNSearchItem> BuildParamCandidates(VNRow row, VNParamDef parameter)
        {
            if (parameter == null) return null;

            string[] options = parameter.id == "say.speaker"
                ? _ctx.characterIds
                : OptionsFor(row, parameter);
            if (options == null) return null;

            string[] display = parameter.id == "say.speaker"
                ? null : DisplayOptionsFor(row, parameter, options);

            var items = new List<VNSearchItem>();
            for (int i = 0; i < options.Length; i++)
                items.Add(new VNSearchItem
                {
                    value = options[i],
                    title = display != null && display.Length == options.Length
                        ? display[i] : options[i],
                });
            return items;
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int at = text.IndexOf('\n');
            return at < 0 ? text : text.Substring(0, at);
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

            // if + cost + flag + jump + delete
            float tailW = 16f + 66f + 4f + 16f + 66f + 4f + 118f + 4f + 118f + 4f + 20f;
            o.text = EditorGUI.TextField(
                new Rect(x, rect.y, rect.xMax - x - tailW - 6f, rect.height), o.text);
            x = rect.xMax - tailW;

            GUI.Label(new Rect(x, rect.y, 16f, rect.height),
                new GUIContent("if", "if:条件——不满足则隐藏该选项（无空格，如 魅力>=20）"),
                EditorStyles.miniLabel);
            x += 16f;
            o.condition = EditorGUI.TextField(new Rect(x, rect.y, 66f, rect.height), o.condition);
            x += 70f;
            GUI.Label(new Rect(x, rect.y, 16f, rect.height),
                new GUIContent("$", "cost:花费——如 金钱-100，付不起时选项置灰，选中自动扣除并飘字"),
                EditorStyles.miniLabel);
            x += 16f;
            o.costOp = EditorGUI.TextField(new Rect(x, rect.y, 66f, rect.height), o.costOp);
            x += 70f;

            o.flagOp = PopupString(new Rect(x, rect.y, 118f, rect.height),
                o.flagOp, _flagOps, "(no flag)", (r, $"opt{i}.flag"));
            x += 122f;
            o.jump = PopupString(new Rect(x, rect.y, 118f, rect.height),
                o.jump, LabelAddressOptions(), "(continue)", (r, $"opt{i}.jump"));
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

        void DrawParams(Rect rect, VNRow r, VNCommandDef def) =>
            DrawParams(rect, r, def, 0, int.MaxValue);

        /// <summary>
        /// 画 [from, to) 区间的参数。event 靠它把「通用参数」与「模块专属参数」
        /// 分成两行画，其余命令一律整段画完。
        /// </summary>
        void DrawParams(Rect rect, VNRow r, VNCommandDef def, int from, int to)
        {
            int end = Mathf.Min(to, def.parameters.Length);
            if (from >= end) return;

            // 计算总权重与标签宽
            float totalWeight = 0f;
            float labelTotal = 0f;
            for (int i = from; i < end; i++)
            {
                totalWeight += def.parameters[i].weight;
                labelTotal += LabelWidth(def.parameters[i]);
            }
            float fieldAvail = rect.width - labelTotal - (end - from) * 4f;
            float x = rect.x;

            for (int i = from; i < end; i++)
            {
                var p = def.parameters[i];
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

            // 音频 id 参数：左侧加 ▶ 试听小按钮（编辑器内直接预览选中的素材）
            if (p.source == VNParamSource.AudioBgm || p.source == VNParamSource.AudioSe ||
                p.source == VNParamSource.AudioVoice)
            {
                var playRect = new Rect(rect.x, rect.y, 20f, rect.height);
                rect = new Rect(playRect.xMax + 2f, rect.y,
                    Mathf.Max(34f, rect.width - 22f), rect.height);
                DrawAudioPreviewButton(playRect, p.source, v);
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

            string nv2 = PopupString(rect, v, options, "-", (r, p.id),
                DisplayOptionsFor(r, p, options));
            if (nv2 != v) r.Set(p.id, nv2);
        }

        /// <summary>枚举类参数的中英对照显示名（写进剧本的值仍是英文）；没有对照就返回 null</summary>
        static string[] DisplayOptionsFor(VNRow r, VNParamDef p, string[] options)
        {
            if (IsTransitionParameter(r, p))
                return BuildTranslatedOptions(options, TransitionTranslations);
            if (IsEmoteParameter(r, p))
                return BuildTranslatedOptions(options, EmoteTranslations);
            if (IsMarkParameter(r, p))
                return BuildTranslatedOptions(options, MarkTranslations);
            if (r.keyword == "show" && p.id == "with")
                return BuildTranslatedOptions(options, EntranceTranslations);
            if (r.keyword == "hide" && p.id == "with")
                return BuildTranslatedOptions(options, ExitTranslations);
            if ((r.keyword == "show" && p.id == "from") ||
                (r.keyword == "hide" && p.id == "to"))
                return BuildTranslatedOptions(options, SideTranslations);
            return null;
        }

        static bool IsTransitionParameter(VNRow row, VNParamDef parameter) =>
            (row.keyword == "bg" && parameter.id == "transition") ||
            (row.keyword == "cg" && parameter.id == "transition") ||
            (row.keyword == "transition" && parameter.id == "type");

        static bool IsEmoteParameter(VNRow row, VNParamDef parameter) =>
            row.keyword == "emote" && parameter.id == "emote";

        static bool IsMarkParameter(VNRow row, VNParamDef parameter) =>
            row.keyword == "mark" && (parameter.id == "mark" || parameter.id == "mode");

        static string[] BuildTranslatedOptions(string[] options,
            Dictionary<string, string> translations)
        {
            var display = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
                display[i] = translations.TryGetValue(options[i], out string translation)
                    ? $"{options[i]}（{translation}）" : options[i];
            return display;
        }

        /// <summary>
        /// 某类定义资产的 id 候选（event 各模块的 id: / theme: / persona: 这些）。
        /// **必须缓存**：OptionsFor 每帧都被调，直接 FindAssets 会把编辑器拖垮。
        /// 缓存在 RefreshSources 里清（登记新素材后由 VNAssetLibraryEvents 触发）。
        /// </summary>
        readonly Dictionary<string, string[]> _assetIdCache = new Dictionary<string, string[]>();

        string[] AssetIdOptions(VNParamDef p)
        {
            if (string.IsNullOrEmpty(p.assetType)) return null;
            string key = p.assetType + "/" + p.assetIdField;
            if (_assetIdCache.TryGetValue(key, out var cached)) return cached;

            var list = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + p.assetType))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                string id = null;
                if (!string.IsNullOrEmpty(p.assetIdField))
                {
                    var prop = new SerializedObject(asset).FindProperty(p.assetIdField);
                    if (prop != null && prop.propertyType == SerializedPropertyType.String)
                        id = prop.stringValue;
                }
                // id 留空按资产文件名认，与运行时各模块的容错一致
                if (string.IsNullOrEmpty(id)) id = asset.name;
                if (!list.Contains(id)) list.Add(id);
            }
            list.Sort(System.StringComparer.Ordinal);
            // 一个资产都没有时返回 null 退回文本框：否则玩家面对一个空下拉，
            // 连「先把 id 写上、资产稍后再建」都做不到
            var result = list.Count > 0 ? list.ToArray() : null;
            _assetIdCache[key] = result;
            return result;
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
                case VNParamSource.WeatherId: return _ctx.weatherIds;
                case VNParamSource.InterludeId: return _ctx.interludeIds;
                case VNParamSource.TutorialId: return _ctx.tutorialIds;
                case VNParamSource.AssetId: return AssetIdOptions(p);
                case VNParamSource.UiSkinId: return UiSkinOptions(r.Get(p.dependsOn));
                case VNParamSource.Label: return LabelAddressOptions();
                case VNParamSource.Flag: return _flags.ToArray();
                default: return null; // Text / Number → 文本框
            }
        }

        /// <summary>
        /// ui 命令第二参数的候选。kind=name 时列的是**内置名字样式预设**
        /// （不在 VNGameConfig 登记，与 dialogue/choice 的皮肤 id 是两套东西）。
        /// </summary>
        string[] UiSkinOptions(string kind)
        {
            var options = new List<string> { "default" };
            if (kind == "name")
            {
                foreach (var a in VNNameplateStyle.Aliases) options.Add(a.name);
            }
            else if (kind == "choice")
            {
                options.AddRange(_ctx.choiceSkinIds);
            }
            else
            {
                options.AddRange(_ctx.dialogueSkinIds);
            }
            return options.ToArray();
        }

        string[] LabelAddressOptions()
        {
            var options = new List<string>(_labels);
            options.AddRange(_ctx.qualifiedLabelIds);
            return options.ToArray();
        }

        // ---- 舞台一览小格 ----

        /// <summary>一行的舞台快照：该行播完后台上的背景/CG 与角色站位</summary>
        sealed class RowStageState
        {
            public string bg;
            public string cg;
            public bool cgKeepChars;
            public readonly List<StageChar> chars = new List<StageChar>();
        }

        struct StageChar
        {
            public string id;
            public int slot;   // 0=left 1=center 2=right
        }

        static readonly Color[] StagePalette =
        {
            new Color(0.98f, 0.55f, 0.62f), new Color(0.45f, 0.75f, 1f),
            new Color(0.55f, 0.88f, 0.55f), new Color(1f, 0.82f, 0.4f),
            new Color(0.8f, 0.62f, 1f), new Color(0.45f, 0.88f, 0.85f),
            new Color(1f, 0.68f, 0.4f), new Color(0.75f, 0.8f, 0.55f),
        };

        /// <summary>按文件顺序逐行推算舞台状态（jump/choice 分支不展开，与
        /// "重建前置状态"调试同一近似）。仅 _version 变化时重算。</summary>
        void RebuildStageStatesIfNeeded()
        {
            if (_stageStatesVersion == _version) return;
            _stageStatesVersion = _version;
            _stageStates.Clear();

            string bg = null, cg = null;
            bool cgKeepChars = false;
            var chars = new List<StageChar>();

            foreach (var r in _doc.rows)
            {
                if (r.kind == VNRowKind.Command)
                {
                    switch (r.keyword)
                    {
                        case "bg":
                            if (!string.IsNullOrEmpty(r.Get("id"))) bg = r.Get("id");
                            break;
                        case "cg":
                        {
                            string id = r.Get("id");
                            if (id == "off") { cg = null; cgKeepChars = false; }
                            else if (!string.IsNullOrEmpty(id))
                            {
                                cg = id;
                                cgKeepChars = r.Get("chars") == "keep";
                            }
                            break;
                        }
                        case "show": ApplyStageShow(chars, r); break;
                        case "hide":
                        {
                            string id = r.Get("character");
                            chars.RemoveAll(c => c.id == id);
                            break;
                        }
                        case "move": ApplyStageMove(chars, r); break;
                    }
                }

                var state = new RowStageState
                    { bg = bg, cg = cg, cgKeepChars = cgKeepChars };
                state.chars.AddRange(chars);
                _stageStates.Add(state);
            }
        }

        static void ApplyStageShow(List<StageChar> chars, VNRow r)
        {
            string id = r.Get("character");
            if (string.IsNullOrEmpty(id)) return;
            string at = r.Get("at");
            int existing = chars.FindIndex(c => c.id == id);
            if (existing >= 0)
            {
                // 已在场且没写 at → 原地换表情，站位不动（与运行时语义一致）
                if (!string.IsNullOrEmpty(at))
                    chars[existing] = new StageChar { id = id, slot = SlotIndex(at) };
                return;
            }
            chars.Add(new StageChar { id = id, slot = SlotIndex(at) });
        }

        static void ApplyStageMove(List<StageChar> chars, VNRow r)
        {
            string id = r.Get("character");
            int i = chars.FindIndex(c => c.id == id);
            if (i < 0) return;
            chars[i] = new StageChar { id = id, slot = SlotIndex(r.Get("at")) };
        }

        static int SlotIndex(string at)
        {
            switch (at)
            {
                case "left": return 0;
                case "right": return 2;
                case null: case "": case "center": return 1;
                default:
                    // 自定义 x 坐标：按左/中/右粗分桶
                    if (float.TryParse(at, out float x))
                        return x < -120f ? 0 : x > 120f ? 2 : 1;
                    return 1;
            }
        }

        Color StageCharColor(string id)
        {
            int i = System.Array.IndexOf(_ctx.characterIds, id);
            if (i < 0)
            {
                i = 0;
                foreach (char c in id) i = i * 31 + c;
                i = Mathf.Abs(i);
            }
            return StagePalette[i % StagePalette.Length];
        }

        static string SlotName(int slot) => slot == 0 ? "左" : slot == 2 ? "右" : "中";

        void DrawStageCell(Rect rect, RowStageState state)
        {
            // 背景（CG 显示时优先画 CG）缩略图
            var thumbRect = new Rect(rect.x, rect.y, 30f, rect.height);
            EditorGUI.DrawRect(thumbRect, new Color(0.08f, 0.08f, 0.08f, 1f));
            string shownId = state.cg ?? state.bg;
            Sprite sprite = state.cg != null
                ? SpriteFor(_cgPreviews, state.cg)
                : state.bg != null ? SpriteFor(_backgroundPreviews, state.bg) : null;
            if (sprite != null)
                DrawSpritePreview(new Rect(thumbRect.x + 1f, thumbRect.y + 1f,
                    thumbRect.width - 2f, thumbRect.height - 2f), sprite);
            else
                GUI.Label(thumbRect, shownId == null ? "—" : "?",
                    EditorStyles.centeredGreyMiniLabel);

            // 三个站位格（左/中/右），有人 = 角色专属色块
            bool dimmed = state.cg != null && !state.cgKeepChars; // CG 默认藏立绘
            float blockW = 7f;
            float blockH = Mathf.Min(rect.height, 13f);
            float blockY = rect.y + (rect.height - blockH) * 0.5f;
            float baseX = thumbRect.xMax + 4f;
            for (int slot = 0; slot < 3; slot++)
                EditorGUI.DrawRect(new Rect(baseX + slot * (blockW + 2f), blockY,
                    blockW, blockH), new Color(0f, 0f, 0f, 0.28f));
            foreach (var c in state.chars)
            {
                Color color = StageCharColor(c.id);
                if (dimmed) color.a = 0.3f;
                EditorGUI.DrawRect(new Rect(baseX + c.slot * (blockW + 2f) + 1f,
                    blockY + 1f, blockW - 2f, blockH - 2f), color);
            }

            // 整格 tooltip：背景/CG/在场角色一览
            var tip = new StringBuilder();
            tip.Append("背景: ").Append(string.IsNullOrEmpty(state.bg) ? "（无）" : state.bg);
            if (state.cg != null)
                tip.Append("\nCG: ").Append(state.cg)
                   .Append(state.cgKeepChars ? "（保留立绘）" : "（隐藏立绘）");
            tip.Append("\n台上: ");
            if (state.chars.Count == 0) tip.Append("（无人）");
            else
                for (int i = 0; i < state.chars.Count; i++)
                {
                    if (i > 0) tip.Append("、");
                    tip.Append(state.chars[i].id)
                       .Append('（').Append(SlotName(state.chars[i].slot)).Append('）');
                }
            tip.Append("\n（按文件顺序推算，jump/choice 分支不展开）");
            GUI.Label(rect, new GUIContent("", tip.ToString()));
        }

        // ---- 音频行内试听 ----

        AudioClip FindAudioClip(VNParamSource source, string id)
        {
            if (string.IsNullOrEmpty(id) || id == "stop") return null;
            Dictionary<string, AudioClip> clips;
            switch (source)
            {
                case VNParamSource.AudioBgm: clips = _bgmClips; break;
                case VNParamSource.AudioSe: clips = _seClips; break;
                case VNParamSource.AudioVoice: clips = _voiceClips; break;
                default: return null;
            }
            return clips.TryGetValue(id, out var clip) ? clip : null;
        }

        void DrawAudioPreviewButton(Rect rect, VNParamSource source, string id)
        {
            AudioClip clip = FindAudioClip(source, id);
            string key = source + "|" + id;
            bool playing = _previewAudioKey == key;

            // 试听按钮不能把文档标脏（同 分类颜色 开关的处理）
            bool previousChanged = GUI.changed;
            using (new EditorGUI.DisabledScope(clip == null || !VNEditorAudioPreview.Available))
            {
                var content = playing
                    ? new GUIContent("■", "停止试听")
                    : new GUIContent("▶", clip != null
                        ? $"试听「{id}」（编辑器预览，不含音量标定/循环）"
                        : "先选择一个已登记的音频 id");
                if (GUI.Button(rect, content, EditorStyles.miniButton))
                {
                    if (playing) StopAudioPreview();
                    else StartAudioPreview(key, clip);
                }
            }
            GUI.changed = previousChanged;
        }

        void StartAudioPreview(string key, AudioClip clip)
        {
            VNEditorAudioPreview.Play(clip);
            _previewAudioKey = key;
            EditorApplication.update -= PollAudioPreview;
            if (VNEditorAudioPreview.CanQueryPlaying)
                EditorApplication.update += PollAudioPreview;
            Repaint();
        }

        void StopAudioPreview()
        {
            EditorApplication.update -= PollAudioPreview;
            if (_previewAudioKey != null)
            {
                _previewAudioKey = null;
                VNEditorAudioPreview.StopAll();
                Repaint();
            }
        }

        void PollAudioPreview()
        {
            if (_previewAudioKey == null)
            {
                EditorApplication.update -= PollAudioPreview;
                return;
            }
            if (!VNEditorAudioPreview.IsPlaying())
            {
                _previewAudioKey = null;
                EditorApplication.update -= PollAudioPreview;
                Repaint();
            }
        }

        /// <summary>
        /// 下拉 + "custom…" 自由输入的通用控件。emptyLabel 对应空值。
        ///
        /// 【同步契约不能破】本函数是同步返回的：调用方拿返回值自己写回。
        /// camseq 路径点（值在 camLines 文本里）和 choice 选项行（值在 VNChoiceOptionRow
        /// 字段里）都靠这一点，所以搜索弹窗只能把选中值丢进 _popupResults，
        /// 由下一帧的本函数 return 出去——绝不能学 SpritePopup 在回调里直写 values。
        /// </summary>
        string PopupString(Rect rect, string value, string[] options, string emptyLabel,
            (VNRow, string) key, string[] displayOptions = null)
        {
            // 上一帧弹窗选的值：同步交还给调用方
            if (_popupResults.TryGetValue(key, out string picked))
            {
                _popupResults.Remove(key);
                _customEdit.Remove(key);
                GUI.changed = true;
                return picked;
            }

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

            int idx = System.Array.IndexOf(options, value);
            string label = string.IsNullOrEmpty(value) ? emptyLabel
                : (displayOptions != null && displayOptions.Length == options.Length &&
                   idx >= 0 ? displayOptions[idx] : value);
            if (GUI.Button(rect, label, EditorStyles.popup))
                ShowStringSearch(rect, options, emptyLabel, key, displayOptions);
            return value;
        }

        /// <summary>参数格的可搜下拉：选中值放进 _popupResults，下一帧由 PopupString 交还</summary>
        void ShowStringSearch(Rect rect, string[] options, string emptyLabel,
            (VNRow, string) key, string[] displayOptions)
        {
            var items = new List<VNSearchItem>
            {
                new VNSearchItem { value = "", title = emptyLabel, searchExtra = "清空 留空" },
            };
            for (int i = 0; i < options.Length; i++)
                items.Add(new VNSearchItem
                {
                    value = options[i],
                    title = displayOptions != null && displayOptions.Length == options.Length
                        ? displayOptions[i] : options[i],
                });

            PopupWindow.Show(rect, new VNSearchPopup(
                "打字筛选，或直接输入自定义值", items,
                item =>
                {
                    _popupResults[key] = item.value;
                    Repaint();
                },
                twoLine: false, allowFreeValue: true,
                onCustom: () =>
                {
                    _customEdit.Add(key);   // 切成常驻文本框（要反复改自由值时用）
                    Repaint();
                },
                width: 300f, height: 280f));
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

        VNRow NewSayRow() => new VNRow { kind = VNRowKind.Say };

        VNRow NewCommandRow(string keyword)
        {
            var r = new VNRow { kind = VNRowKind.Command };
            SetKeyword(r, keyword);
            if (r.camLines != null && r.camLines.Count == 0)
                r.camLines.Add(new VNCamWaypoint
                {
                    point = "middle", zoom = 1.4f, duration = 0.8f,
                }.Format());
            return r;
        }
    }
}
