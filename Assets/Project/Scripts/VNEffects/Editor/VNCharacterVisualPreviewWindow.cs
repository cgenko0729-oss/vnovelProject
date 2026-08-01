using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>预览窗口的临时校准草稿；HideAndDontSave，确认前不会触碰角色资产。</summary>
    internal sealed class VNCharacterPreviewDraft : ScriptableObject
    {
        public float sizeScale = 1f;
        public Vector2 positionOffset;
        public bool enableBlink;
        public Sprite blinkSprite;
        public float blinkIntervalMin = 2.5f;
        public float blinkIntervalMax = 5f;
        public float blinkDuration = 0.1f;
        public bool enableMouthFlap;
        public Sprite openMouthSprite;
        public bool mouthDefaultExpressionOnly = true;
        public float mouthIntervalMin = 0.08f;
        public float mouthIntervalMax = 0.16f;
        public bool showPortrait = true;
        public float portraitScale = 1f;
        public Vector2 portraitOffset;

        public void ReadFrom(VNCharacterDef character)
        {
            sizeScale = character.sizeScale;
            positionOffset = character.positionOffset;
            enableBlink = character.enableBlink;
            blinkSprite = character.blinkSprite;
            blinkIntervalMin = character.blinkIntervalMin;
            blinkIntervalMax = character.blinkIntervalMax;
            blinkDuration = character.blinkDuration;
            enableMouthFlap = character.enableMouthFlap;
            openMouthSprite = character.openMouthSprite;
            mouthDefaultExpressionOnly = character.mouthDefaultExpressionOnly;
            mouthIntervalMin = character.mouthIntervalMin;
            mouthIntervalMax = character.mouthIntervalMax;
            showPortrait = character.showPortrait;
            portraitScale = character.portraitScale;
            portraitOffset = character.portraitOffset;
        }
    }

    /// <summary>
    /// 角色视觉校准窗口：不进入 Play Mode，也能按运行时公式预览并调整
    /// VNCharacterDef 的立绘尺寸/站位偏移与对话头像缩放/偏移。
    /// </summary>
    public class VNCharacterVisualPreviewWindow : EditorWindow
    {
        const float CanvasWidth = 1920f;
        const float CanvasHeight = 1080f;
        const float DefaultCharacterHeight = 880f;

        static readonly string[] SlotNames = { "left", "center", "right" };
        static readonly float[] SlotX = { -380f, 0f, 380f };

        readonly List<VNCharacterDef> _characters = new List<VNCharacterDef>();
        readonly List<string> _characterLabels = new List<string>();

        VNCharacterDef _character;
        VNCharacterPreviewDraft _draft;
        Sprite _background;
        float _baseCharacterHeight = DefaultCharacterHeight;
        Vector2 _portraitWindowSize = new Vector2(230f, 300f);
        Color _dialoguePanelColor = new Color(0.05f, 0.07f, 0.13f, 0.78f);
        Color _dialogueFrameColor = new Color(1f, 0.85f, 0.5f, 0.9f);
        Color _dialogueNameTagColor = new Color(0.45f, 0.3f, 0.75f, 0.9f);
        bool _showDialogueUi = true;
        bool _previewBlinkClosed;
        bool _previewMouthOpen;
        string _previewDialogue = "今天的晚霞真漂亮啊……整片天空都烧起来了一样。";
        int _characterIndex;
        int _expressionIndex;
        int _portraitIndex;
        int _slotIndex = 1;
        Vector2 _scroll;

        bool _draggingStage;
        bool _draggingPortrait;
        Vector2 _dragStartMouse;
        Vector2 _dragStartOffset;

        [MenuItem("Tools/VN Effects/Character Visual Preview")]
        static void Open()
        {
            var window = GetWindow<VNCharacterVisualPreviewWindow>("角色视觉预览");
            window.minSize = new Vector2(920f, 680f);
            window.Show();
        }

        [MenuItem("Assets/VN Effects/Open Character Visual Preview", true)]
        static bool ValidateOpenSelected() => Selection.activeObject is VNCharacterDef;

        [MenuItem("Assets/VN Effects/Open Character Visual Preview")]
        static void OpenSelected()
        {
            OpenFor(Selection.activeObject as VNCharacterDef);
        }

        [MenuItem("CONTEXT/VNCharacterDef/Open Character Visual Preview")]
        static void OpenFromContext(MenuCommand command)
        {
            OpenFor(command.context as VNCharacterDef);
        }

        static void OpenFor(VNCharacterDef character)
        {
            Open();
            var window = GetWindow<VNCharacterVisualPreviewWindow>();
            window.TrySetCharacter(character);
        }

        void OnEnable()
        {
            RefreshCharacterList();
            ReadSceneDefaults(false);
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;

            if (Selection.activeObject is VNCharacterDef selected)
                SetCharacterImmediate(selected);
            else if (_character == null && _characters.Count > 0)
                SetCharacterImmediate(_characters[0]);
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
            if (_draft != null) DestroyImmediate(_draft);
        }

        void OnSelectionChange()
        {
            if (Selection.activeObject is VNCharacterDef selected && selected != _character)
                TrySetCharacter(selected);
        }

        void OnUndoRedo()
        {
            ClampSelectionIndices();
            RepaintAll();
        }

        void OnProjectChanged()
        {
            RefreshCharacterList();
            Repaint();
        }

        void OnGUI()
        {
            DrawToolbar();

            if (_character == null)
            {
                EditorGUILayout.HelpBox(
                    "项目里没有可预览的 VNCharacterDef。请先创建或在 Project 窗口选择一个角色定义资产。",
                    MessageType.Info);
                return;
            }

            EnsureDraft();
            ClampSelectionIndices();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawPreviews();
            GUILayout.Space(8f);
            DrawCalibrationControls();
            DrawConfirmationBar();
            GUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "预览操作：在左侧舞台拖动立绘可调整草稿 positionOffset，滚轮调整草稿 sizeScale；" +
                "在右侧头像窗口拖动可调整草稿 portraitOffset，滚轮调整草稿 portraitScale。\n" +
                "立绘预览严格使用运行时公式：高度 = VNStage.characterHeight × sizeScale，" +
                "位置 = 标准站位 + positionOffset。头像预览使用 VNDialogueBox 的顶边锚定与裁切公式。" +
                "闭眼和张嘴图可以组合预览。所有校准值只有按下“确认写入角色资产”才会保存。",
                MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("角色", GUILayout.Width(30f));
                if (_characterLabels.Count == 0)
                {
                    GUILayout.Label("(没有角色定义)", EditorStyles.miniLabel,
                        GUILayout.MinWidth(190f));
                }
                else
                {
                    using (var check = new EditorGUI.ChangeCheckScope())
                    {
                        int next = EditorGUILayout.Popup(_characterIndex, _characterLabels.ToArray(),
                            EditorStyles.toolbarPopup, GUILayout.MinWidth(190f));
                        if (check.changed && next >= 0 && next < _characters.Count)
                            TrySetCharacter(_characters[next]);
                    }
                }

                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                    RefreshCharacterList();
                if (GUILayout.Button("定位资产", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                {
                    Selection.activeObject = _character;
                    EditorGUIUtility.PingObject(_character);
                }
                if (GUILayout.Button("从场景读取尺寸", EditorStyles.toolbarButton, GUILayout.Width(94f)))
                    ReadSceneDefaults(true);

                GUILayout.FlexibleSpace();
                GUIStyle stateStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = HasPendingChanges
                        ? new Color(1f, 0.65f, 0.18f) : new Color(0.45f, 0.8f, 0.5f) },
                };
                GUILayout.Label(HasPendingChanges ? "● 有未确认调整" : "✓ 资产值已同步",
                    stateStyle, GUILayout.Width(96f));
            }
        }

        void DrawPreviews()
        {
            float available = Mathf.Max(860f, position.width - 42f);
            float stageWidth = available * 0.68f;
            float portraitWidth = available - stageWidth - 8f;
            float previewHeight = Mathf.Clamp(stageWidth * 9f / 16f + 42f, 300f, 430f);

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect stagePanel = GUILayoutUtility.GetRect(stageWidth, previewHeight,
                    GUILayout.Width(stageWidth), GUILayout.Height(previewHeight));
                DrawPanel(stagePanel, "角色立绘（1920×1080 舞台）");
                Rect stageRect = AspectFit(new Rect(stagePanel.x + 8f, stagePanel.y + 28f,
                    stagePanel.width - 16f, stagePanel.height - 36f), 16f / 9f);
                DrawStagePreview(stageRect);

                Rect portraitPanel = GUILayoutUtility.GetRect(portraitWidth, previewHeight,
                    GUILayout.Width(portraitWidth), GUILayout.Height(previewHeight));
                DrawPanel(portraitPanel, $"对话头像（{_portraitWindowSize.x:0}×{_portraitWindowSize.y:0}）");
                Rect portraitArea = new Rect(portraitPanel.x + 8f, portraitPanel.y + 28f,
                    portraitPanel.width - 16f, portraitPanel.height - 36f);
                DrawPortraitPreview(portraitArea);
            }
        }

        void DrawStagePreview(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.055f, 0.065f, 0.09f));
            DrawGrid(rect, 8, 4, new Color(1f, 1f, 1f, 0.055f));

            if (_background != null)
                DrawSprite(_background, rect, new Color(1f, 1f, 1f, 0.72f), ScaleMode.ScaleAndCrop);

            Vector2 slot = new Vector2(SlotX[Mathf.Clamp(_slotIndex, 0, SlotX.Length - 1)], -60f);
            Vector2 center = slot + _draft.positionOffset;
            Sprite sprite = ExpressionSprite();
            if (sprite != null)
            {
                float height = _baseCharacterHeight * Mathf.Max(0.05f, _draft.sizeScale);
                float width = height * sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
                Vector2 guiCenter = CanvasToGui(rect, center);
                Rect spriteRect = new Rect(
                    guiCenter.x - width / CanvasWidth * rect.width * 0.5f,
                    guiCenter.y - height / CanvasHeight * rect.height * 0.5f,
                    width / CanvasWidth * rect.width,
                    height / CanvasHeight * rect.height);

                GUI.BeginClip(rect);
                var localRect = new Rect(spriteRect.x - rect.x, spriteRect.y - rect.y,
                    spriteRect.width, spriteRect.height);
                DrawSprite(sprite, localRect, Color.white);
                Sprite mouth = MouthPreviewSprite();
                if (mouth != null) DrawSprite(mouth, localRect, Color.white);
                GUI.EndClip();

                DrawRectOutline(spriteRect, new Color(0.35f, 0.85f, 1f, 0.8f), 1f);
            }
            else
            {
                GUI.Label(rect, "没有可预览的表情立绘", CenteredLabelStyle());
            }

            Vector2 groundA = CanvasToGui(rect, new Vector2(-960f, -500f));
            Vector2 groundB = CanvasToGui(rect, new Vector2(960f, -500f));
            Handles.BeginGUI();
            Handles.color = new Color(0.4f, 0.8f, 1f, 0.3f);
            Handles.DrawLine(groundA, groundB);
            Handles.EndGUI();

            if (_showDialogueUi)
                DrawDialogueOverlay(rect);

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);
            HandleStageInput(rect);

            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 20f),
                $"{SlotNames[_slotIndex]}  |  高度 {_baseCharacterHeight * _draft.sizeScale:0}px  |  " +
                $"偏移 ({_draft.positionOffset.x:0}, {_draft.positionOffset.y:0})",
                EditorStyles.miniLabel);
        }

        void DrawPortraitPreview(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.045f, 0.052f, 0.075f));

            float scale = Mathf.Min((area.width - 28f) / Mathf.Max(1f, _portraitWindowSize.x),
                (area.height - 28f) / Mathf.Max(1f, _portraitWindowSize.y));
            scale = Mathf.Max(0.1f, scale);
            Vector2 shownSize = _portraitWindowSize * scale;
            Rect windowRect = new Rect(area.center.x - shownSize.x * 0.5f,
                area.center.y - shownSize.y * 0.5f, shownSize.x, shownSize.y);

            DrawChecker(windowRect, 14f);
            Sprite sprite = PortraitSprite();
            if (sprite != null)
            {
                float width = _portraitWindowSize.x * Mathf.Max(0.05f, _draft.portraitScale) * scale;
                float height = sprite.rect.height / Mathf.Max(1f, sprite.rect.width) * width;
                Rect imageRect = new Rect(
                    windowRect.width * 0.5f - width * 0.5f + _draft.portraitOffset.x * scale,
                    -_draft.portraitOffset.y * scale,
                    width, height);

                GUI.BeginGroup(windowRect);
                DrawSprite(sprite, imageRect,
                    _draft.showPortrait ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                GUI.EndGroup();
            }
            else
            {
                GUI.Label(windowRect, "没有头像或立绘", CenteredLabelStyle());
            }

            DrawRectOutline(windowRect,
                _draft.showPortrait ? new Color(1f, 0.78f, 0.32f, 0.95f)
                    : new Color(0.6f, 0.6f, 0.6f, 0.8f), 2f);

            if (!_draft.showPortrait)
                GUI.Label(new Rect(windowRect.x, windowRect.center.y - 10f, windowRect.width, 20f),
                    "该角色头像已关闭", CenteredLabelStyle());

            EditorGUIUtility.AddCursorRect(windowRect, MouseCursor.Pan);
            HandlePortraitInput(windowRect, scale);

            GUI.Label(new Rect(area.x + 6f, area.y + 5f, area.width - 12f, 20f),
                $"缩放 ×{_draft.portraitScale:0.00}  |  偏移 " +
                $"({_draft.portraitOffset.x:0}, {_draft.portraitOffset.y:0})",
                EditorStyles.miniLabel);
        }

        void DrawCalibrationControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true)))
                {
                    EditorGUILayout.LabelField("角色立绘校准", EditorStyles.boldLabel);
                    _expressionIndex = EditorGUILayout.Popup("预览表情", _expressionIndex,
                        ExpressionNames());
                    _slotIndex = EditorGUILayout.Popup("预览站位", _slotIndex, SlotNames);
                    _baseCharacterHeight = Mathf.Max(50f,
                        EditorGUILayout.FloatField("舞台统一高度", _baseCharacterHeight));
                    _background = (Sprite)EditorGUILayout.ObjectField(
                        "预览背景（不保存）", _background, typeof(Sprite), false);
                    _showDialogueUi = EditorGUILayout.Toggle("显示完整对话 UI", _showDialogueUi);
                    using (new EditorGUI.DisabledScope(!_showDialogueUi))
                        _previewDialogue = EditorGUILayout.TextField("预览对白", _previewDialogue);

                    float size = EditorGUILayout.Slider("sizeScale",
                        _draft.sizeScale, 0.3f, 2.5f);
                    Vector2 offset = EditorGUILayout.Vector2Field(
                        "positionOffset", _draft.positionOffset);
                    if (!Mathf.Approximately(size, _draft.sizeScale) ||
                        offset != _draft.positionOffset)
                    {
                        RecordDraft("调整角色立绘校准草稿");
                        _draft.sizeScale = size;
                        _draft.positionOffset = offset;
                        ChangedDraft();
                    }

                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("默认表情眨眼", EditorStyles.boldLabel);
                    bool enableBlink = EditorGUILayout.Toggle("enableBlink", _draft.enableBlink);
                    Sprite blinkSprite = (Sprite)EditorGUILayout.ObjectField(
                        "完整闭眼立绘", _draft.blinkSprite, typeof(Sprite), false);
                    float intervalMin = Mathf.Max(0.1f,
                        EditorGUILayout.FloatField("最短间隔（秒）", _draft.blinkIntervalMin));
                    float intervalMax = Mathf.Max(intervalMin,
                        EditorGUILayout.FloatField("最长间隔（秒）", _draft.blinkIntervalMax));
                    float duration = EditorGUILayout.Slider(
                        "闭眼时间（秒）", _draft.blinkDuration, 0.03f, 0.5f);
                    if (enableBlink != _draft.enableBlink || blinkSprite != _draft.blinkSprite ||
                        !Mathf.Approximately(intervalMin, _draft.blinkIntervalMin) ||
                        !Mathf.Approximately(intervalMax, _draft.blinkIntervalMax) ||
                        !Mathf.Approximately(duration, _draft.blinkDuration))
                    {
                        RecordDraft("调整角色眨眼设置草稿");
                        _draft.enableBlink = enableBlink;
                        _draft.blinkSprite = blinkSprite;
                        _draft.blinkIntervalMin = intervalMin;
                        _draft.blinkIntervalMax = intervalMax;
                        _draft.blinkDuration = duration;
                        ChangedDraft();
                    }

                    bool canPreviewBlink = _expressionIndex == 0 && _draft.blinkSprite != null;
                    using (new EditorGUI.DisabledScope(!canPreviewBlink))
                        _previewBlinkClosed = EditorGUILayout.Toggle(
                            "预览闭眼状态", _previewBlinkClosed);

                    if (_draft.enableBlink && _draft.blinkSprite == null)
                        EditorGUILayout.HelpBox("已开启眨眼，但尚未指定完整闭眼立绘。运行时会保持睁眼。",
                            MessageType.Warning);
                    else if (BlinkSpritesMisaligned())
                        EditorGUILayout.HelpBox(
                            "闭眼图与默认立绘的宽高比或 Pivot 不一致，眨眼时可能发生跳动。",
                            MessageType.Warning);

                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("说话口型", EditorStyles.boldLabel);
                    bool enableMouth = EditorGUILayout.Toggle(
                        "enableMouthFlap", _draft.enableMouthFlap);
                    Sprite mouthSprite = (Sprite)EditorGUILayout.ObjectField(
                        "透明张嘴图", _draft.openMouthSprite, typeof(Sprite), false);
                    bool defaultOnly = EditorGUILayout.Toggle(
                        "仅默认表情", _draft.mouthDefaultExpressionOnly);
                    float mouthMin = Mathf.Max(0.03f,
                        EditorGUILayout.FloatField("最短切换间隔", _draft.mouthIntervalMin));
                    float mouthMax = Mathf.Max(mouthMin,
                        EditorGUILayout.FloatField("最长切换间隔", _draft.mouthIntervalMax));
                    if (enableMouth != _draft.enableMouthFlap ||
                        mouthSprite != _draft.openMouthSprite ||
                        defaultOnly != _draft.mouthDefaultExpressionOnly ||
                        !Mathf.Approximately(mouthMin, _draft.mouthIntervalMin) ||
                        !Mathf.Approximately(mouthMax, _draft.mouthIntervalMax))
                    {
                        RecordDraft("调整角色说话口型草稿");
                        _draft.enableMouthFlap = enableMouth;
                        _draft.openMouthSprite = mouthSprite;
                        _draft.mouthDefaultExpressionOnly = defaultOnly;
                        _draft.mouthIntervalMin = mouthMin;
                        _draft.mouthIntervalMax = mouthMax;
                        ChangedDraft();
                    }

                    bool mouthAllowed = _draft.openMouthSprite != null &&
                        (!_draft.mouthDefaultExpressionOnly || _expressionIndex == 0);
                    using (new EditorGUI.DisabledScope(!mouthAllowed))
                        _previewMouthOpen = EditorGUILayout.Toggle(
                            "预览张嘴状态", _previewMouthOpen);

                    if (_draft.enableMouthFlap && _draft.openMouthSprite == null)
                        EditorGUILayout.HelpBox("已开启口型，但尚未指定透明张嘴图。运行时会保持闭嘴。",
                            MessageType.Warning);
                    else if (MouthSpritesMisaligned())
                        EditorGUILayout.HelpBox(
                            "张嘴图与默认立绘的宽高比或 Pivot 不一致，嘴部叠加可能错位。",
                            MessageType.Warning);

                    if (GUILayout.Button("选中张嘴图") && _draft.openMouthSprite != null)
                    {
                        Selection.activeObject = _draft.openMouthSprite;
                        EditorGUIUtility.PingObject(_draft.openMouthSprite);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("立绘参数归零"))
                        {
                            RecordDraft("重置角色立绘校准草稿");
                            _draft.sizeScale = 1f;
                            _draft.positionOffset = Vector2.zero;
                            ChangedDraft();
                        }
                        if (GUILayout.Button("选中当前立绘") && ExpressionSprite() != null)
                        {
                            Selection.activeObject = ExpressionSprite();
                            EditorGUIUtility.PingObject(ExpressionSprite());
                        }
                    }
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true)))
                {
                    EditorGUILayout.LabelField("对话头像校准", EditorStyles.boldLabel);
                    _portraitIndex = EditorGUILayout.Popup("预览头像", _portraitIndex,
                        PortraitNames());
                    _portraitWindowSize = EditorGUILayout.Vector2Field(
                        "头像窗口（预览）", _portraitWindowSize);

                    bool show = EditorGUILayout.Toggle("showPortrait", _draft.showPortrait);
                    float scale = EditorGUILayout.Slider("portraitScale",
                        _draft.portraitScale, 0.2f, 6f);
                    Vector2 offset = EditorGUILayout.Vector2Field(
                        "portraitOffset", _draft.portraitOffset);
                    if (show != _draft.showPortrait ||
                        !Mathf.Approximately(scale, _draft.portraitScale) ||
                        offset != _draft.portraitOffset)
                    {
                        RecordDraft("调整角色对话头像校准草稿");
                        _draft.showPortrait = show;
                        _draft.portraitScale = scale;
                        _draft.portraitOffset = offset;
                        ChangedDraft();
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("头像参数归零"))
                        {
                            RecordDraft("重置角色对话头像校准草稿");
                            _draft.portraitScale = 1f;
                            _draft.portraitOffset = Vector2.zero;
                            ChangedDraft();
                        }
                        if (GUILayout.Button("选中当前头像") && PortraitSprite() != null)
                        {
                            Selection.activeObject = PortraitSprite();
                            EditorGUIUtility.PingObject(PortraitSprite());
                        }
                    }
                }
            }
        }

        void DrawConfirmationBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(HasPendingChanges
                    ? "当前显示的是未确认草稿；角色资产尚未改变。"
                    : "当前草稿与角色资产一致。",
                    HasPendingChanges ? EditorStyles.boldLabel : EditorStyles.label);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!HasPendingChanges))
                {
                    if (GUILayout.Button("放弃未确认调整", GUILayout.Width(130f), GUILayout.Height(28f)))
                        DiscardDraft(true);
                    if (GUILayout.Button("确认写入角色资产", GUILayout.Width(150f), GUILayout.Height(28f)))
                        ApplyDraft(true);
                }
            }
        }

        void ApplyDraft(bool notify)
        {
            if (_character == null || _draft == null || !HasPendingChanges)
            {
                if (notify) ShowNotification(new GUIContent("没有需要确认的调整"));
                return;
            }

            Undo.RecordObject(_character, "确认角色视觉校准");
            _character.sizeScale = _draft.sizeScale;
            _character.positionOffset = _draft.positionOffset;
            _character.enableBlink = _draft.enableBlink;
            _character.blinkSprite = _draft.blinkSprite;
            _character.blinkIntervalMin = _draft.blinkIntervalMin;
            _character.blinkIntervalMax = _draft.blinkIntervalMax;
            _character.blinkDuration = _draft.blinkDuration;
            _character.enableMouthFlap = _draft.enableMouthFlap;
            _character.openMouthSprite = _draft.openMouthSprite;
            _character.mouthDefaultExpressionOnly = _draft.mouthDefaultExpressionOnly;
            _character.mouthIntervalMin = _draft.mouthIntervalMin;
            _character.mouthIntervalMax = _draft.mouthIntervalMax;
            _character.showPortrait = _draft.showPortrait;
            _character.portraitScale = _draft.portraitScale;
            _character.portraitOffset = _draft.portraitOffset;
            EditorUtility.SetDirty(_character);
            AssetDatabase.SaveAssetIfDirty(_character);
            if (notify) ShowNotification(new GUIContent("角色资产已更新并保存"));
            RepaintAll();
        }

        void DiscardDraft(bool notify)
        {
            if (_character == null || _draft == null) return;
            _draft.ReadFrom(_character);
            if (notify) ShowNotification(new GUIContent("已放弃未确认调整"));
            RepaintAll();
        }

        void DrawDialogueOverlay(Rect stageRect)
        {
            Rect rootScreen = new Rect(96f, 28f, 1728f, 230f);
            Rect root = ScreenRectToGui(stageRect, rootScreen);
            float uiScale = stageRect.width / CanvasWidth;
            Sprite portrait = PortraitSprite();
            bool showPortrait = _draft.showPortrait && portrait != null;
            float portraitInset = showPortrait ? _portraitWindowSize.x + 14f : 0f;

            EditorGUI.DrawRect(new Rect(root.x + 2f, root.y + 3f, root.width, root.height),
                new Color(0f, 0f, 0f, 0.35f));
            EditorGUI.DrawRect(root, _dialoguePanelColor);
            DrawRectOutline(root, _dialogueFrameColor, Mathf.Max(1f, 2f * uiScale));

            Rect nameTagScreen = new Rect(
                rootScreen.x + 44f + portraitInset,
                rootScreen.y + rootScreen.height + 4f - 25f,
                210f, 50f);
            Rect nameTag = ScreenRectToGui(stageRect, nameTagScreen);
            Color nameColor = _dialogueNameTagColor;
            nameColor.a = Mathf.Max(0.85f, nameColor.a);
            EditorGUI.DrawRect(nameTag, nameColor);

            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(26f * uiScale)),
                normal = { textColor = Color.white },
            };
            string speaker = string.IsNullOrEmpty(_character.displayName)
                ? _character.name : _character.displayName;
            GUI.Label(nameTag, speaker, nameStyle);

            Rect bodyScreen = new Rect(
                rootScreen.x + 40f + portraitInset,
                rootScreen.y + 26f,
                rootScreen.width - 80f - portraitInset,
                rootScreen.height - 66f);
            Rect body = ScreenRectToGui(stageRect, bodyScreen);
            var bodyStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(28f * uiScale)),
                normal = { textColor = Color.white },
            };
            GUI.Label(body, _previewDialogue, bodyStyle);

            if (showPortrait)
            {
                Rect portraitScreen = new Rect(
                    rootScreen.x + 14f,
                    rootScreen.y + 12f,
                    _portraitWindowSize.x,
                    _portraitWindowSize.y);
                Rect portraitWindow = ScreenRectToGui(stageRect, portraitScreen);
                DrawDialoguePortrait(portraitWindow, uiScale, portrait);
            }

            Rect arrowScreen = new Rect(
                rootScreen.xMax - 58f,
                rootScreen.y + 9f,
                40f, 34f);
            Rect arrow = ScreenRectToGui(stageRect, arrowScreen);
            var arrowStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(26f * uiScale)),
                normal = { textColor = new Color(1f, 0.9f, 0.62f) },
            };
            GUI.Label(arrow, "▼", arrowStyle);
        }

        void DrawDialoguePortrait(Rect windowRect, float uiScale, Sprite portrait)
        {
            EditorGUI.DrawRect(windowRect, new Color(0.02f, 0.025f, 0.04f, 0.55f));
            float width = _portraitWindowSize.x * Mathf.Max(0.05f, _draft.portraitScale) * uiScale;
            float height = portrait.rect.height / Mathf.Max(1f, portrait.rect.width) * width;
            Rect imageRect = new Rect(
                windowRect.width * 0.5f - width * 0.5f + _draft.portraitOffset.x * uiScale,
                -_draft.portraitOffset.y * uiScale,
                width, height);

            GUI.BeginGroup(windowRect);
            DrawSprite(portrait, imageRect, Color.white);
            GUI.EndGroup();
            DrawRectOutline(windowRect, new Color(1f, 1f, 1f, 0.16f), 1f);
        }

        static Rect ScreenRectToGui(Rect stageRect, Rect screenRect)
        {
            float scaleX = stageRect.width / CanvasWidth;
            float scaleY = stageRect.height / CanvasHeight;
            return new Rect(
                stageRect.x + screenRect.x * scaleX,
                stageRect.y + (CanvasHeight - screenRect.yMax) * scaleY,
                screenRect.width * scaleX,
                screenRect.height * scaleY);
        }

        void HandleStageInput(Rect rect)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _draggingStage = true;
                _dragStartMouse = e.mousePosition;
                _dragStartOffset = _draft.positionOffset;
                RecordDraft("拖动角色立绘位置草稿");
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingStage && e.button == 0)
            {
                Vector2 delta = e.mousePosition - _dragStartMouse;
                _draft.positionOffset = _dragStartOffset + new Vector2(
                    delta.x / rect.width * CanvasWidth,
                    -delta.y / rect.height * CanvasHeight);
                ChangedDraft();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _draggingStage && e.button == 0)
            {
                _draggingStage = false;
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                RecordDraft("滚轮调整角色立绘尺寸草稿");
                _draft.sizeScale = Mathf.Clamp(
                    _draft.sizeScale * (1f - e.delta.y * 0.035f), 0.3f, 2.5f);
                ChangedDraft();
                e.Use();
            }
        }

        void HandlePortraitInput(Rect rect, float displayScale)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _draggingPortrait = true;
                _dragStartMouse = e.mousePosition;
                _dragStartOffset = _draft.portraitOffset;
                RecordDraft("拖动对话头像位置草稿");
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingPortrait && e.button == 0)
            {
                Vector2 delta = (e.mousePosition - _dragStartMouse) / Mathf.Max(0.01f, displayScale);
                _draft.portraitOffset = _dragStartOffset + new Vector2(delta.x, -delta.y);
                ChangedDraft();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _draggingPortrait && e.button == 0)
            {
                _draggingPortrait = false;
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                RecordDraft("滚轮调整对话头像尺寸草稿");
                _draft.portraitScale = Mathf.Clamp(
                    _draft.portraitScale * (1f - e.delta.y * 0.035f), 0.2f, 6f);
                ChangedDraft();
                e.Use();
            }
        }

        void RefreshCharacterList()
        {
            VNCharacterDef keep = _character;
            _characters.Clear();
            _characterLabels.Clear();

            foreach (string guid in AssetDatabase.FindAssets("t:VNCharacterDef"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var character = AssetDatabase.LoadAssetAtPath<VNCharacterDef>(path);
                if (character == null) continue;
                _characters.Add(character);
                string label = string.IsNullOrEmpty(character.displayName)
                    ? character.name : character.displayName;
                _characterLabels.Add($"{label}  [{character.id}]");
            }

            _characterIndex = Mathf.Max(0, _characters.IndexOf(keep));
            if (keep != null && _characters.Contains(keep)) _character = keep;
            else if (_characters.Count > 0) SetCharacterImmediate(_characters[_characterIndex]);
            else
            {
                _character = null;
                if (_draft != null) DestroyImmediate(_draft);
            }
        }

        void EnsureDraft()
        {
            if (_draft != null) return;
            _draft = CreateInstance<VNCharacterPreviewDraft>();
            _draft.name = "VN Character Preview Draft";
            _draft.hideFlags = HideFlags.HideAndDontSave;
            if (_character != null) _draft.ReadFrom(_character);
        }

        bool HasPendingChanges => _character != null && _draft != null &&
            (!Mathf.Approximately(_draft.sizeScale, _character.sizeScale) ||
             _draft.positionOffset != _character.positionOffset ||
             _draft.enableBlink != _character.enableBlink ||
             _draft.blinkSprite != _character.blinkSprite ||
             !Mathf.Approximately(_draft.blinkIntervalMin, _character.blinkIntervalMin) ||
             !Mathf.Approximately(_draft.blinkIntervalMax, _character.blinkIntervalMax) ||
             !Mathf.Approximately(_draft.blinkDuration, _character.blinkDuration) ||
             _draft.enableMouthFlap != _character.enableMouthFlap ||
             _draft.openMouthSprite != _character.openMouthSprite ||
             _draft.mouthDefaultExpressionOnly != _character.mouthDefaultExpressionOnly ||
             !Mathf.Approximately(_draft.mouthIntervalMin, _character.mouthIntervalMin) ||
             !Mathf.Approximately(_draft.mouthIntervalMax, _character.mouthIntervalMax) ||
             _draft.showPortrait != _character.showPortrait ||
             !Mathf.Approximately(_draft.portraitScale, _character.portraitScale) ||
             _draft.portraitOffset != _character.portraitOffset);

        bool TrySetCharacter(VNCharacterDef character)
        {
            if (character == null || character == _character) return character != null;
            if (HasPendingChanges)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "存在未确认调整",
                    $"角色“{_character.displayName}”仍有未写入资产的预览草稿。切换前要如何处理？",
                    "确认并切换", "取消切换", "放弃并切换");
                if (choice == 1) return false;
                if (choice == 0) ApplyDraft(false);
            }

            SetCharacterImmediate(character);
            return true;
        }

        void SetCharacterImmediate(VNCharacterDef character)
        {
            if (character == null) return;
            _character = character;
            EnsureDraft();
            _draft.ReadFrom(character);
            int index = _characters.IndexOf(character);
            if (index >= 0) _characterIndex = index;
            _expressionIndex = 0;
            _portraitIndex = 0;
            _previewBlinkClosed = false;
            _previewMouthOpen = false;
            ClampSelectionIndices();
            Repaint();
        }

        void ReadSceneDefaults(bool notify)
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            if (stage != null)
            {
                _baseCharacterHeight = Mathf.Max(50f, stage.characterHeight);
                if (stage.backgroundImage != null) _background = stage.backgroundImage.sprite;
            }

            var dialogue = Object.FindFirstObjectByType<VNDialogueBox>();
            if (dialogue != null)
            {
                _portraitWindowSize = dialogue.portraitWindowSize;
                _dialoguePanelColor = dialogue.panelColor;
                _dialogueFrameColor = dialogue.frameColor;
                _dialogueNameTagColor = dialogue.nameTagColor;
            }

            if (notify)
                ShowNotification(new GUIContent(stage != null || dialogue != null
                    ? "已读取当前场景尺寸" : "场景中没有 VNStage / VNDialogueBox，使用默认尺寸"));
            Repaint();
        }

        string[] ExpressionNames()
        {
            if (_character.expressions == null || _character.expressions.Count == 0)
                return new[] { "(无立绘)" };
            var names = new string[_character.expressions.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = string.IsNullOrEmpty(_character.expressions[i].name)
                    ? $"表情 {i + 1}" : _character.expressions[i].name;
            return names;
        }

        string[] PortraitNames()
        {
            bool fallback = _character.portraits == null || _character.portraits.Count == 0;
            var list = fallback ? _character.expressions : _character.portraits;
            if (list == null || list.Count == 0) return new[] { "(无头像)" };
            var names = new string[list.Count];
            for (int i = 0; i < names.Length; i++)
            {
                string name = string.IsNullOrEmpty(list[i].name) ? $"头像 {i + 1}" : list[i].name;
                names[i] = fallback ? $"{name}（回退立绘）" : name;
            }
            return names;
        }

        Sprite ExpressionSprite()
        {
            if (_character == null || _character.expressions == null ||
                _character.expressions.Count == 0) return null;
            int index = Mathf.Clamp(_expressionIndex, 0, _character.expressions.Count - 1);
            if (index == 0 && _previewBlinkClosed && _draft != null &&
                _draft.blinkSprite != null)
                return _draft.blinkSprite;
            return _character.expressions[index].sprite;
        }

        bool BlinkSpritesMisaligned()
        {
            if (_character == null || _character.DefaultSprite == null ||
                _draft == null || _draft.blinkSprite == null)
                return false;

            Sprite open = _character.DefaultSprite;
            Sprite closed = _draft.blinkSprite;
            float openAspect = open.rect.width / Mathf.Max(1f, open.rect.height);
            float closedAspect = closed.rect.width / Mathf.Max(1f, closed.rect.height);
            return Mathf.Abs(openAspect - closedAspect) > 0.01f ||
                   Vector2.Distance(open.pivot, closed.pivot) > 0.5f;
        }

        Sprite MouthPreviewSprite()
        {
            if (!_previewMouthOpen || _draft == null || _draft.openMouthSprite == null)
                return null;
            if (_draft.mouthDefaultExpressionOnly && _expressionIndex != 0)
                return null;
            return _draft.openMouthSprite;
        }

        bool MouthSpritesMisaligned()
        {
            if (_character == null || _character.DefaultSprite == null ||
                _draft == null || _draft.openMouthSprite == null)
                return false;

            Sprite closed = _character.DefaultSprite;
            Sprite open = _draft.openMouthSprite;
            float closedAspect = closed.rect.width / Mathf.Max(1f, closed.rect.height);
            float openAspect = open.rect.width / Mathf.Max(1f, open.rect.height);
            return Mathf.Abs(closedAspect - openAspect) > 0.01f ||
                   Vector2.Distance(closed.pivot, open.pivot) > 0.5f;
        }

        Sprite PortraitSprite()
        {
            if (_character == null) return null;
            bool fallback = _character.portraits == null || _character.portraits.Count == 0;
            var list = fallback ? _character.expressions : _character.portraits;
            if (list == null || list.Count == 0) return null;
            int index = Mathf.Clamp(_portraitIndex, 0, list.Count - 1);
            return list[index].sprite;
        }

        void ClampSelectionIndices()
        {
            if (_character == null) return;
            int expressionCount = _character.expressions == null ? 0 : _character.expressions.Count;
            int portraitCount = _character.portraits == null || _character.portraits.Count == 0
                ? expressionCount : _character.portraits.Count;
            _expressionIndex = Mathf.Clamp(_expressionIndex, 0, Mathf.Max(0, expressionCount - 1));
            _portraitIndex = Mathf.Clamp(_portraitIndex, 0, Mathf.Max(0, portraitCount - 1));
            _slotIndex = Mathf.Clamp(_slotIndex, 0, SlotNames.Length - 1);
        }

        void RecordDraft(string action)
        {
            EnsureDraft();
            Undo.RecordObject(_draft, action);
        }

        void ChangedDraft()
        {
            RepaintAll();
        }

        void RepaintAll()
        {
            Repaint();
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        static void DrawPanel(Rect rect, string title)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, 20f),
                title, EditorStyles.boldLabel);
        }

        static Rect AspectFit(Rect rect, float aspect)
        {
            float width = rect.width;
            float height = width / aspect;
            if (height > rect.height)
            {
                height = rect.height;
                width = height * aspect;
            }
            return new Rect(rect.center.x - width * 0.5f, rect.center.y - height * 0.5f,
                width, height);
        }

        static Vector2 CanvasToGui(Rect rect, Vector2 point)
        {
            return new Vector2(
                rect.x + (point.x + CanvasWidth * 0.5f) / CanvasWidth * rect.width,
                rect.y + (1f - (point.y + CanvasHeight * 0.5f) / CanvasHeight) * rect.height);
        }

        static void DrawGrid(Rect rect, int columns, int rows, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            for (int x = 1; x < columns; x++)
            {
                float px = Mathf.Lerp(rect.x, rect.xMax, x / (float)columns);
                Handles.DrawLine(new Vector2(px, rect.y), new Vector2(px, rect.yMax));
            }
            for (int y = 1; y < rows; y++)
            {
                float py = Mathf.Lerp(rect.y, rect.yMax, y / (float)rows);
                Handles.DrawLine(new Vector2(rect.x, py), new Vector2(rect.xMax, py));
            }
            Handles.EndGUI();
        }

        static void DrawChecker(Rect rect, float cell)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.2f));
            int columns = Mathf.CeilToInt(rect.width / cell);
            int rows = Mathf.CeilToInt(rect.height / cell);
            Color light = new Color(0.27f, 0.27f, 0.3f);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                    if ((x + y) % 2 == 0)
                        EditorGUI.DrawRect(new Rect(rect.x + x * cell, rect.y + y * cell,
                            Mathf.Min(cell, rect.xMax - (rect.x + x * cell)),
                            Mathf.Min(cell, rect.yMax - (rect.y + y * cell))), light);
        }

        static void DrawSprite(Sprite sprite, Rect rect, Color tint,
            ScaleMode scaleMode = ScaleMode.StretchToFill)
        {
            if (sprite == null || sprite.texture == null) return;
            Texture2D texture = sprite.texture;
            Rect textureRect;
            try { textureRect = sprite.textureRect; }
            catch { textureRect = new Rect(0f, 0f, texture.width, texture.height); }

            Rect uv = new Rect(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height);
            Color old = GUI.color;
            GUI.color = tint;

            if (scaleMode == ScaleMode.StretchToFill)
                GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
            else
                DrawSpriteScaled(texture, uv, rect, textureRect.size, scaleMode);

            GUI.color = old;
        }

        static void DrawSpriteScaled(Texture texture, Rect uv, Rect target,
            Vector2 sourceSize, ScaleMode mode)
        {
            float sourceAspect = sourceSize.x / Mathf.Max(1f, sourceSize.y);
            float targetAspect = target.width / Mathf.Max(1f, target.height);
            if (mode == ScaleMode.ScaleToFit)
            {
                Rect fitted = AspectFit(target, sourceAspect);
                GUI.DrawTextureWithTexCoords(fitted, texture, uv, true);
                return;
            }

            Rect croppedUv = uv;
            if (sourceAspect > targetAspect)
            {
                float fraction = targetAspect / sourceAspect;
                croppedUv.x += croppedUv.width * (1f - fraction) * 0.5f;
                croppedUv.width *= fraction;
            }
            else
            {
                float fraction = sourceAspect / targetAspect;
                croppedUv.y += croppedUv.height * (1f - fraction) * 0.5f;
                croppedUv.height *= fraction;
            }
            GUI.DrawTextureWithTexCoords(target, texture, croppedUv, true);
        }

        static void DrawRectOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        static GUIStyle CenteredLabelStyle()
        {
            return new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
        }
    }
}
