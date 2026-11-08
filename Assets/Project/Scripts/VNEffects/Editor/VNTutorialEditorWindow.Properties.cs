using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public partial class VNTutorialEditorWindow
    {
        // ------------------------------------------------------------------
        // 右：属性
        // ------------------------------------------------------------------

        void DrawProperties(Rect rect)
        {
            GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f));
            _propScroll = EditorGUILayout.BeginScrollView(_propScroll);

            DrawPlayModeBlock();
            EditorGUILayout.Space(6f);

            _wholeFold = EditorGUILayout.Foldout(_wholeFold, "整篇设置", true);
            if (_wholeFold) DrawWholeSettings();
            EditorGUILayout.Space(6f);

            var step = Current;
            if (step == null)
            {
                EditorGUILayout.HelpBox("左侧选一步，或点「+ 新步骤」。", MessageType.None);
            }
            else
            {
                EditorGUILayout.LabelField($"第 {_selected + 1} 步", EditorStyles.boldLabel);
                DrawStepTarget(step);
                EditorGUILayout.Space(4f);
                DrawStepLook(step);
                EditorGUILayout.Space(4f);
                DrawStepText(step);
                EditorGUILayout.Space(4f);
                DrawStepExtras(step);
            }

            EditorGUILayout.Space(10f);
            DrawTriggerHints();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawWholeSettings()
        {
            EditorGUI.BeginChangeCheck();
            string id = EditorGUILayout.TextField("id（剧本引用名）", _def.id);
            float dim = EditorGUILayout.Slider("暗幕浓度", _def.dim, 0f, 1f);
            Color edge = EditorGUILayout.ColorField(new GUIContent("洞口描边（HDR）"), _def.edgeColor, true, true, true);
            float edgeWidth = EditorGUILayout.FloatField("描边宽度（px）", _def.edgeWidth);
            float pulse = EditorGUILayout.Slider("描边呼吸", _def.edgePulse, 0f, 1f);
            bool allowSkip = EditorGUILayout.Toggle("允许 ESC 跳过", _def.allowSkip);
            bool once = EditorGUILayout.Toggle("看过一次就不再播", _def.once);
            float cardW = EditorGUILayout.FloatField(new GUIContent("卡片默认宽度（px）", "0 = 播放器组件的 cardWidth（780）/ 皮肤 prefab 自带宽度；每步可单独覆盖"), _def.cardWidth);
            float cardS = EditorGUILayout.Slider(new GUIContent("卡片默认缩放", "字号·边距·配图一起等比变；每步可单独覆盖"), _def.cardScale <= 0f ? 1f : _def.cardScale, 0.5f, 2.5f);
            if (EditorGUI.EndChangeCheck())
            {
                Snapshot();
                _def.id = id;
                _def.dim = dim;
                _def.edgeColor = edge;
                _def.edgeWidth = edgeWidth;
                _def.edgePulse = pulse;
                _def.allowSkip = allowSkip;
                _def.once = once;
                _def.cardWidth = Mathf.Max(0f, cardW);
                _def.cardScale = cardS;
                Save();
            }
        }

        void DrawStepTarget(VNTutorialStep step)
        {
            EditorGUILayout.LabelField("高亮目标", EditorStyles.boldLabel);
            // 模式由数据推出来（锚点非空 → 锚点；有矩形 → 矩形；否则整屏）。
            // 「锚点」但还没填 id 的过渡态用 _anchorModePending 撑着，不往资产里写占位符。
            int mode = !string.IsNullOrEmpty(step.anchor) ? 1
                : _anchorModePending == step ? 1
                : step.HasArea ? 2 : 0;
            int newMode = EditorGUILayout.Popup("方式", mode, TargetModes);
            if (newMode != mode)
            {
                Snapshot();
                _anchorModePending = null;
                switch (newMode)
                {
                    case 0:
                        step.anchor = "";
                        step.area = new Rect(0f, 0f, 0f, 0f);
                        break;
                    case 1:
                        step.anchor = "";
                        step.area = new Rect(0f, 0f, 0f, 0f);
                        _anchorModePending = step;
                        break;
                    case 2:
                        step.anchor = "";
                        if (!step.HasArea) step.area = new Rect(0.4f, 0.45f, 0.2f, 0.1f);
                        break;
                }
                Save();
            }

            if (newMode == 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string anchor = EditorGUILayout.TextField("锚点 id", step.anchor.Trim());
                    if (EditorGUI.EndChangeCheck())
                    {
                        Snapshot();
                        step.anchor = anchor;
                        Save();
                    }
                    if (GUILayout.Button("▾", GUILayout.Width(22f))) ShowAnchorMenu(step);
                }

                string trimmed = step.anchor.Trim();
                var live = VNTutorialAnchorCatalog.LiveIds();
                if (string.IsNullOrEmpty(trimmed))
                    EditorGUILayout.HelpBox("从 ▾ 目录里选一个锚点，或 Play Mode 下 Ctrl+点 Game 视图拾取。", MessageType.Info);
                else if (!VNTutorialAnchorCatalog.Contains(trimmed) && !live.Contains(trimmed))
                    EditorGUILayout.HelpBox($"目录里没有「{trimmed}」：可能拼错，或对应模块还没登记这个锚点。", MessageType.Warning);
                else if (EditorApplication.isPlaying && !live.Contains(trimmed))
                    EditorGUILayout.HelpBox("这个锚点现在没登记（那个界面还没打开），预览会只有整屏压暗。", MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (EditorApplication.isPlaying && live.Contains(trimmed) &&
                        GUILayout.Button("在 Game 视图里闪一下", GUILayout.Width(150f)))
                        VNTutorialPicker.Ensure()?.Flash(VNTutorialAnchors.Get(trimmed));
                }

                EditorGUI.BeginChangeCheck();
                bool useFallback = step.HasArea;
                bool nf = EditorGUILayout.ToggleLeft("锚点找不到时用兜底矩形", useFallback);
                if (EditorGUI.EndChangeCheck())
                {
                    Snapshot();
                    step.area = nf ? new Rect(0.4f, 0.45f, 0.2f, 0.1f) : new Rect(0f, 0f, 0f, 0f);
                    Save();
                }
                if (step.HasArea)
                {
                    EditorGUI.BeginChangeCheck();
                    var r = EditorGUILayout.RectField("兜底矩形", step.area);
                    if (EditorGUI.EndChangeCheck()) { Snapshot(); step.area = r; Save(); }
                }
            }
            else if (newMode == 2)
            {
                EditorGUI.BeginChangeCheck();
                var r = EditorGUILayout.RectField("矩形（归一化，左下原点）", step.area);
                if (EditorGUI.EndChangeCheck()) { Snapshot(); step.area = r; Save(); }
                EditorGUILayout.LabelField("画布上：拖框体移动、拖右下角改大小、在空处拖出新框", EditorStyles.miniLabel);
            }
        }

        void ShowAnchorMenu(VNTutorialStep step)
        {
            var menu = new GenericMenu();
            var live = VNTutorialAnchorCatalog.LiveIds();
            var listed = new HashSet<string>();
            foreach (var entry in VNTutorialAnchorCatalog.Entries)
            {
                listed.Add(entry.id);
                string mark = live.Contains(entry.id) ? "● " : "";
                string id = entry.id;
                menu.AddItem(new GUIContent($"{entry.owner}/{mark}{id}"), step.anchor.Trim() == id,
                    () => { Snapshot(); step.anchor = id; Save(); });
            }
            // 运行时登记了但目录里没有的（比如皮肤 prefab 上挂的 VNTutorialAnchor）
            bool extra = false;
            foreach (var id in live)
            {
                if (listed.Contains(id)) continue;
                if (!extra) { menu.AddSeparator(""); extra = true; }
                string captured = id;
                menu.AddItem(new GUIContent($"当前登记（未在目录）/● {id}"), step.anchor.Trim() == id,
                    () => { Snapshot(); step.anchor = captured; Save(); });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("重扫目录"), false, VNTutorialAnchorCatalog.Invalidate);
            menu.ShowAsContext();
        }

        void DrawStepLook(VNTutorialStep step)
        {
            EditorGUILayout.LabelField("洞口外观", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var shape = (VNTutorialHole)EditorGUILayout.EnumPopup("形状", step.shape);
            float padding = EditorGUILayout.FloatField("外扩边距（px）", step.padding);
            float corner = EditorGUILayout.FloatField("圆角半径（px）", step.corner);
            float feather = EditorGUILayout.FloatField("边缘羽化（px）", step.feather);
            if (EditorGUI.EndChangeCheck())
            {
                Snapshot();
                step.shape = shape;
                step.padding = padding;
                step.corner = corner;
                step.feather = feather;
                Save();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("把边距/圆角/羽化套用到全部步骤", GUILayout.Width(210f)))
                {
                    Snapshot();
                    foreach (var s in _def.steps)
                    {
                        if (s == null || s == step) continue;
                        s.padding = step.padding;
                        s.corner = step.corner;
                        s.feather = step.feather;
                    }
                    Save();
                    ShowNotification(new GUIContent("已套用"));
                }
            }
        }

        void DrawStepText(VNTutorialStep step)
        {
            EditorGUILayout.LabelField($"文字（{LangNames[_lang]}，工具栏切换语言）", EditorStyles.boldLabel);
            string title = _lang == 0 ? step.title : _lang == 1 ? step.titleEn : step.titleJa;
            string body = _lang == 0 ? step.body : _lang == 1 ? step.bodyEn : step.bodyJa;

            EditorGUI.BeginChangeCheck();
            string newTitle = EditorGUILayout.TextField("标题（可空）", title ?? "");
            EditorGUILayout.LabelField("正文");
            string newBody = EditorGUILayout.TextArea(body ?? "", GUILayout.MinHeight(70f));
            if (EditorGUI.EndChangeCheck())
            {
                Snapshot();
                switch (_lang)
                {
                    case 0: step.title = newTitle; step.body = newBody; break;
                    case 1: step.titleEn = newTitle; step.bodyEn = newBody; break;
                    default: step.titleJa = newTitle; step.bodyJa = newBody; break;
                }
                Save();
            }
            if (_lang != 0 && string.IsNullOrEmpty(newTitle) && string.IsNullOrEmpty(newBody))
                EditorGUILayout.LabelField("留空 = 运行时回退中文", EditorStyles.miniLabel);
        }

        void DrawStepExtras(VNTutorialStep step)
        {
            EditorGUILayout.LabelField("其它", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var image = (Sprite)EditorGUILayout.ObjectField("配图", step.image, typeof(Sprite), false);
            float imageH = step.imageHeight;
            if (image != null) imageH = EditorGUILayout.FloatField("配图高度（px）", step.imageHeight);
            var card = (VNTutorialCardSpot)EditorGUILayout.EnumPopup("卡片位置", step.card);
            Vector2 cardPos = step.cardPos;
            if (card == VNTutorialCardSpot.Custom)
            {
                cardPos = EditorGUILayout.Vector2Field("卡片中心（归一化，左下原点）", step.cardPos);
                cardPos = new Vector2(Mathf.Clamp01(cardPos.x), Mathf.Clamp01(cardPos.y));
                EditorGUILayout.HelpBox("画布上直接拖卡片也行（任何落位模式下拖一下就变成 Custom）。" +
                                        "(0.5,0.5) = 屏幕正中；出屏部分运行时会自动钳回来。", MessageType.None);
            }
            float stepW = EditorGUILayout.FloatField(new GUIContent("卡片宽度覆盖（px）",
                $"0 = 用整篇默认（当前 {(_def.ResolveCardWidth(null) > 0f ? _def.ResolveCardWidth(null).ToString("0") : "780")}px）；画布上拖卡片右下角手柄也能改"), step.cardWidth);
            float stepS = EditorGUILayout.FloatField(new GUIContent("卡片缩放覆盖",
                $"0 = 用整篇默认（当前 {_def.ResolveCardScale(null):0.00}）；Shift+拖右下角手柄也能改"), step.cardScale);
            string se = EditorGUILayout.TextField("音效 id（SE 库）", step.se ?? "");
            if (EditorGUI.EndChangeCheck())
            {
                Snapshot();
                step.image = image;
                step.imageHeight = imageH;
                if (card == VNTutorialCardSpot.Custom && step.card != VNTutorialCardSpot.Custom)
                    cardPos = GuiToNorm(_lastArt, _cardMock.center);   // 从当前画出来的位置起步，别跳到正中
                step.card = card;
                step.cardPos = cardPos;
                step.cardWidth = Mathf.Max(0f, stepW);
                step.cardScale = stepS <= 0f ? 0f : Mathf.Clamp(stepS, 0.5f, 2.5f);
                step.se = se;
                Save();
            }
        }

        void DrawTriggerHints()
        {
            EditorGUILayout.LabelField("怎么触发", EditorStyles.boldLabel);
            string id = DisplayId(_def);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel($"tutorial {id}", EditorStyles.textField, GUILayout.Height(18f));
                if (GUILayout.Button("复制", GUILayout.Width(44f)))
                {
                    EditorGUIUtility.systemCopyBuffer = $"tutorial {id}";
                    ShowNotification(new GUIContent("已复制"));
                }
            }
            EditorGUILayout.LabelField($"强制重看：tutorial {id} force:on", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"模块首次自动播：event <模块> … tutorial:{id}", EditorStyles.miniLabel);
            var cfg = VNGameConfig.Active;
            if (cfg != null && (cfg.tutorials == null || !cfg.tutorials.Contains(_def)))
            {
                EditorGUILayout.HelpBox("这篇还没登记进 VNGameConfig 的教程库，运行时找不到。", MessageType.Warning);
                if (GUILayout.Button("登记进教程库"))
                {
                    cfg.tutorials.Add(_def);
                    EditorUtility.SetDirty(cfg);
                    VNAssetLibraryEvents.RaiseChanged();
                }
            }
        }

        // ------------------------------------------------------------------
        // Play Mode 区块：拾取 / 真机预览 / 实时锚点清单
        // ------------------------------------------------------------------

        void DrawPlayModeBlock()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "进 Play Mode 后这里会多出：Ctrl+点 Game 视图拾取目标、真机预览停在某一步、" +
                    "实时锚点清单（悬停在 Game 视图里闪一下）。",
                    MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Play Mode", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool live = GUILayout.Toggle(_livePreview, _livePreview ? "■ 停止真机预览" : "▶ 真机预览（停在当前步）",
                    "Button", GUILayout.Height(24f));
                if (live != _livePreview)
                {
                    _livePreview = live;
                    if (!live) StopPreview();
                    else _previewDirty = true;
                }
                using (new EditorGUI.DisabledScope(_def.steps.Count == 0))
                {
                    if (GUILayout.Button("◀", GUILayout.Width(28f), GUILayout.Height(24f)))
                        _selected = Mathf.Max(0, _selected - 1);
                    if (GUILayout.Button("▶", GUILayout.Width(28f), GUILayout.Height(24f)))
                        _selected = Mathf.Min(_def.steps.Count - 1, _selected + 1);
                }
            }
            if (GUILayout.Button("从头完整播一遍（force，按真实流程）"))
            {
                var player = VNTutorialPlayer.Instance;
                if (player != null)
                {
                    _livePreview = false;
                    StopPreview();
                    player.StartCoroutine(player.PlayCo(_def, true));
                }
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool armed = GUILayout.Toggle(_pickArmed, _pickArmed ? "◎ 拾取中：Ctrl+左键点 Game 视图" : "◎ 拾取目标（Ctrl+左键）",
                    "Button", GUILayout.Height(24f));
                if (armed != _pickArmed)
                {
                    _pickArmed = armed;
                    if (!armed) Disarm();
                }
                using (new EditorGUI.DisabledScope(_picker == null || _picker.LastRect == null || Current == null))
                    if (GUILayout.Button("↑ 父级", GUILayout.Width(56f), GUILayout.Height(24f)))
                        _picker.PickParent();
            }
            if (!string.IsNullOrEmpty(_lastPickInfo))
                EditorGUILayout.LabelField(_lastPickInfo, EditorStyles.wordWrappedMiniLabel);
            if (_pickArmed && !_livePreview)
                EditorGUILayout.LabelField("提示：先开真机预览游戏才会冻住，否则 Ctrl+点击也会被游戏当普通点击", EditorStyles.wordWrappedMiniLabel);

            // ---- 实时锚点清单 ----
            var live2 = new List<string>(VNTutorialAnchors.Ids);
            live2.Sort(string.CompareOrdinal);
            _liveListFold = EditorGUILayout.Foldout(_liveListFold, $"当前已登记的锚点（{live2.Count}）", true);
            if (_liveListFold)
            {
                if (live2.Count == 0)
                    EditorGUILayout.LabelField("（还没有：先在游戏里打开要讲的界面 / 进那个小游戏）", EditorStyles.miniLabel);
                bool hoveringAny = false;
                foreach (var id in live2)
                {
                    var row = GUILayoutUtility.GetRect(10f, 18f, GUILayout.ExpandWidth(true));
                    bool hover = row.Contains(Event.current.mousePosition);
                    if (hover)
                    {
                        hoveringAny = true;
                        EditorGUI.DrawRect(row, new Color(1f, 0.9f, 0.3f, 0.15f));
                        if (_hoverAnchor != id)
                        {
                            _hoverAnchor = id;
                            VNTutorialPicker.Ensure()?.Flash(VNTutorialAnchors.Get(id));
                        }
                    }
                    GUI.Label(row, "● " + id, EditorStyles.miniLabel);
                    if (Event.current.type == EventType.MouseDown && hover && Current != null)
                    {
                        Snapshot();
                        Current.anchor = id;
                        Save();
                        Event.current.Use();
                    }
                }
                if (!hoveringAny) _hoverAnchor = null;
                if (Event.current.type == EventType.MouseMove) Repaint();
            }
        }

        void SyncPreview()
        {
            var player = VNTutorialPlayer.Instance;
            if (player == null) return;
            if (_def == null || _selected < 0 || _selected >= _def.steps.Count)
            {
                if (player.IsEditorPreview) player.EditorPreviewEnd();
                return;
            }
            if (!player.IsEditorPreview || _previewDirty || _lastPreviewIndex != _selected)
            {
                player.EditorPreviewApply(_def, _selected);
                _previewDirty = false;
                _lastPreviewIndex = _selected;
            }
        }

        void StopPreview()
        {
            if (!EditorApplication.isPlaying) return;
            var player = VNTutorialPlayer.Instance;
            if (player != null && player.IsEditorPreview) player.EditorPreviewEnd();
            _lastPreviewIndex = -1;
        }

        void Disarm()
        {
            if (_picker != null)
            {
                _picker.Armed = false;
                _picker.Picked -= OnPicked;
            }
            _picker = null;
        }

        void OnPicked(VNTutorialPicker.PickResult result)
        {
            var step = Current;
            if (step == null) return;
            if (result.rect == null)
            {
                _lastPickInfo = "点的地方没有 UI 控件";
                Repaint();
                return;
            }
            Snapshot();
            if (!string.IsNullOrEmpty(result.anchor))
            {
                step.anchor = result.anchor;
                _lastPickInfo = $"拾取：锚点 {result.anchor}\n{result.path}";
            }
            else
            {
                step.anchor = "";
                step.area = result.area;
                _lastPickInfo = $"拾取：没登记锚点，已写成矩形 {result.area.x:0.00},{result.area.y:0.00} " +
                                $"{result.area.width:0.00}×{result.area.height:0.00}\n{result.path}";
            }
            Save();
            ShowNotification(new GUIContent("已拾取"));
        }

        // ------------------------------------------------------------------

        void DrawStatus(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.15f));
            string backdrop = string.IsNullOrEmpty(_backdropPath) ? "无底图" : "底图：" + Path.GetFileName(_backdropPath);
            string text = $"{AssetDatabase.GetAssetPath(_def)}   ·   {backdrop}   ·   Ctrl+Z/Y 撤销重做（本窗口）  Ctrl+S 保存  Ctrl+D 复制步骤";
            GUI.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, rect.height), text, EditorStyles.miniLabel);
        }

        void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            bool ctrl = e.control || e.command;
            if (ctrl && e.keyCode == KeyCode.Z) { PerformUndo(); e.Use(); }
            else if (ctrl && e.keyCode == KeyCode.Y) { PerformRedo(); e.Use(); }
            else if (ctrl && e.keyCode == KeyCode.S)
            {
                AssetDatabase.SaveAssets();
                ShowNotification(new GUIContent("已保存"));
                e.Use();
            }
            else if (ctrl && e.keyCode == KeyCode.D && !EditorGUIUtility.editingTextField)
            {
                DuplicateStep();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Delete && !EditorGUIUtility.editingTextField && Current != null)
            {
                DeleteStep(_selected);
                e.Use();
            }
        }
    }
}
