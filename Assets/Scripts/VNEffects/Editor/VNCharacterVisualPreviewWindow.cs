using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
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
        Sprite _background;
        float _baseCharacterHeight = DefaultCharacterHeight;
        Vector2 _portraitWindowSize = new Vector2(230f, 300f);
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
            window.SetCharacter(character);
        }

        void OnEnable()
        {
            RefreshCharacterList();
            ReadSceneDefaults(false);
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;

            if (Selection.activeObject is VNCharacterDef selected)
                SetCharacter(selected);
            else if (_character == null && _characters.Count > 0)
                SetCharacter(_characters[0]);
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        void OnSelectionChange()
        {
            if (Selection.activeObject is VNCharacterDef selected && selected != _character)
                SetCharacter(selected);
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

            ClampSelectionIndices();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawPreviews();
            GUILayout.Space(8f);
            DrawCalibrationControls();
            GUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "实时操作：在左侧舞台拖动立绘可调整 positionOffset，滚轮调整 sizeScale；" +
                "在右侧头像窗口拖动可调整 portraitOffset，滚轮调整 portraitScale。\n" +
                "立绘预览严格使用运行时公式：高度 = VNStage.characterHeight × sizeScale，" +
                "位置 = 标准站位 + positionOffset。头像预览使用 VNDialogueBox 的顶边锚定与裁切公式。",
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
                            SetCharacter(_characters[next]);
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
                if (GUILayout.Button("保存角色资产", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                {
                    EditorUtility.SetDirty(_character);
                    AssetDatabase.SaveAssetIfDirty(_character);
                    ShowNotification(new GUIContent("角色资产已保存"));
                }
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
            Vector2 center = slot + _character.positionOffset;
            Sprite sprite = ExpressionSprite();
            if (sprite != null)
            {
                float height = _baseCharacterHeight * Mathf.Max(0.05f, _character.sizeScale);
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

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);
            HandleStageInput(rect);

            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 20f),
                $"{SlotNames[_slotIndex]}  |  高度 {_baseCharacterHeight * _character.sizeScale:0}px  |  " +
                $"偏移 ({_character.positionOffset.x:0}, {_character.positionOffset.y:0})",
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
                float width = _portraitWindowSize.x * Mathf.Max(0.05f, _character.portraitScale) * scale;
                float height = sprite.rect.height / Mathf.Max(1f, sprite.rect.width) * width;
                Rect imageRect = new Rect(
                    windowRect.width * 0.5f - width * 0.5f + _character.portraitOffset.x * scale,
                    -_character.portraitOffset.y * scale,
                    width, height);

                GUI.BeginGroup(windowRect);
                DrawSprite(sprite, imageRect,
                    _character.showPortrait ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                GUI.EndGroup();
            }
            else
            {
                GUI.Label(windowRect, "没有头像或立绘", CenteredLabelStyle());
            }

            DrawRectOutline(windowRect,
                _character.showPortrait ? new Color(1f, 0.78f, 0.32f, 0.95f)
                    : new Color(0.6f, 0.6f, 0.6f, 0.8f), 2f);

            if (!_character.showPortrait)
                GUI.Label(new Rect(windowRect.x, windowRect.center.y - 10f, windowRect.width, 20f),
                    "该角色头像已关闭", CenteredLabelStyle());

            EditorGUIUtility.AddCursorRect(windowRect, MouseCursor.Pan);
            HandlePortraitInput(windowRect, scale);

            GUI.Label(new Rect(area.x + 6f, area.y + 5f, area.width - 12f, 20f),
                $"缩放 ×{_character.portraitScale:0.00}  |  偏移 " +
                $"({_character.portraitOffset.x:0}, {_character.portraitOffset.y:0})",
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

                    float size = EditorGUILayout.Slider("sizeScale",
                        _character.sizeScale, 0.3f, 2.5f);
                    Vector2 offset = EditorGUILayout.Vector2Field(
                        "positionOffset", _character.positionOffset);
                    if (!Mathf.Approximately(size, _character.sizeScale) ||
                        offset != _character.positionOffset)
                    {
                        RecordCharacter("调整角色立绘校准");
                        _character.sizeScale = size;
                        _character.positionOffset = offset;
                        ChangedCharacter();
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("立绘参数归零"))
                        {
                            RecordCharacter("重置角色立绘校准");
                            _character.sizeScale = 1f;
                            _character.positionOffset = Vector2.zero;
                            ChangedCharacter();
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

                    bool show = EditorGUILayout.Toggle("showPortrait", _character.showPortrait);
                    float scale = EditorGUILayout.Slider("portraitScale",
                        _character.portraitScale, 0.2f, 6f);
                    Vector2 offset = EditorGUILayout.Vector2Field(
                        "portraitOffset", _character.portraitOffset);
                    if (show != _character.showPortrait ||
                        !Mathf.Approximately(scale, _character.portraitScale) ||
                        offset != _character.portraitOffset)
                    {
                        RecordCharacter("调整角色对话头像校准");
                        _character.showPortrait = show;
                        _character.portraitScale = scale;
                        _character.portraitOffset = offset;
                        ChangedCharacter();
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("头像参数归零"))
                        {
                            RecordCharacter("重置角色对话头像校准");
                            _character.portraitScale = 1f;
                            _character.portraitOffset = Vector2.zero;
                            ChangedCharacter();
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

        void HandleStageInput(Rect rect)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _draggingStage = true;
                _dragStartMouse = e.mousePosition;
                _dragStartOffset = _character.positionOffset;
                Undo.RecordObject(_character, "拖动角色立绘位置");
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingStage && e.button == 0)
            {
                Vector2 delta = e.mousePosition - _dragStartMouse;
                _character.positionOffset = _dragStartOffset + new Vector2(
                    delta.x / rect.width * CanvasWidth,
                    -delta.y / rect.height * CanvasHeight);
                ChangedCharacter();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _draggingStage && e.button == 0)
            {
                _draggingStage = false;
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                Undo.RecordObject(_character, "滚轮调整角色立绘尺寸");
                _character.sizeScale = Mathf.Clamp(
                    _character.sizeScale * (1f - e.delta.y * 0.035f), 0.3f, 2.5f);
                ChangedCharacter();
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
                _dragStartOffset = _character.portraitOffset;
                Undo.RecordObject(_character, "拖动对话头像位置");
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingPortrait && e.button == 0)
            {
                Vector2 delta = (e.mousePosition - _dragStartMouse) / Mathf.Max(0.01f, displayScale);
                _character.portraitOffset = _dragStartOffset + new Vector2(delta.x, -delta.y);
                ChangedCharacter();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _draggingPortrait && e.button == 0)
            {
                _draggingPortrait = false;
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                Undo.RecordObject(_character, "滚轮调整对话头像尺寸");
                _character.portraitScale = Mathf.Clamp(
                    _character.portraitScale * (1f - e.delta.y * 0.035f), 0.2f, 6f);
                ChangedCharacter();
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
            else if (_characters.Count > 0) SetCharacter(_characters[_characterIndex]);
            else _character = null;
        }

        void SetCharacter(VNCharacterDef character)
        {
            if (character == null) return;
            _character = character;
            int index = _characters.IndexOf(character);
            if (index >= 0) _characterIndex = index;
            _expressionIndex = 0;
            _portraitIndex = 0;
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
            if (dialogue != null) _portraitWindowSize = dialogue.portraitWindowSize;

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
            return _character.expressions[index].sprite;
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

        void RecordCharacter(string action)
        {
            Undo.RecordObject(_character, action);
        }

        void ChangedCharacter()
        {
            EditorUtility.SetDirty(_character);
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
