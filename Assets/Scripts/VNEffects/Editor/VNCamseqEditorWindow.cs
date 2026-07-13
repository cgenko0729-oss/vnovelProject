using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DG.Tweening;
using DG.Tweening.Core.Easing;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 镜头演出可视化编辑器（camseq）：
    ///   - 迷你画布：背景缩略图 + 各路径点的取景框 + 路径线，点击/拖动直接设点
    ///   - 路径点列表：拖拽排序、zoom/时长/缓动编辑
    ///   - 预览：进度条拖动或 ▶ 播放，取景框按真实缓动公式沿路径移动
    ///   - 文本双向：一键生成 camseq 文本到剪贴板；粘贴已有文本反向载入继续调
    /// 菜单：Tools → VN Effects → Camera Sequence Editor
    /// </summary>
    public class VNCamseqEditorWindow : EditorWindow
    {
        enum PointType { Anchor, Character, Coords }

        [System.Serializable]
        class Waypoint
        {
            public PointType type = PointType.Coords;
            public int anchorIndex = 4;   // middle
            public string charId = "";
            public int partIndex = 0;
            public int slotIndex = 1;     // 编辑态假定站位（center）
            public Vector2 coords;
            public float zoom = 1.4f;
            public float duration = 0.8f;
            public int easeIndex = 0;     // 0 = (默认)
        }

        // ---- 与运行时一致的常量/词汇 ----
        static readonly string[] AnchorTokens =
            { "topleft", "top", "topright", "left", "middle", "right", "bottomleft", "bottom", "bottomright" };
        static readonly Vector2[] AnchorPositions =
        {
            new Vector2(-620, 340), new Vector2(0, 340), new Vector2(620, 340),
            new Vector2(-620, 0), new Vector2(0, 0), new Vector2(620, 0),
            new Vector2(-620, -340), new Vector2(0, -340), new Vector2(620, -340),
        };
        static readonly string[] PartTokens = { "(中心)", "head", "chest", "waist", "feet", "up", "mid", "down" };
        static readonly float[] PartFracs = { 0f, 0.36f, 0.15f, -0.08f, -0.42f, 0.3f, 0f, -0.3f };
        static readonly string[] SlotNames = { "left", "center", "right" };
        static readonly float[] SlotX = { -380f, 0f, 380f };
        static readonly string[] EaseNames =
        {
            "(默认)", "Linear", "InSine", "OutSine", "InOutSine", "InQuad", "OutQuad",
            "InOutQuad", "InCubic", "OutCubic", "InOutCubic", "OutBack", "InOutBack", "OutExpo",
        };
        static readonly Vector2 CanvasHalf = new Vector2(960f, 540f);
        static readonly Vector2 Overscan = new Vector2(60f, 60f);

        readonly List<Waypoint> _points = new List<Waypoint>();
        ReorderableList _list;
        float _scrub;          // 0~总时长 的预览时间
        bool _playing;
        double _lastUpdateTime;
        string _pasteText = "";
        string _generatedText = "";
        Vector2 _scroll;

        // ---- 第二批：场景预览 / 画布拖拽 / 预设库 ----
        enum DragMode { None, Center, Corner }
        DragMode _dragMode;

        bool _scenePreviewing;
        RectTransform _zoomRoot;
        Vector2 _origPos;
        Vector3 _origScale;

        VNCamseqPresetLibrary _library;
        string _presetName = "";
        int _presetIndex;

        const string LibraryPath = "Assets/VNEffects/CamseqPresets.asset";

        [MenuItem("Tools/VN Effects/Camera Sequence Editor")]
        static void Open()
        {
            var win = GetWindow<VNCamseqEditorWindow>("镜头编排");
            win.minSize = new Vector2(560f, 720f);
        }

        void OnEnable()
        {
            _list = new ReorderableList(_points, typeof(Waypoint), true, true, true, true)
            {
                drawHeaderCallback = r => GUI.Label(r, "路径点（拖动左侧手柄排序 | 时长 0 = 瞬切）"),
                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 2f + 10f,
                drawElementCallback = DrawElement,
                onAddCallback = l => _points.Add(new Waypoint()),
            };
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);
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
            if (_scenePreviewing) ApplySceneState();
        }

        // ==================================================================
        // GUI
        // ==================================================================

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawToolbar();
            GUILayout.Space(4f);

            // 迷你画布（16:9）
            var canvasRect = GUILayoutUtility.GetAspectRect(16f / 9f);
            DrawCanvas(canvasRect);
            GUILayout.Space(4f);

            // 预览进度条
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
            GUILayout.Space(4f);

            _list.DoLayoutList();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 瞬切起手（时长0）"))
                    _points.Insert(0, new Waypoint { type = PointType.Anchor, anchorIndex = 2, zoom = 1.8f, duration = 0f });
                if (GUILayout.Button("+ 回原点收尾"))
                    _points.Add(new Waypoint { type = PointType.Anchor, anchorIndex = 4, zoom = 1f, duration = 1f });
            }

            GUILayout.Space(6f);
            GUILayout.Label("生成的剧本文本（粘贴进 .vn.txt）：", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(string.IsNullOrEmpty(_generatedText)
                ? "（点上方「生成文本」）" : _generatedText, GUILayout.MinHeight(70f));

            GUILayout.Space(6f);
            GUILayout.Label("解析已有 camseq 文本（粘贴后点「解析载入」）：", EditorStyles.boldLabel);
            _pasteText = EditorGUILayout.TextArea(_pasteText, GUILayout.MinHeight(60f));
            if (GUILayout.Button("解析载入")) ParseText();

            EditorGUILayout.HelpBox(
                "画布：点空白 = 给选中点设坐标；点取景中心 = 选中；拖动 = 移动；" +
                "拖选中框的四角 = 改 zoom。\n" +
                "场景预览：开启后拖进度条/按 ▶，Game 视图实时显示真实画面运镜，" +
                "关闭或进出 Play 自动还原（场景可能显示未保存标记，属正常）。\n" +
                "捕获当前镜头：把场景里 ZoomRoot 的当前状态反推成一个路径点" +
                "（可先手动摆好 ZoomRoot 再捕获）。\n" +
                "编辑态下「角色部位」按假定站位显示，Play 中按真实位置。" +
                "缓动默认：单段 InOutSine；多段首 InSine / 中 Linear / 末 OutSine（与运行时一致）。",
                MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_playing ? "■ 停止" : "▶ 预览", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    _playing = !_playing;
                    if (_playing)
                    {
                        if (_scrub >= TotalDuration() - 0.001f) _scrub = 0f;
                        _lastUpdateTime = EditorApplication.timeSinceStartup;
                    }
                }
                if (GUILayout.Button("生成文本→剪贴板", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                {
                    _generatedText = GenerateText();
                    EditorGUIUtility.systemCopyBuffer = _generatedText;
                    ShowNotification(new GUIContent("已复制到剪贴板"));
                }
                if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                {
                    if (EditorUtility.DisplayDialog("清空", "确定清空全部路径点？", "清空", "取消"))
                        _points.Clear();
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_points.Count} 个路径点", EditorStyles.miniLabel);
            }

            // 第二行：场景预览 / 捕获 / 预设库
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool newPreview = GUILayout.Toggle(_scenePreviewing, "场景预览",
                    EditorStyles.toolbarButton, GUILayout.Width(70f));
                if (newPreview != _scenePreviewing)
                {
                    if (newPreview) StartScenePreview();
                    else StopScenePreview();
                }

                if (GUILayout.Button("捕获当前镜头", EditorStyles.toolbarButton, GUILayout.Width(94f)))
                    CaptureCurrentCamera();

                GUILayout.Space(10f);
                GUILayout.Label("预设:", GUILayout.Width(34f));
                _presetName = GUILayout.TextField(_presetName, GUILayout.Width(90f));
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                    SavePreset();

                var names = PresetNames();
                _presetIndex = EditorGUILayout.Popup(_presetIndex, names, GUILayout.Width(110f));
                using (new EditorGUI.DisabledScope(names.Length == 0 || names[0] == "(无预设)"))
                {
                    if (GUILayout.Button("载入", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                        LoadPreset();
                    if (GUILayout.Button("删除", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                        DeletePreset();
                }
                GUILayout.FlexibleSpace();
            }
        }

        void DrawElement(Rect rect, int index, bool active, bool focused)
        {
            var w = _points[index];
            float line = EditorGUIUtility.singleLineHeight;
            var r1 = new Rect(rect.x, rect.y + 3f, rect.width, line);
            var r2 = new Rect(rect.x, rect.y + line + 7f, rect.width, line);

            // 第一行：编号 + 类型 + 目标
            float x = r1.x;
            GUI.Label(new Rect(x, r1.y, 24f, line), $"{index + 1}."); x += 26f;
            w.type = (PointType)EditorGUI.EnumPopup(new Rect(x, r1.y, 74f, line), w.type); x += 78f;

            float remain = r1.xMax - x;
            switch (w.type)
            {
                case PointType.Anchor:
                    w.anchorIndex = EditorGUI.Popup(new Rect(x, r1.y, remain, line), w.anchorIndex, AnchorTokens);
                    break;
                case PointType.Character:
                {
                    var ids = SceneCharacterIds();
                    float third = remain / 3f;
                    if (ids.Length > 0)
                    {
                        int cur = System.Array.IndexOf(ids, w.charId);
                        int sel = EditorGUI.Popup(new Rect(x, r1.y, third, line), Mathf.Max(0, cur), ids);
                        w.charId = ids[sel];
                    }
                    else
                    {
                        w.charId = EditorGUI.TextField(new Rect(x, r1.y, third, line), w.charId);
                    }
                    w.partIndex = EditorGUI.Popup(new Rect(x + third, r1.y, third, line), w.partIndex, PartTokens);
                    w.slotIndex = EditorGUI.Popup(new Rect(x + third * 2f, r1.y, third, line), w.slotIndex, SlotNames);
                    break;
                }
                case PointType.Coords:
                    w.coords = EditorGUI.Vector2Field(new Rect(x, r1.y, remain, line), GUIContent.none, w.coords);
                    break;
            }

            // 第二行：zoom / 时长 / 缓动
            x = r2.x + 26f;
            GUI.Label(new Rect(x, r2.y, 42f, line), "zoom"); x += 44f;
            w.zoom = EditorGUI.Slider(new Rect(x, r2.y, 160f, line), w.zoom, 0.5f, 3f); x += 166f;
            GUI.Label(new Rect(x, r2.y, 30f, line), "秒"); x += 32f;
            w.duration = EditorGUI.FloatField(new Rect(x, r2.y, 46f, line), w.duration); x += 52f;
            GUI.Label(new Rect(x, r2.y, 34f, line), "ease"); x += 36f;
            w.easeIndex = EditorGUI.Popup(new Rect(x, r2.y, Mathf.Max(70f, r2.xMax - x), line), w.easeIndex, EaseNames);
        }

        // ==================================================================
        // 迷你画布
        // ==================================================================

        void DrawCanvas(Rect rect)
        {
            // 底：背景缩略图或深色底
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.12f));
            var bgSprite = SceneBackgroundSprite();
            if (bgSprite != null)
                GUI.DrawTexture(rect, bgSprite.texture, ScaleMode.ScaleAndCrop);
            DrawRectOutline(rect, new Color(1f, 1f, 1f, 0.35f), 1f);

            // 站位参考剪影（left/center/right）
            for (int s = 0; s < 3; s++)
            {
                var p = CanvasToGui(rect, new Vector2(SlotX[s], -60f));
                float hw = 880f * 0.28f * rect.width / 1920f;
                float hh = 880f * 0.5f * rect.height / 1080f;
                EditorGUI.DrawRect(new Rect(p.x - hw * 0.5f, p.y - hh, hw, hh * 2f),
                    new Color(1f, 1f, 1f, 0.05f));
            }

            // 各路径点取景框 + 路径线
            Vector2? prevCenter = null;
            for (int i = 0; i < _points.Count; i++)
            {
                var state = TargetState(_points[i]);
                var center = -state.offset / state.zoom;      // 取景中心（画布坐标）
                var half = CanvasHalf / state.zoom;

                bool selected = _list.index == i;
                var color = selected
                    ? new Color(1f, 0.85f, 0.2f, 0.95f)
                    : new Color(0.3f, 0.85f, 1f, 0.55f);
                DrawCanvasFrame(rect, center, half, color, selected ? 2f : 1f);

                var guiCenter = CanvasToGui(rect, center);
                GUI.Label(new Rect(guiCenter.x - 8f, guiCenter.y - 9f, 30f, 18f),
                    (i + 1).ToString(), EditorStyles.whiteBoldLabel);

                if (prevCenter.HasValue)
                    DrawDottedLine(rect, prevCenter.Value, center, new Color(1f, 1f, 1f, 0.5f));
                prevCenter = center;
            }

            // 预览取景框（沿路径插值）
            if (_points.Count > 0)
            {
                var s = StateAtTime(_scrub);
                var center = -s.offset / s.zoom;
                DrawCanvasFrame(rect, center, CanvasHalf / s.zoom, Color.white, 2.5f);
            }

            HandleCanvasInput(rect);
        }

        void HandleCanvasInput(Rect rect)
        {
            var e = Event.current;

            if (e.type == EventType.MouseUp)
            {
                _dragMode = DragMode.None;
                return;
            }
            if (!rect.Contains(e.mousePosition)) return;

            bool hasSelection = _list.index >= 0 && _list.index < _points.Count;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var click = GuiToCanvas(rect, e.mousePosition);

                // 1) 选中路径点的取景框四角（GUI 12px 内）→ 拖角改 zoom
                if (hasSelection)
                {
                    var st = TargetState(_points[_list.index]);
                    var center = -st.offset / st.zoom;
                    var half = CanvasHalf / st.zoom;
                    for (int cx = -1; cx <= 1; cx += 2)
                    for (int cy = -1; cy <= 1; cy += 2)
                    {
                        var cornerGui = CanvasToGui(rect,
                            center + new Vector2(half.x * cx, half.y * cy));
                        if (Vector2.Distance(cornerGui, e.mousePosition) < 12f)
                        {
                            _dragMode = DragMode.Corner;
                            e.Use();
                            return;
                        }
                    }
                }

                // 2) 任意取景中心 60 画布单位内 → 选中该点并可拖动
                int nearest = -1;
                float best = 60f;
                for (int i = 0; i < _points.Count; i++)
                {
                    var st = TargetState(_points[i]);
                    float d = Vector2.Distance(-st.offset / st.zoom, click);
                    if (d < best) { best = d; nearest = i; }
                }
                if (nearest >= 0)
                {
                    _list.index = nearest;
                    _dragMode = DragMode.Center;
                }
                else if (hasSelection)
                {
                    // 3) 空白处点击 = 给选中点设坐标
                    var w = _points[_list.index];
                    w.type = PointType.Coords;
                    w.coords = Round(click);
                    _dragMode = DragMode.Center;
                }
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && hasSelection)
            {
                var w = _points[_list.index];
                if (_dragMode == DragMode.Corner)
                {
                    // 拖角改 zoom：以取景中心为基准，指针到中心的距离 = 新的取景半宽/半高
                    var st = TargetState(w);
                    var center = -st.offset / st.zoom;
                    var mouse = GuiToCanvas(rect, e.mousePosition);
                    var half = new Vector2(
                        Mathf.Max(20f, Mathf.Abs(mouse.x - center.x)),
                        Mathf.Max(12f, Mathf.Abs(mouse.y - center.y)));
                    float zoom = Mathf.Max(CanvasHalf.x / half.x, CanvasHalf.y / half.y);
                    w.zoom = Mathf.Clamp(zoom, 0.5f, 3f);
                }
                else if (_dragMode == DragMode.Center)
                {
                    w.type = PointType.Coords;
                    w.coords = Round(GuiToCanvas(rect, e.mousePosition));
                }
                e.Use();
                Repaint();
            }
        }

        // ==================================================================
        // 场景内实时预览 / 捕获当前镜头
        // ==================================================================

        RectTransform FindZoomRoot()
        {
            var cam = Object.FindFirstObjectByType<VNCamera>();
            if (cam != null && cam.target != null) return cam.target;
            var go = GameObject.Find("ZoomRoot");
            return go != null ? go.transform as RectTransform : null;
        }

        void StartScenePreview()
        {
            _zoomRoot = FindZoomRoot();
            if (_zoomRoot == null)
            {
                ShowNotification(new GUIContent("场景里找不到 ZoomRoot（先生成剧本演示场景）"));
                return;
            }
            _origPos = _zoomRoot.anchoredPosition;
            _origScale = _zoomRoot.localScale;
            _scenePreviewing = true;
            ApplySceneState();
        }

        void StopScenePreview()
        {
            if (!_scenePreviewing) return;
            _scenePreviewing = false;
            if (_zoomRoot != null)
            {
                _zoomRoot.anchoredPosition = _origPos;
                _zoomRoot.localScale = _origScale;
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        void ApplySceneState()
        {
            if (_zoomRoot == null)
            {
                StopScenePreview();
                return;
            }
            var s = StateAtTime(_scrub);
            _zoomRoot.localScale = Vector3.one * s.zoom;
            _zoomRoot.anchoredPosition = _origPos + s.offset;
            EditorApplication.QueuePlayerLoopUpdate(); // 编辑态强制刷新 Game 视图
        }

        /// <summary>把 ZoomRoot 当前的实际状态反推成一个路径点（坐标类型）</summary>
        void CaptureCurrentCamera()
        {
            var root = _zoomRoot != null ? _zoomRoot : FindZoomRoot();
            if (root == null)
            {
                ShowNotification(new GUIContent("场景里找不到 ZoomRoot"));
                return;
            }
            // 预览中用记录的基准位；否则假定基准为当前值即无偏移的 (0,0)
            var basePos = _scenePreviewing ? _origPos : Vector2.zero;
            float zoom = Mathf.Max(0.1f, root.localScale.x);
            Vector2 offset = root.anchoredPosition - basePos;
            Vector2 point = -offset / zoom;

            _points.Add(new Waypoint
            {
                type = PointType.Coords,
                coords = Round(point),
                zoom = Mathf.Clamp(zoom, 0.5f, 3f),
                duration = 0.8f,
            });
            _list.index = _points.Count - 1;
            ShowNotification(new GUIContent($"已捕获：({point.x:0},{point.y:0}) ×{zoom:0.##}"));
        }

        // ==================================================================
        // 预设库
        // ==================================================================

        VNCamseqPresetLibrary EnsureLibrary()
        {
            if (_library != null) return _library;
            _library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);
            if (_library == null)
            {
                _library = CreateInstance<VNCamseqPresetLibrary>();
                AssetDatabase.CreateAsset(_library, LibraryPath);
                AssetDatabase.SaveAssets();
            }
            return _library;
        }

        string[] PresetNames()
        {
            if (_library == null)
                _library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);
            if (_library == null || _library.presets.Count == 0)
                return new[] { "(无预设)" };
            var names = new string[_library.presets.Count];
            for (int i = 0; i < names.Length; i++) names[i] = _library.presets[i].name;
            return names;
        }

        void SavePreset()
        {
            if (_points.Count == 0)
            {
                ShowNotification(new GUIContent("没有路径点可保存"));
                return;
            }
            string name = string.IsNullOrEmpty(_presetName.Trim())
                ? $"预设{System.DateTime.Now:HHmmss}" : _presetName.Trim();

            var lib = EnsureLibrary();
            var existing = lib.presets.Find(p => p.name == name);
            if (existing != null) existing.camseqText = GenerateText(); // 同名覆盖
            else lib.presets.Add(new VNCamseqPresetLibrary.Preset
                { name = name, camseqText = GenerateText() });

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            _presetIndex = lib.presets.FindIndex(p => p.name == name);
            ShowNotification(new GUIContent($"已保存预设「{name}」"));
        }

        void LoadPreset()
        {
            if (_library == null || _presetIndex < 0 || _presetIndex >= _library.presets.Count) return;
            _pasteText = _library.presets[_presetIndex].camseqText;
            ParseText();
            _presetName = _library.presets[_presetIndex].name;
        }

        void DeletePreset()
        {
            if (_library == null || _presetIndex < 0 || _presetIndex >= _library.presets.Count) return;
            string name = _library.presets[_presetIndex].name;
            if (!EditorUtility.DisplayDialog("删除预设", $"删除「{name}」？", "删除", "取消")) return;
            _library.presets.RemoveAt(_presetIndex);
            _presetIndex = 0;
            EditorUtility.SetDirty(_library);
            AssetDatabase.SaveAssets();
        }

        static Vector2 Round(Vector2 v) =>
            new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));

        // 画布坐标（中心原点，y 向上）↔ GUI 像素
        static Vector2 CanvasToGui(Rect rect, Vector2 canvas)
        {
            return new Vector2(
                rect.x + (canvas.x + CanvasHalf.x) / (CanvasHalf.x * 2f) * rect.width,
                rect.y + (1f - (canvas.y + CanvasHalf.y) / (CanvasHalf.y * 2f)) * rect.height);
        }

        static Vector2 GuiToCanvas(Rect rect, Vector2 gui)
        {
            return new Vector2(
                (gui.x - rect.x) / rect.width * CanvasHalf.x * 2f - CanvasHalf.x,
                (1f - (gui.y - rect.y) / rect.height) * CanvasHalf.y * 2f - CanvasHalf.y);
        }

        void DrawCanvasFrame(Rect rect, Vector2 center, Vector2 half, Color color, float thickness)
        {
            var tl = CanvasToGui(rect, new Vector2(center.x - half.x, center.y + half.y));
            var br = CanvasToGui(rect, new Vector2(center.x + half.x, center.y - half.y));
            DrawRectOutline(Rect.MinMaxRect(tl.x, tl.y, br.x, br.y), color, thickness);
        }

        static void DrawRectOutline(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        void DrawDottedLine(Rect rect, Vector2 fromCanvas, Vector2 toCanvas, Color color)
        {
            const int dots = 24;
            for (int i = 0; i <= dots; i++)
            {
                var p = CanvasToGui(rect, Vector2.Lerp(fromCanvas, toCanvas, i / (float)dots));
                EditorGUI.DrawRect(new Rect(p.x - 1f, p.y - 1f, 2f, 2f), color);
            }
        }

        // ==================================================================
        // 预览插值（与运行时同一套公式）
        // ==================================================================

        struct CamState { public Vector2 offset; public float zoom; }

        Vector2 PreviewPoint(Waypoint w)
        {
            switch (w.type)
            {
                case PointType.Anchor: return AnchorPositions[Mathf.Clamp(w.anchorIndex, 0, 8)];
                case PointType.Coords: return w.coords;
                case PointType.Character:
                {
                    float height = 880f;
                    Vector2 offset = Vector2.zero;
                    var stage = Object.FindFirstObjectByType<VNStage>();
                    if (stage != null)
                    {
                        var def = stage.characters.Find(c => c != null && c.id == w.charId);
                        if (def != null)
                        {
                            height = stage.characterHeight * Mathf.Max(0.05f, def.sizeScale);
                            offset = def.positionOffset;
                        }
                    }
                    var basePos = new Vector2(SlotX[Mathf.Clamp(w.slotIndex, 0, 2)], -60f) + offset;
                    float frac = PartFracs[Mathf.Clamp(w.partIndex, 0, PartFracs.Length - 1)];
                    return basePos + new Vector2(0f, height * frac);
                }
            }
            return Vector2.zero;
        }

        CamState TargetState(Waypoint w)
        {
            float zoom = Mathf.Max(0.1f, w.zoom);
            return new CamState
            {
                zoom = zoom,
                offset = VNCamera.ComputeOffset(PreviewPoint(w), zoom, CanvasHalf, Overscan, true),
            };
        }

        float TotalDuration()
        {
            float t = 0f;
            foreach (var w in _points) t += Mathf.Max(0f, w.duration);
            return t;
        }

        Ease EaseFor(int index, int firstMove, int lastMove, int moveCount)
        {
            var w = _points[index];
            if (w.easeIndex > 0 &&
                System.Enum.TryParse(EaseNames[w.easeIndex], true, out Ease custom))
                return custom;
            if (moveCount <= 1) return Ease.InOutSine;
            if (index == firstMove) return Ease.InSine;
            if (index == lastMove) return Ease.OutSine;
            return Ease.Linear;
        }

        CamState StateAtTime(float time)
        {
            var state = new CamState { offset = Vector2.zero, zoom = 1f };
            if (_points.Count == 0) return state;

            int firstMove = -1, lastMove = -1, moveCount = 0;
            for (int i = 0; i < _points.Count; i++)
            {
                if (_points[i].duration > 0.001f)
                {
                    if (firstMove < 0) firstMove = i;
                    lastMove = i;
                    moveCount++;
                }
            }

            float t = time;
            for (int i = 0; i < _points.Count; i++)
            {
                var target = TargetState(_points[i]);
                float dur = _points[i].duration;
                if (dur <= 0.001f)
                {
                    state = target; // 瞬切
                    continue;
                }
                if (t >= dur)
                {
                    state = target;
                    t -= dur;
                    continue;
                }
                float eased = EaseManager.Evaluate(
                    EaseFor(i, firstMove, lastMove, moveCount), null, t, dur, 1.70158f, 0f);
                state.offset = Vector2.LerpUnclamped(state.offset, target.offset, eased);
                state.zoom = Mathf.LerpUnclamped(state.zoom, target.zoom, eased);
                return state;
            }
            return state;
        }

        // ==================================================================
        // 文本生成 / 解析
        // ==================================================================

        string PointToken(Waypoint w)
        {
            switch (w.type)
            {
                case PointType.Anchor:
                    return AnchorTokens[Mathf.Clamp(w.anchorIndex, 0, 8)];
                case PointType.Character:
                    return w.partIndex > 0 ? $"{w.charId}:{PartTokens[w.partIndex]}" : w.charId;
                default:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0:0.#},{1:0.#}", w.coords.x, w.coords.y);
            }
        }

        string GenerateText()
        {
            var sb = new StringBuilder("camseq\n");
            foreach (var w in _points)
            {
                sb.Append("> ").Append(PointToken(w))
                  .Append(' ').Append(w.zoom.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append(' ').Append(w.duration.ToString("0.##", CultureInfo.InvariantCulture));
                if (w.easeIndex > 0) sb.Append(" ease:").Append(EaseNames[w.easeIndex]);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        void ParseText()
        {
            var commands = VNScriptParser.Parse(_pasteText);
            VNScriptCommand camseq = null;
            foreach (var c in commands)
                if (c.keyword == "camseq" && c.camPoints != null && c.camPoints.Count > 0)
                {
                    camseq = c;
                    break;
                }
            if (camseq == null)
            {
                ShowNotification(new GUIContent("没有找到含路径点的 camseq 块"));
                return;
            }

            _points.Clear();
            foreach (var def in camseq.camPoints)
            {
                var w = new Waypoint { zoom = def.zoom, duration = def.duration };

                int anchor = System.Array.IndexOf(AnchorTokens, def.point.ToLower());
                if (def.point.ToLower() == "center" || def.point.ToLower() == "origin"
                    || def.point.ToLower() == "reset") anchor = 4;

                if (anchor >= 0)
                {
                    w.type = PointType.Anchor;
                    w.anchorIndex = anchor;
                }
                else if (def.point.Contains(","))
                {
                    w.type = PointType.Coords;
                    var parts = def.point.Split(',');
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out w.coords.x);
                    if (parts.Length > 1)
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out w.coords.y);
                }
                else
                {
                    w.type = PointType.Character;
                    int colon = def.point.IndexOf(':');
                    w.charId = colon > 0 ? def.point.Substring(0, colon) : def.point;
                    if (colon > 0)
                    {
                        int part = System.Array.IndexOf(PartTokens, def.point.Substring(colon + 1).ToLower());
                        w.partIndex = Mathf.Max(0, part);
                    }
                }

                if (!string.IsNullOrEmpty(def.ease))
                {
                    for (int i = 1; i < EaseNames.Length; i++)
                        if (string.Equals(EaseNames[i], def.ease, System.StringComparison.OrdinalIgnoreCase))
                        {
                            w.easeIndex = i;
                            break;
                        }
                }
                _points.Add(w);
            }
            _scrub = 0f;
            ShowNotification(new GUIContent($"已载入 {_points.Count} 个路径点"));
        }

        // ==================================================================
        // 场景查询
        // ==================================================================

        string[] SceneCharacterIds()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            if (stage == null) return new string[0];
            var ids = new List<string>();
            foreach (var c in stage.characters)
                if (c != null && !string.IsNullOrEmpty(c.id)) ids.Add(c.id);
            return ids.ToArray();
        }

        Sprite SceneBackgroundSprite()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            if (stage != null && stage.backgroundImage != null && stage.backgroundImage.sprite != null)
                return stage.backgroundImage.sprite;
            return null;
        }
    }

    /// <summary>
    /// 镜头预设库：以 camseq 文本形式保存常用运镜（存/取都走文本双向通道，
    /// 与手写剧本 100% 一致）。资产：Assets/VNEffects/CamseqPresets.asset。
    /// </summary>
    public class VNCamseqPresetLibrary : ScriptableObject
    {
        [System.Serializable]
        public class Preset
        {
            public string name;
            [TextArea(3, 10)] public string camseqText;
        }

        public List<Preset> presets = new List<Preset>();
    }
}
