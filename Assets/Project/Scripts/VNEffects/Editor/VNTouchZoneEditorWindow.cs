using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 部位区域编辑器：在立绘上直接拖框画出可互动部位。
    ///
    /// 没有这个工具的话，VNTouchZoneDef 里那些归一化坐标只能靠猜着填、
    /// 进游戏看、再回来改，一个部位要试五六轮。
    ///
    /// 坐标：画布与运行时共用 <see cref="VNTouchZoneDef.Contains"/> 的同一套语义
    /// （归一化，(0,0) = 立绘中心，x/y 各 -0.5~+0.5），所以这里画的框
    /// 就是游戏里摸到的地方。
    ///
    /// 撤销是**窗口内独立栈**（快照 = 整份 zones 的 JSON），不挂 Unity 全局 Undo ——
    /// 与 camseq 编排窗口一致：全局 Undo 会把画框和场景里其它操作混在一条时间线上。
    /// </summary>
    public class VNTouchZoneEditorWindow : EditorWindow
    {
        const float SidebarWidth = 300f;
        const float HandleSize = 10f;

        [MenuItem("Tools/VN Effects/预览 Preview/部位区域编辑器 Touch Zone Editor", priority = 60)]
        public static void Open()
        {
            var window = GetWindow<VNTouchZoneEditorWindow>("部位区域");
            window.minSize = new Vector2(900f, 560f);
            window.Refresh();
        }

        [SerializeField] VNTouchZoneDef _def;
        [SerializeField] VNCharacterDef _character;
        [SerializeField] int _expressionIndex;
        [SerializeField] int _overrideIndex = -1;   // -1 = 编辑基准
        [SerializeField] int _selected = -1;
        [SerializeField] bool _showInherited = true;

        Vector2 _listScroll;
        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();

        // 拖动状态
        int _dragZone = -1;
        bool _draggingSize;
        Vector2 _dragStartNorm;
        Vector2 _dragStartCenter;
        Vector2 _dragStartSize;

        [System.Serializable]
        class ZoneList { public List<VNTouchZone> zones = new List<VNTouchZone>(); }

        void Refresh()
        {
            if (_def == null)
            {
                var guids = AssetDatabase.FindAssets("t:VNTouchZoneDef");
                if (guids.Length > 0)
                    _def = AssetDatabase.LoadAssetAtPath<VNTouchZoneDef>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            SyncCharacter();
        }

        void SyncCharacter()
        {
            if (_def == null || string.IsNullOrEmpty(_def.characterId)) return;
            if (_character != null && _character.id == _def.characterId) return;

            _character = AssetDatabase.FindAssets("t:VNCharacterDef")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<VNCharacterDef>)
                .FirstOrDefault(c => c != null && c.id == _def.characterId);
        }

        void OnGUI()
        {
            DrawToolbar();

            if (_def == null)
            {
                EditorGUILayout.HelpBox(
                    "先选一份 VNTouchZoneDef 资产（Create → VN → Touch Zone Definition，" +
                    "或用 Tools → VN Effects → 场景装机 → 亲密互动 自动生成一份）。",
                    MessageType.Info);
                return;
            }

            var full = new Rect(0f, 22f, position.width, position.height - 22f);
            var sidebar = new Rect(full.x, full.y, SidebarWidth, full.height);
            var canvas = new Rect(full.x + SidebarWidth + 6f, full.y + 6f,
                full.width - SidebarWidth - 12f, full.height - 12f);

            DrawSidebar(sidebar);
            DrawCanvas(canvas);
            HandleShortcuts();
        }

        // ------------------------------------------------------------------

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _def = (VNTouchZoneDef)EditorGUILayout.ObjectField(
                    _def, typeof(VNTouchZoneDef), false, GUILayout.Width(220f));
                if (EditorGUI.EndChangeCheck())
                {
                    _selected = -1;
                    _overrideIndex = -1;
                    _undo.Clear();
                    _redo.Clear();
                    SyncCharacter();
                }

                if (_def != null)
                {
                    _character = (VNCharacterDef)EditorGUILayout.ObjectField(
                        _character, typeof(VNCharacterDef), false, GUILayout.Width(180f));

                    // 编辑哪一层：基准 / 某个立绘覆盖
                    var layerNames = new List<string> { "基准（默认立绘）" };
                    foreach (var o in _def.overrides)
                        layerNames.Add("覆盖：" + (o.sprite != null ? o.sprite.name
                            : string.IsNullOrEmpty(o.expression) ? "(未指定)" : o.expression));
                    int layerPick = EditorGUILayout.Popup(_overrideIndex + 1,
                        layerNames.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(180f));
                    if (layerPick - 1 != _overrideIndex)
                    {
                        _overrideIndex = layerPick - 1;
                        _selected = -1;
                    }

                    if (GUILayout.Button("+ 覆盖层", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                        AddOverride();

                    if (_character != null && _character.expressions.Count > 0)
                    {
                        var names = _character.expressions.Select(e => e.name).ToArray();
                        _expressionIndex = EditorGUILayout.Popup(
                            Mathf.Clamp(_expressionIndex, 0, names.Length - 1), names,
                            EditorStyles.toolbarPopup, GUILayout.Width(110f));
                    }

                    _showInherited = GUILayout.Toggle(_showInherited, "显示继承框",
                        EditorStyles.toolbarButton, GUILayout.Width(80f));
                }

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_undo.Count == 0))
                    if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                        PerformUndo();
                using (new EditorGUI.DisabledScope(_redo.Count == 0))
                    if (GUILayout.Button("重做", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                        PerformRedo();
            }
        }

        void DrawSidebar(Rect rect)
        {
            GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 6f,
                rect.width - 12f, rect.height - 12f));

            EditorGUILayout.LabelField(
                _overrideIndex < 0 ? "基准部位" : "本层覆盖的部位", EditorStyles.boldLabel);

            var zones = EditingZones();
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(160f));
            for (int i = 0; i < zones.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope(
                    i == _selected ? Selected : GUIStyle.none))
                {
                    if (GUILayout.Button(zones[i].Label, EditorStyles.label))
                        _selected = i;
                    zones[i].enabled = GUILayout.Toggle(zones[i].enabled, GUIContent.none,
                        GUILayout.Width(16f));
                    if (GUILayout.Button("✕", GUILayout.Width(22f)))
                    {
                        Snapshot();
                        zones.RemoveAt(i);
                        _selected = -1;
                        Save();
                        break;
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("+ 新建部位"))
            {
                Snapshot();
                zones.Add(new VNTouchZone
                {
                    id = "部位" + (zones.Count + 1),
                    shape = VNZoneShape.Ellipse,
                    center = Vector2.zero,
                    size = new Vector2(0.15f, 0.12f),
                    gainScale = 1f,
                    enabled = true,
                });
                _selected = zones.Count - 1;
                Save();
            }

            EditorGUILayout.Space(8f);

            if (_selected >= 0 && _selected < zones.Count)
            {
                var z = zones[_selected];
                EditorGUILayout.LabelField("选中部位", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                z.id = EditorGUILayout.TextField("id", z.id);
                z.displayName = EditorGUILayout.TextField("显示名", z.displayName);
                z.shape = (VNZoneShape)EditorGUILayout.EnumPopup("形状", z.shape);
                z.center = EditorGUILayout.Vector2Field("中心", z.center);
                z.size = EditorGUILayout.Vector2Field("尺寸", z.size);
                z.rotation = EditorGUILayout.Slider("旋转", z.rotation, -90f, 90f);
                z.priority = EditorGUILayout.IntField("优先级（重叠时大的赢）", z.priority);
                z.gainScale = EditorGUILayout.FloatField("增益倍率", z.gainScale);
                z.unlockStage = EditorGUILayout.IntField("解禁阶段（禁忌）", z.unlockStage);
                if (EditorGUI.EndChangeCheck()) Save();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "在右边画布上拖动框体 = 移动，拖右下角小方块 = 改尺寸。\n" +
                    "Ctrl+Z / Ctrl+Y 撤销重做（只影响本窗口）。", MessageType.None);
            }

            GUILayout.FlexibleSpace();
            if (_overrideIndex >= 0)
            {
                var ov = _def.overrides[_overrideIndex];
                EditorGUILayout.LabelField("覆盖层设置", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                ov.sprite = (Sprite)EditorGUILayout.ObjectField("匹配立绘", ov.sprite,
                    typeof(Sprite), false);
                ov.expression = EditorGUILayout.TextField("或匹配表情名", ov.expression);
                ov.replaceAll = EditorGUILayout.Toggle("完全不继承基准", ov.replaceAll);
                if (EditorGUI.EndChangeCheck()) Save();
            }
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------
        // 画布
        // ------------------------------------------------------------------

        void DrawCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.18f, 1f));

            Sprite sprite = CurrentSprite();
            Rect art = FitRect(rect, sprite != null
                ? sprite.rect.width / sprite.rect.height : 1f);

            if (sprite != null) DrawSpriteRaw(art, sprite);
            else EditorGUI.LabelField(art, "（这个角色没有立绘）", EditorStyles.centeredGreyMiniLabel);

            // 继承来的框画灰虚线，本层的画实线 —— 一眼看出哪些是能改的
            if (_showInherited && _overrideIndex >= 0)
                foreach (var z in InheritedZones())
                    DrawZone(art, z, new Color(1f, 1f, 1f, 0.18f), false);

            var zones = EditingZones();
            for (int i = 0; i < zones.Count; i++)
            {
                var color = zones[i].unlockStage > 0
                    ? new Color(1f, 0.45f, 0.45f, 0.5f)     // 禁忌部位
                    : new Color(0.4f, 0.85f, 1f, 0.5f);
                DrawZone(art, zones[i], color, i == _selected);
            }

            HandleCanvasInput(art, zones);
        }

        void DrawZone(Rect art, VNTouchZone z, Color color, bool selected)
        {
            Rect r = ZoneRect(art, z);
            Handles.color = color;
            var matrix = Handles.matrix;
            if (Mathf.Abs(z.rotation) > 0.01f)
                Handles.matrix = Matrix4x4.TRS(r.center, Quaternion.Euler(0f, 0f, -z.rotation),
                    Vector3.one) * Matrix4x4.Translate(-r.center);

            if (z.shape == VNZoneShape.Ellipse)
                DrawEllipse(r, color);
            else
                Handles.DrawSolidRectangleWithOutline(r,
                    new Color(color.r, color.g, color.b, 0.15f), color);
            Handles.matrix = matrix;

            var label = new GUIContent(z.Label);
            var style = new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = selected ? Color.yellow : Color.white;
            EditorGUI.LabelField(new Rect(r.x, r.center.y - 8f, r.width, 16f), label, style);

            if (selected)
            {
                EditorGUI.DrawRect(SizeHandleRect(r), Color.yellow);
                Handles.color = Color.yellow;
                Handles.DrawAAPolyLine(2f,
                    new Vector3(r.x, r.y), new Vector3(r.xMax, r.y),
                    new Vector3(r.xMax, r.yMax), new Vector3(r.x, r.yMax),
                    new Vector3(r.x, r.y));
            }
        }

        static void DrawEllipse(Rect r, Color color)
        {
            const int Steps = 40;
            var pts = new Vector3[Steps + 1];
            for (int i = 0; i <= Steps; i++)
            {
                float a = i / (float)Steps * Mathf.PI * 2f;
                pts[i] = new Vector3(
                    r.center.x + Mathf.Cos(a) * r.width * 0.5f,
                    r.center.y + Mathf.Sin(a) * r.height * 0.5f);
            }
            Handles.color = color;
            Handles.DrawAAPolyLine(2f, pts);
        }

        void HandleCanvasInput(Rect art, List<VNTouchZone> zones)
        {
            var e = Event.current;
            if (!art.Contains(e.mousePosition) && _dragZone < 0) return;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                {
                    // 先看是不是抓在选中框的尺寸手柄上
                    if (_selected >= 0 && _selected < zones.Count &&
                        SizeHandleRect(ZoneRect(art, zones[_selected])).Contains(e.mousePosition))
                    {
                        BeginDrag(art, _selected, true, zones[_selected]);
                        e.Use();
                        return;
                    }

                    // 命中检测直接用运行时那套数学，所见即所得
                    Vector2 norm = GuiToNorm(art, e.mousePosition);
                    int hit = -1;
                    int bestPriority = int.MinValue;
                    for (int i = 0; i < zones.Count; i++)
                        if (VNTouchZoneDef.Contains(zones[i], norm) &&
                            zones[i].priority >= bestPriority)
                        {
                            hit = i;
                            bestPriority = zones[i].priority;
                        }

                    _selected = hit;
                    if (hit >= 0) BeginDrag(art, hit, false, zones[hit]);
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseDrag when _dragZone >= 0 && _dragZone < zones.Count:
                {
                    Vector2 norm = GuiToNorm(art, e.mousePosition);
                    Vector2 delta = norm - _dragStartNorm;
                    var z = zones[_dragZone];
                    if (_draggingSize)
                        z.size = new Vector2(
                            Mathf.Max(0.01f, _dragStartSize.x + delta.x * 2f),
                            Mathf.Max(0.01f, _dragStartSize.y - delta.y * 2f));
                    else
                        z.center = _dragStartCenter + delta;
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseUp when _dragZone >= 0:
                    _dragZone = -1;
                    Save();
                    e.Use();
                    break;
            }
        }

        void BeginDrag(Rect art, int index, bool size, VNTouchZone z)
        {
            Snapshot();
            _dragZone = index;
            _draggingSize = size;
            _dragStartNorm = GuiToNorm(art, Event.current.mousePosition);
            _dragStartCenter = z.center;
            _dragStartSize = z.size;
        }

        // ---- 坐标变换（归一化 -0.5~0.5，y 向上；GUI y 向下） ----

        static Vector2 NormToGui(Rect art, Vector2 n) =>
            new Vector2(art.x + (n.x + 0.5f) * art.width,
                        art.y + (0.5f - n.y) * art.height);

        static Vector2 GuiToNorm(Rect art, Vector2 g) =>
            new Vector2((g.x - art.x) / art.width - 0.5f,
                        0.5f - (g.y - art.y) / art.height);

        static Rect ZoneRect(Rect art, VNTouchZone z)
        {
            var tl = NormToGui(art, new Vector2(z.center.x - z.size.x * 0.5f,
                                                z.center.y + z.size.y * 0.5f));
            var br = NormToGui(art, new Vector2(z.center.x + z.size.x * 0.5f,
                                                z.center.y - z.size.y * 0.5f));
            return Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
        }

        static Rect SizeHandleRect(Rect zoneRect) =>
            new Rect(zoneRect.xMax - HandleSize * 0.5f, zoneRect.yMax - HandleSize * 0.5f,
                HandleSize, HandleSize);

        static Rect FitRect(Rect area, float aspect)
        {
            float w = area.width, h = area.height;
            if (w / h > aspect) w = h * aspect; else h = w / aspect;
            return new Rect(area.center.x - w * 0.5f, area.center.y - h * 0.5f, w, h);
        }

        static void DrawSpriteRaw(Rect dst, Sprite sprite)
        {
            var texture = sprite.texture;
            if (texture == null) return;
            // 图集里的 sprite 不能整张 texture 当图用，必须按 textureRect 取 UV
            var src = sprite.textureRect;
            var uv = new Rect(src.x / texture.width, src.y / texture.height,
                src.width / texture.width, src.height / texture.height);
            GUI.DrawTextureWithTexCoords(dst, texture, uv, true);
        }

        // ------------------------------------------------------------------
        // 数据
        // ------------------------------------------------------------------

        List<VNTouchZone> EditingZones() =>
            _overrideIndex >= 0 && _overrideIndex < _def.overrides.Count
                ? _def.overrides[_overrideIndex].zones
                : _def.baseZones;

        /// <summary>编辑覆盖层时，基准里没被本层覆盖掉的那些框（灰线显示）</summary>
        IEnumerable<VNTouchZone> InheritedZones()
        {
            if (_overrideIndex < 0 || _overrideIndex >= _def.overrides.Count)
                yield break;
            var ov = _def.overrides[_overrideIndex];
            if (ov.replaceAll) yield break;

            foreach (var z in _def.baseZones)
            {
                if (z == null || string.IsNullOrEmpty(z.id)) continue;
                if (ov.zones.Any(o => o != null && o.id == z.id)) continue;
                if (ov.removeZoneIds.Contains(z.id)) continue;
                yield return z;
            }
        }

        Sprite CurrentSprite()
        {
            if (_overrideIndex >= 0 && _overrideIndex < _def.overrides.Count)
            {
                var ov = _def.overrides[_overrideIndex];
                if (ov.sprite != null) return ov.sprite;
                if (!string.IsNullOrEmpty(ov.expression) && _character != null)
                    return _character.GetSprite(ov.expression);
            }
            if (_character == null || _character.expressions.Count == 0) return null;
            int i = Mathf.Clamp(_expressionIndex, 0, _character.expressions.Count - 1);
            return _character.expressions[i].sprite;
        }

        void AddOverride()
        {
            Snapshot();
            _def.overrides.Add(new VNZoneSpriteOverride
            {
                sprite = CurrentSprite(),
                zones = new List<VNTouchZone>(),
            });
            _overrideIndex = _def.overrides.Count - 1;
            _selected = -1;
            Save();
        }

        void Save()
        {
            if (_def == null) return;
            _def.InvalidateCache();      // 运行时按 (sprite, 表情) 缓存过合成结果
            EditorUtility.SetDirty(_def);
            Repaint();
        }

        // ---- 窗口内独立撤销栈 ----

        string Serialize() => JsonUtility.ToJson(new ZoneList { zones = EditingZones() });

        void Snapshot()
        {
            _undo.Add(Serialize());
            if (_undo.Count > 50) _undo.RemoveAt(0);
            _redo.Clear();
        }

        void PerformUndo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(Serialize());
            ApplySnapshot(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
        }

        void PerformRedo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(Serialize());
            ApplySnapshot(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
        }

        void ApplySnapshot(string json)
        {
            var restored = JsonUtility.FromJson<ZoneList>(json);
            var target = EditingZones();
            target.Clear();
            if (restored != null && restored.zones != null) target.AddRange(restored.zones);
            _selected = -1;
            Save();
        }

        void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || !(e.control || e.command)) return;
            if (e.keyCode == KeyCode.Z) { PerformUndo(); e.Use(); }
            else if (e.keyCode == KeyCode.Y) { PerformRedo(); e.Use(); }
            else if (e.keyCode == KeyCode.S)
            {
                AssetDatabase.SaveAssets();
                ShowNotification(new GUIContent("已保存"));
                e.Use();
            }
        }

        static GUIStyle _selectedStyle;
        static GUIStyle Selected
        {
            get
            {
                if (_selectedStyle == null)
                {
                    _selectedStyle = new GUIStyle(EditorStyles.helpBox);
                    // static 样式在域重载后会丢，lazy 重建即可
                }
                return _selectedStyle;
            }
        }

        void OnLostFocus() => AssetDatabase.SaveAssets();
    }
}
