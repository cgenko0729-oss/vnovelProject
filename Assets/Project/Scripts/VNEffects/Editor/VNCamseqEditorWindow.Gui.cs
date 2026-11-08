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
        // GUI
        // ==================================================================

        /// <summary>
        /// Ctrl+C 复制选中路径点（全部参数一模一样），Ctrl+V 贴到选中点后面并选中副本。
        /// 走 IMGUI 的 Copy/Paste 命令（Edit 菜单发给有焦点的窗口），而不是 Shortcut——
        /// Shortcut 会连文本框里的 Ctrl+C 一起抢走。**正在编辑文本框时整段让路**，
        /// 否则在 X/Y 格里选中数字想复制，复制走的却是整个路径点
        /// </summary>
        void HandleCopyPaste()
        {
            var e = Event.current;
            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand) return;
            if (EditorGUIUtility.editingTextField) return;

            bool hasSelection = _list != null && _list.index >= 0 && _list.index < _points.Count;
            if (e.commandName == "Copy" && hasSelection)
            {
                if (e.type == EventType.ExecuteCommand)
                {
                    s_copiedPoint = CloneWaypoint(_points[_list.index]);
                    ShowNotification(new GUIContent($"已复制路径点 {_list.index + 1}"));
                }
                e.Use();
            }
            else if (e.commandName == "Paste" && s_copiedPoint != null)
            {
                if (e.type == EventType.ExecuteCommand)
                {
                    // 每次贴都重新 Clone：贴两次得到的是两个独立的点，不是同一个引用
                    int at = hasSelection ? _list.index + 1 : _points.Count;
                    _points.Insert(at, CloneWaypoint(s_copiedPoint));
                    _list.index = at;
                    ShowNotification(new GUIContent($"已粘贴为路径点 {at + 1}"));
                    Repaint();
                }
                e.Use();
            }
        }

        // 路径点剪贴板：static 让同时开着的多个镜头窗口之间也能互相粘贴。
        // 不进 systemCopyBuffer——那是玩家文本剪贴板，被一个路径点覆盖掉会很莫名其妙
        static Waypoint s_copiedPoint;

        /// <summary>▶ 预览 / ■ 停止：从当前进度条位置播；已经播到尾就从头来</summary>
        void TogglePlay()
        {
            _playing = !_playing;
            if (_playing)
            {
                if (_scrub >= TotalDuration() - 0.001f) _scrub = 0f;
                _lastUpdateTime = EditorApplication.timeSinceStartup;
            }
            Repaint();
        }

        /// <summary>
        /// 空格 = 播放 / 停止。放在 OnGUI 最前面先吃掉：IMGUI 里空格也会「按下」
        /// 有键盘焦点的按钮 / 开关，不抢先的话会顺手点了刚才那个按钮。
        /// 正在编辑文本框（数字格、文本区）时让路，否则打不出空格
        /// </summary>
        void HandlePlayKey()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode != KeyCode.Space) return;
            if (EditorGUIUtility.editingTextField) return;
            if (e.control || e.alt || e.command) return;   // 组合键留给别人
            TogglePlay();
            e.Use();
        }

        void OnGUI()
        {
            HandlePlayKey();
            HandleCopyPaste();
            SyncLink();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawToolbar();
            DrawLinkBar();
            GUILayout.Space(4f);

            // 左看右改：左栏画布/进度条/时间轴，右栏路径点列表。
            // 窗口窄到右栏排不下时自动退回上下布局（挤成一团比滚动更难用）
            float avail = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 22f);
            if (avail >= SplitLayoutMinWidth)
            {
                float left = LeftColumnWidth(avail);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(left)))
                        DrawViewColumn(left);
                    DrawColumnSplitter(avail, CanvasPixelHeight(left) + LeftColumnExtra);
                    using (new EditorGUILayout.VerticalScope())
                        DrawEditColumn();
                }
            }
            else
            {
                DrawViewColumn(avail);
                GUILayout.Space(6f);
                DrawEditColumn();
            }
            GUILayout.Space(4f);

            // 开场 / 收尾叠化选项（对应 camseq 的 start: / end: 参数）
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(ZoomModeLabel, GUILayout.Width(58f));
                _zoomMode = (VNCamZoomMode)EditorGUILayout.Popup(
                    (int)_zoomMode, ZoomModeNames, GUILayout.Width(150f));
                GUILayout.Space(12f);
                GUILayout.Label("开场", GUILayout.Width(30f));
                _startMode = (StartMode)EditorGUILayout.Popup(
                    (int)_startMode, StartModeNames, GUILayout.Width(190f));
                if (_startMode == StartMode.Fade)
                {
                    GUILayout.Label("秒", GUILayout.Width(16f));
                    _startFade = Mathf.Max(0.05f,
                        EditorGUILayout.FloatField(_startFade, GUILayout.Width(40f)));
                }
                GUILayout.Space(12f);
                _endFade = GUILayout.Toggle(_endFade, "收尾叠化回全图", GUILayout.Width(108f));
                if (_endFade)
                {
                    GUILayout.Label("秒", GUILayout.Width(16f));
                    _endFadeDur = Mathf.Max(0.05f,
                        EditorGUILayout.FloatField(_endFadeDur, GUILayout.Width(40f)));
                }
                GUILayout.FlexibleSpace();
            }
            if (_startMode == StartMode.Cut && _points.Count > 0 && _points[0].duration > 0.001f)
                EditorGUILayout.HelpBox(
                    "start:cut 要求首个路径点时长为 0（瞬切），否则运行时按普通 camseq 执行",
                    MessageType.Warning);
            if (_zoomMode == VNCamZoomMode.Char)
                EditorGUILayout.HelpBox(
                    "mode:char 下背景纹丝不动，所以路径点的「目标点」不生效——只有 zoom 有用，" +
                    "它作用在立绘上（zoom 1.6 = 立绘放大到 1.6 倍）。\n" +
                    "想让镜头也动就换 both / depth。",
                    MessageType.Info);

            GUILayout.Space(4f);
            DrawTextSection();

            EditorGUILayout.EndScrollView();

            // 实时回写：放在最后，确保这一帧的所有编辑都已落到 _points 上
            if (_liveApply && HasLink) ApplyToLink();
        }

        /// <summary>
        /// 底部「文本与说明」：生成文本 / 解析文本 / 使用说明三块折进一个折叠。
        /// 三个多行文本框常年占掉半个窗口，而它们都是偶尔用一次的东西。
        /// 「生成文本 → 剪贴板」放在标题行右边，**收起状态也点得到**（最常用的出口）。
        /// </summary>
        void DrawTextSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool fold = EditorGUILayout.Foldout(_textFold,
                    "文本与说明（生成 / 解析 / 使用说明）", true);
                if (fold != _textFold)
                {
                    _textFold = fold;
                    EditorPrefs.SetBool(TextFoldPrefKey, fold);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("生成文本 → 剪贴板",
                        "按当前编排生成 camseq 文本并复制到剪贴板（绑定了剧本行时不用它，改动会自动回写）"),
                        EditorStyles.miniButton, GUILayout.Width(126f)))
                {
                    _generatedText = GenerateText();
                    EditorGUIUtility.systemCopyBuffer = _generatedText;
                    ShowNotification(new GUIContent("已复制到剪贴板"));
                }
            }
            if (!_textFold) return;

            GUILayout.Label("生成的剧本文本（粘贴进 .vn.txt）：", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(string.IsNullOrEmpty(_generatedText)
                ? "（点右上「生成文本 → 剪贴板」）" : _generatedText, GUILayout.MinHeight(70f));

            GUILayout.Space(6f);
            GUILayout.Label("解析已有 camseq 文本（粘贴后点「解析载入」）：", EditorStyles.boldLabel);
            _pasteText = EditorGUILayout.TextArea(_pasteText, GUILayout.MinHeight(60f));
            if (GUILayout.Button("解析载入"))
            {
                CommitUndo();
                ParseText();
            }

            EditorGUILayout.HelpBox(
                "画布：点取景框/序号牌 = 选中；**Ctrl + 拖 = 移动点位**（裸拖只选中，" +
                "防止把调好的取景推走）；拖选中框的四角 = 改 zoom。\n" +
                "布局：画布底边可上下拖改大小、左右分栏的竖条可拖改栏宽，双击任一条复位；" +
                "窗口拉窄到 900px 以下自动退回上下布局。\n" +
                "叠化：xfade>0 的点用「截屏→瞬切→淡出」代替平移；预览时白框瞬切到新视角、" +
                "橙色残框 = 正在淡出的旧视角。场景预览里叠化段表现为瞬切" +
                "（真实叠化由运行时截屏完成）。\n" +
                "开场 cut 只在剧本里紧跟带 transition 的 bg 时生效（首点时长须为 0）。\n" +
                "场景预览：开启后拖进度条/按 ▶，Game 视图实时显示真实画面运镜，" +
                "关闭或进出 Play 自动还原（场景可能显示未保存标记，属正常）。\n" +
                "捕获当前镜头：把场景里 ZoomRoot 的当前状态反推成一个路径点" +
                "（可先手动摆好 ZoomRoot 再捕获）。\n" +
                "编辑态下「角色部位」按假定站位显示，Play 中按真实位置。" +
                "缓动默认：单段 InOutSine；多段首 InSine / 中 Linear / 末 OutSine，" +
                "叠化段会把连续补间分成独立组（与运行时一致）。\n" +
                "绑定条：从剧本编辑器 camseq 行的「编排」按钮进来后，这里的改动可以直接回写那一行；" +
                "「跟随选中」时在剧本里点另一个 camseq 行会自动切过来。\n" +
                "hold：到达该点后停留的秒数（0 = 不停）。要「推到脸上停一秒再拉回」写 hold 就够了，" +
                "不用再补一个同点位、时长 0 的路径点。\n" +
                "震：到达该点的瞬间震一下屏幕（light/medium/heavy 或自定义「强度,秒数」）。" +
                "震完才走下一段——停顿取 max(hold, 震动时长)，不是相加。" +
                "预览时间轴会把这段停顿算进去，但不模拟抖动本身。\n" +
                "辅助线：整图模式画在选中路径点的取景框里，镜头视角模式铺满画布。" +
                "对话框遮挡区按场景里真实对话框的尺寸换算——特写时脸有没有被挡住看这条。\n" +
                "撤销：Ctrl+Z / Ctrl+Y（Ctrl+Shift+Z 也行），只作用于本窗口，不动 Unity 全局撤销；" +
                "拖滑条、连续输入会合并成一步；换绑定行会重开一段历史。",
                MessageType.Info);
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(new GUIContent(_playing ? "■ 停止" : "▶ 预览",
                            "播放 / 停止预览（空格）"),
                        EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    TogglePlay();

                using (new EditorGUI.DisabledScope(!CanUndo))
                    if (GUILayout.Button(new GUIContent("↶", "撤销（Ctrl+Z，只作用于本窗口）"),
                            EditorStyles.toolbarButton, GUILayout.Width(26f)))
                        PerformUndo();
                using (new EditorGUI.DisabledScope(!CanRedo))
                    if (GUILayout.Button(new GUIContent("↷", "重做（Ctrl+Y / Ctrl+Shift+Z）"),
                            EditorStyles.toolbarButton, GUILayout.Width(26f)))
                        PerformRedo();

                GUILayout.Space(8f);

                bool cameraView = GUILayout.Toggle(_cameraView,
                    new GUIContent(_cameraView ? "镜头视角" : "整图",
                        "整图 = 全景 + 取景框（可拖点编辑）\n" +
                        "镜头视角 = 画布直接显示镜头里看到的画面，拖进度条 / ▶ 就是运镜动画（只看不改）"),
                    EditorStyles.toolbarButton, GUILayout.Width(58f));
                if (cameraView != _cameraView) _cameraView = cameraView;

                var guidesRect = GUILayoutUtility.GetRect(
                    new GUIContent("辅助线 ▾"), EditorStyles.toolbarButton, GUILayout.Width(62f));
                var prevBg = GUI.backgroundColor;
                if (_guides != Guides.None) GUI.backgroundColor = new Color(0.45f, 0.85f, 1f);
                if (GUI.Button(guidesRect,
                        new GUIContent("辅助线 ▾",
                            "构图辅助线：三分线 / 中心十字 / 安全区 / 对话框遮挡区。\n" +
                            "整图模式画在选中路径点的取景框里，镜头视角模式铺满整个画布"),
                        EditorStyles.toolbarButton))
                    ShowGuidesMenu(guidesRect);
                GUI.backgroundColor = prevBg;

                bool newPreview = GUILayout.Toggle(_scenePreviewing,
                    new GUIContent("场景预览",
                        "把绑定行的背景/立绘摆进场景并接管 ZoomRoot，" +
                        "让 Game 视图显示带后处理的真实画面；关掉全部还原"),
                    EditorStyles.toolbarButton, GUILayout.Width(66f));
                if (newPreview != _scenePreviewing)
                {
                    if (newPreview) StartScenePreview();
                    else StopScenePreview();
                }

                if (GUILayout.Button(new GUIContent("捕获镜头",
                        "把场景里 ZoomRoot 的当前状态反推成一个路径点"),
                        EditorStyles.toolbarButton, GUILayout.Width(66f)))
                    CaptureCurrentCamera();

                var presetRect = GUILayoutUtility.GetRect(
                    new GUIContent("预设 ▾"), EditorStyles.toolbarButton, GUILayout.Width(56f));
                if (GUI.Button(presetRect,
                        new GUIContent("预设 ▾", "载入 / 存为 / 删除镜头预设（资产：" + LibraryPath + "）"),
                        EditorStyles.toolbarButton))
                    ShowPresetMenu(presetRect);

                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_points.Count} 个路径点", EditorStyles.miniLabel);
            }
        }


        // 单行布局的固定宽度：窗口拉窄时先压目标区，右边这几格永远在原位
        // （以前是两行 × N 个点，十来个点就把列表撑得比画布还高）
        const float NumWidth = 26f, TypeWidth = 66f, ZoomFieldWidth = 44f,
                    SecFieldWidth = 40f, MoreWidth = 46f;

        void DrawElement(Rect rect, int index, bool active, bool focused)
        {
            var w = _points[index];
            float line = EditorGUIUtility.singleLineHeight;
            float y = rect.y + 3f;
            bool stay = w.type == PointType.Stay;

            // 从右往左钉死固定块：⋯ → 秒 → zoom，剩下的宽度全归目标区
            float right = rect.xMax;

            var moreRect = new Rect(right - MoreWidth, y, MoreWidth, line);
            right -= MoreWidth + 4f;

            var secField = new Rect(right - SecFieldWidth, y, SecFieldWidth, line);
            var secLabel = new Rect(secField.x - 20f, y, 20f, line);
            right = secLabel.x - 4f;

            var zoomField = new Rect(right - ZoomFieldWidth, y, ZoomFieldWidth, line);
            right = zoomField.x;

            // 宽度富裕才补一条 zoom 滑条（窄窗口下它是第一个被牺牲的，数字框始终在）
            float sliderW = Mathf.Clamp(rect.width - 480f, 0f, 90f);
            var zoomSlider = new Rect(right - sliderW - 2f, y, sliderW, line);
            if (sliderW >= 40f) right = zoomSlider.x;

            var zoomLabel = new Rect(right - 34f, y, 34f, line);
            right = zoomLabel.x - 6f;

            // ---- 左半：编号 + 类型 + 目标 ----
            float x = rect.x;
            GUI.Label(new Rect(x, y, NumWidth, line), $"{index + 1}.");
            x += NumWidth;
            w.type = (PointType)EditorGUI.EnumPopup(new Rect(x, y, TypeWidth, line), w.type);
            x += TypeWidth + 4f;

            float remain = Mathf.Max(40f, right - x);
            switch (w.type)
            {
                case PointType.Stay:
                    GUI.Label(new Rect(x, y, remain, line),
                        new GUIContent("沿用上一个点（位置与 zoom 都不变）",
                            "原地：镜头一动不动，专门用来在序列中间插一段震动或停顿。\n" +
                            "画布上按连续第几个 stay 往里缩一圈画虚线框，不与上一个点抢位置。"),
                        EditorStyles.miniLabel);
                    break;

                case PointType.Anchor:
                    w.anchorIndex = EditorGUI.Popup(
                        new Rect(x, y, Mathf.Min(remain, 130f), line), w.anchorIndex, AnchorTokens);
                    break;

                case PointType.Character:
                {
                    // 角色 id 最长，部位/站位是短词：宽裕时按 剩余/62/58 分，
                    // 挤到放不下就退回三等分（宁可都窄一点，也不让站位被压成一个箭头）
                    float partW = 62f, slotW = 58f;
                    float idW = remain - partW - slotW - 8f;
                    if (idW < 70f) { idW = partW = slotW = (remain - 8f) / 3f; }

                    var ids = CharacterIds();
                    var idRect = new Rect(x, y, idW, line);
                    if (ids.Length > 0)
                    {
                        int cur = System.Array.IndexOf(ids, w.charId);
                        int sel = EditorGUI.Popup(idRect, Mathf.Max(0, cur), ids);
                        w.charId = ids[sel];
                    }
                    else
                    {
                        w.charId = EditorGUI.TextField(idRect, w.charId);
                    }
                    w.partIndex = EditorGUI.Popup(
                        new Rect(x + idW + 4f, y, partW, line), w.partIndex, PartTokens);
                    w.slotIndex = EditorGUI.Popup(
                        new Rect(x + idW + partW + 8f, y, slotW, line), w.slotIndex, SlotNames);
                    break;
                }

                case PointType.Coords:
                    w.coords = EditorGUI.Vector2Field(
                        new Rect(x, y, Mathf.Min(remain, 200f), line), GUIContent.none, w.coords);
                    break;
            }

            // ---- 右半：zoom / 秒 / ⋯ ----
            GUI.Label(zoomLabel, new GUIContent("zoom", stay
                ? "原地点没有自己的 zoom，沿用上一个点" : "取景倍率：1 = 全图，越大越推近"));
            if (stay)
            {
                // 禁用占位而不是留空：切换点位类型时右边这几格不会左右横跳
                using (new EditorGUI.DisabledScope(true))
                {
                    if (sliderW >= 40f) EditorGUI.LabelField(zoomSlider, "沿用", EditorStyles.miniLabel);
                    EditorGUI.LabelField(zoomField, "—", EditorStyles.miniLabel);
                }
            }
            else
            {
                // 滑条只覆盖常用区间 MinZoom~MaxZoom，数字框不设这个上限（运行时本来就不限）。
                // 但 HorizontalSlider 会把传进去的值 clamp 后原样返回，直接写回就会
                // 把手敲的超限值拉回上限 —— 所以只在真的拖动了滑条时才采纳它的值。
                if (sliderW >= 40f)
                {
                    EditorGUI.BeginChangeCheck();
                    float dragged = GUI.HorizontalSlider(
                        new Rect(zoomSlider.x, zoomSlider.y + 4f, zoomSlider.width, zoomSlider.height),
                        Mathf.Clamp(w.zoom, MinZoom, MaxZoom), MinZoom, MaxZoom);
                    if (EditorGUI.EndChangeCheck()) w.zoom = dragged;
                }
                w.zoom = Mathf.Clamp(EditorGUI.FloatField(zoomField, w.zoom), 0.1f, 10f);
            }

            GUI.Label(secLabel, new GUIContent("秒", "移动到本点的时长；0 = 瞬切"));
            w.duration = Mathf.Max(0f, EditorGUI.FloatField(secField, w.duration));

            DrawMoreButton(moreRect, w, index);
        }

        /// <summary>
        /// 「⋯」按钮：ease / xfade / hold / 震 四项折进弹出面板。
        /// **按钮上必须看得出"设过没有"**——折起来的参数是看不见的，四个点长一样
        /// 就等于把这四项藏没了，而 xfade 与 hold 会直接改变整段运镜的时间轴长度。
        /// 所以设过的项在按钮上留一个字母标记（E/X/H/震），并把按钮染色。
        /// </summary>
        void DrawMoreButton(Rect rect, Waypoint w, int index)
        {
            var tags = new StringBuilder();
            var tip = new StringBuilder("缓动 / 叠化 / 停留 / 震屏（点开编辑）");
            if (w.easeIndex > 0)
            {
                tags.Append('E');
                tip.Append("\nease：").Append(EaseNames[w.easeIndex]);
            }
            if (w.fade > 0.0001f)
            {
                tags.Append('X');
                tip.Append("\nxfade：").Append(w.fade.ToString("0.##")).Append(" 秒（叠化代替平移）");
            }
            if (w.hold > 0.0001f)
            {
                tags.Append('H');
                tip.Append("\nhold：").Append(w.hold.ToString("0.##")).Append(" 秒停留");
            }
            if (!string.IsNullOrEmpty(w.shake))
            {
                tags.Append('震');
                tip.Append("\nshake：").Append(w.shake);
            }

            bool any = tags.Length > 0;
            var prev = GUI.backgroundColor;
            if (any) GUI.backgroundColor = new Color(0.5f, 0.85f, 1f);
            if (GUI.Button(rect, new GUIContent(any ? tags.ToString() : "⋯", tip.ToString()),
                    EditorStyles.miniButton))
                PopupWindow.Show(rect, new WaypointDetailPopup(this, w, index));
            GUI.backgroundColor = prev;
        }

        /// <summary>路径点的次要参数面板（zoom 滑条 + ease / xfade / hold / 震）</summary>
        class WaypointDetailPopup : PopupWindowContent
        {
            readonly VNCamseqEditorWindow _owner;
            readonly Waypoint _wp;
            readonly int _index;

            public WaypointDetailPopup(VNCamseqEditorWindow owner, Waypoint wp, int index)
            {
                _owner = owner;
                _wp = wp;
                _index = index;
            }

            public override Vector2 GetWindowSize() => new Vector2(336f, 172f);

            public override void OnGUI(Rect rect)
            {
                bool stay = _wp.type == PointType.Stay;
                // labelWidth 是全局的，改完必须还原，否则主窗口的字段标签跟着一起变宽
                float prevLabel = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 88f;

                using (new GUILayout.AreaScope(
                    new Rect(8f, 6f, rect.width - 16f, rect.height - 12f)))
                {
                    GUILayout.Label($"第 {_index + 1} 个路径点", EditorStyles.boldLabel);

                    // 同行内滑条：拖了才写回，免得把手敲的超范围 zoom 悄悄拉回 3
                    using (new EditorGUI.DisabledScope(stay))
                    {
                        EditorGUI.BeginChangeCheck();
                        float dragged = EditorGUILayout.Slider(
                            new GUIContent("zoom", "取景倍率：1 = 全图，越大越推近"),
                            Mathf.Clamp(_wp.zoom, MinZoom, MaxZoom), MinZoom, MaxZoom);
                        if (EditorGUI.EndChangeCheck()) _wp.zoom = dragged;
                    }

                    _wp.easeIndex = EditorGUILayout.Popup(
                        new GUIContent("缓动 ease",
                            "留「(默认)」则按组自动分配：单段 InOutSine；" +
                            "多段首 InSine / 中 Linear / 末 OutSine"),
                        _wp.easeIndex, EaseNames);

                    _wp.fade = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent("叠化 xfade", "叠化到本点的秒数（>0 时代替平移/瞬切）"),
                        _wp.fade));

                    _wp.hold = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent("停留 hold", "到达本点后停留的秒数（0 = 不停，直接走下一段）"),
                        _wp.hold));

                    var shakeRect = EditorGUILayout.GetControlRect();
                    EditorGUI.LabelField(
                        new Rect(shakeRect.x, shakeRect.y, EditorGUIUtility.labelWidth, shakeRect.height),
                        new GUIContent("震屏 shake", VNCamShakeUi.Tooltip));
                    float sx = shakeRect.x + EditorGUIUtility.labelWidth + 2f;
                    _wp.shake = VNCamShakeUi.Draw(
                        new Rect(sx, shakeRect.y, shakeRect.xMax - sx, shakeRect.height), _wp.shake);

                    GUILayout.Space(4f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(new GUIContent("清掉这四项",
                                "把 ease / xfade / hold / 震 还原成默认（zoom 不动）"),
                                EditorStyles.miniButton, GUILayout.Width(92f)))
                        {
                            _wp.easeIndex = 0;
                            _wp.fade = 0f;
                            _wp.hold = 0f;
                            _wp.shake = "";
                            GUI.changed = true;
                        }
                    }
                }

                EditorGUIUtility.labelWidth = prevLabel;

                // 面板是独立窗口：改完要叫主窗口重画，画布取景框、预览总时长
                // 与 OnGUI 末尾的实时回写全都在那边
                if (GUI.changed) _owner.Repaint();
            }
        }


        // ==================================================================
        // 两栏布局
        // ==================================================================

        /// <summary>左栏：画布 + 进度条 + 时间轴（全是「看」的东西，编点位时它们要一直在眼前）</summary>
        void DrawViewColumn(float width)
        {
            DrawCanvasArea(width);
            GUILayout.Space(2f);

            float total = TotalDuration();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"预览 {_scrub:0.00}s / {total:0.00}s", GUILayout.Width(140f));
                float newScrub = GUILayout.HorizontalSlider(_scrub, 0f, Mathf.Max(0.01f, total));
                if (!Mathf.Approximately(newScrub, _scrub))
                {
                    _scrub = newScrub;
                    _playing = false;
                }
            }

            DrawTimeline();
        }

        /// <summary>右栏：路径点列表 + 它的按钮行</summary>
        void DrawEditColumn()
        {
            _list.DoLayoutList();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 瞬切起手（时长0）"))
                    _points.Insert(0, new Waypoint
                        { type = PointType.Anchor, anchorIndex = 2, zoom = 1.8f, duration = 0f });
                if (GUILayout.Button("+ 回原点收尾"))
                    _points.Add(new Waypoint
                        { type = PointType.Anchor, anchorIndex = 4, zoom = 1f, duration = 1f });
                var templateRect = GUILayoutUtility.GetRect(
                    new GUIContent("内置模板 ▾"), GUI.skin.button);
                if (GUI.Button(templateRect, "内置模板 ▾"))
                    ShowTemplateMenu(templateRect);
            }
        }

        float LeftColumnWidth(float avail)
        {
            float wanted = _leftColumnWidth > 0f ? _leftColumnWidth : avail * 0.58f;
            return Mathf.Clamp(wanted, LeftColumnMin,
                Mathf.Max(LeftColumnMin, avail - RightColumnMin - SplitterHeight));
        }

        /// <summary>
        /// 左右栏之间的竖条。高度按左栏内容算死，**绝不能用 ExpandHeight** ——
        /// 那会把整个 HorizontalScope 撑到滚动区的全部高度，
        /// 底下的开场/收尾与折叠区被顶到窗口最下面，中间空出一大片。
        /// </summary>
        void DrawColumnSplitter(float avail, float height)
        {
            var r = GUILayoutUtility.GetRect(SplitterHeight, height,
                GUILayout.Width(SplitterHeight), GUILayout.Height(height));
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.18f));
            EditorGUI.DrawRect(new Rect(r.center.x - 1f, r.y + r.height * 0.5f - 14f, 2f, 28f),
                new Color(1f, 1f, 1f, _draggingColumnSplit ? 0.75f : 0.32f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            bool inside = r.Contains(e.mousePosition);

            // 双击复位。必须排在 MouseDown 之前，否则第一下就被 Use 掉，clickCount 到不了 2
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && inside)
            {
                _leftColumnWidth = 0f;
                EditorPrefs.SetFloat(ColumnWidthPrefKey, 0f);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && inside)
            {
                _draggingColumnSplit = true;
                _splitStartX = e.mousePosition.x;
                _splitStartColumnWidth = LeftColumnWidth(avail);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingColumnSplit)
            {
                _leftColumnWidth = Mathf.Clamp(
                    _splitStartColumnWidth + (e.mousePosition.x - _splitStartX),
                    LeftColumnMin, Mathf.Max(LeftColumnMin, avail - RightColumnMin - SplitterHeight));
                e.Use();
                Repaint();
            }
            else if (_draggingColumnSplit &&
                     (e.type == EventType.MouseUp || e.type == EventType.Ignore))
            {
                _draggingColumnSplit = false;
                EditorPrefs.SetFloat(ColumnWidthPrefKey, _leftColumnWidth);
                e.Use();
                Repaint();
            }
        }
    }
}
