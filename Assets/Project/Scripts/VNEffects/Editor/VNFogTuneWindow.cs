using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 擦雾调参窗口（Tools → VN Effects → 预览 Preview → 擦雾调参 Fog Wipe Tuning）。
    ///
    /// 【为什么这个窗口是必需品而不是加分项】
    /// 本玩法刻意不做速度惩罚（擦到就掉），难度的**唯一**来源就是
    /// 「笔刷面积 vs 回雾速度」这两个数——手感全压在参数上，而参数不可能一次调对。
    /// 每改一个数就进一次 Play Mode 试玩，一轮要一分钟；在这里改，是即时的。
    ///
    /// 两块内容：
    ///   ① 上方**算出来的**预计通关秒数（不是试出来的）。公式见 EstimateSeconds：
    ///      每秒擦除面积 ≈ 笔刷直径 × 鼠标速度，除以重叠浪费，再减回雾速率。
    ///   ② 下方可交互的掩码预览：在画布上拖鼠标就是擦，回雾照参数实时跑。
    ///      用的是运行时同一个 VNFogMask —— 这里看到的行为和游戏里一模一样，
    ///      不是另写一套近似（同 VNShakeSpec / VNCamera 公式共用的习惯）。
    ///
    /// 预览只碰 VNFogMask 的数据，不碰资产以外的任何东西；关窗即销毁。
    /// </summary>
    public class VNFogTuneWindow : EditorWindow
    {
        /// <summary>估算用的典型拖动速度（屏幕像素/秒）。普通玩家认真擦大约就是这个量级。</summary>
        const float AssumedDragSpeed = 800f;

        /// <summary>重叠浪费系数：玩家不可能画出完美不重叠的覆盖路径</summary>
        const float OverlapFactor = 1.5f;

        const float ReferenceWidth = 1920f;
        const float ReferenceHeight = 1080f;

        VNFogWipeDef _def;
        Editor _defEditor;

        readonly VNFogMask _mask = new VNFogMask();
        Texture2D _preview;
        bool _built;
        bool _running = true;
        double _lastTime;

        Vector2 _lastUv;
        bool _hasLastUv;
        Vector2 _scroll;

        [MenuItem("Tools/VN Effects/预览 Preview/擦雾调参 Fog Wipe Tuning", priority = 172)]
        public static void Open()
        {
            var window = GetWindow<VNFogTuneWindow>("擦雾调参");
            window.minSize = new Vector2(520f, 620f);
            window.Show();
        }

        void OnEnable()
        {
            _lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            _mask.Destroy();
            if (_preview != null) DestroyImmediate(_preview);
            if (_defEditor != null) DestroyImmediate(_defEditor);
            _built = false;
        }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min(0.05f, (float)(now - _lastTime));
            _lastTime = now;

            if (!_built || _def == null || !_running) return;

            _mask.ErodeFromEdges(_def.edgeRate, dt);

            _blobTimer -= dt;
            if (_def.blobRate > 0f && _blobTimer <= 0f)
            {
                float interval = 0.5f * (_def.blobIntervalMin + _def.blobIntervalMax);
                float radius = ScreenToMask(Random.Range(_def.blobRadiusMin, _def.blobRadiusMax));
                _mask.FogBlob(
                    new Vector2(Random.Range(0.15f, 0.85f), Random.Range(0.15f, 0.85f)),
                    radius,
                    VNFogMask.BlobStrengthFor(_def.blobRate, interval, radius));
                _blobTimer = Random.Range(_def.blobIntervalMin, _def.blobIntervalMax);
            }

            _mask.Flush();
            RefreshPreview();
            Repaint();
        }

        float _blobTimer;

        void OnGUI()
        {
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _def = (VNFogWipeDef)EditorGUILayout.ObjectField(
                    "擦雾定义", _def, typeof(VNFogWipeDef), false);
                if (check.changed)
                {
                    if (_defEditor != null) { DestroyImmediate(_defEditor); _defEditor = null; }
                    ResetSim();
                }
            }

            if (_def == null)
            {
                EditorGUILayout.HelpBox(
                    "选一个 VNFogWipeDef 资产。\n" +
                    "没有的话跑一次 Tools → VN Effects → 场景装机 Install To Scene → 擦雾 Fog Wipe，" +
                    "它会造一个示例资产「浴室镜面」。", MessageType.Info);
                return;
            }

            DrawEstimate();
            EditorGUILayout.Space(6f);
            DrawCanvas();
            EditorGUILayout.Space(6f);
            DrawInspector();
        }

        // ------------------------------------------------------------------
        // ① 算出来的预计通关时间
        // ------------------------------------------------------------------

        void DrawEstimate()
        {
            float cover = CoveragePerSecond();
            float refog = _def.edgeRate + _def.blobRate;
            float net = cover - refog;

            EditorGUILayout.LabelField("预估（按典型拖速 800px/s 算）", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"擦除速率　{cover:0.0} %/秒　（笔刷 {_def.brushDiameter:0} px，已扣掉 1.5 倍重叠浪费）");
                EditorGUILayout.LabelField(
                    $"回雾速率　{refog:0.0} %/秒　（边缘 {_def.edgeRate:0.0} + 雾团 {_def.blobRate:0.0}）");
                EditorGUILayout.LabelField($"净推进　　{net:0.0} %/秒", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "（保守估计：边缘侵蚀只能吃已擦净的像素，实际掉得比设定值少，所以真实进度更快些）",
                    EditorStyles.miniLabel);
            }

            if (net <= 0.05f)
            {
                EditorGUILayout.HelpBox(
                    "净推进 ≤ 0：回雾比玩家擦得还快，这局**过不了**。\n" +
                    "把回雾调小，或者把笔刷调大。", MessageType.Error);
                return;
            }

            float toNormal = _def.targetNormal / net;
            float toPerfect = _def.targetPerfect / net;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"擦到「普通」{_def.targetNormal:0}%　约 {toNormal:0} 秒");
                EditorGUILayout.LabelField(
                    $"擦到「完美」{_def.targetPerfect:0}%　约 {toPerfect:0} 秒");
                EditorGUILayout.LabelField($"时限　　　　　　　 {_def.timeLimit:0} 秒");
            }

            // 手感评语：这一段就是「参数好不好」的唯一判据
            if (toNormal > _def.timeLimit)
                EditorGUILayout.HelpBox(
                    $"连「普通」都要 {toNormal:0} 秒 > 时限 {_def.timeLimit:0} 秒——太苛刻，" +
                    "认真擦的玩家也会失败。", MessageType.Error);
            else if (toPerfect < _def.timeLimit * 0.45f)
                EditorGUILayout.HelpBox(
                    $"「完美」只要 {toPerfect:0} 秒，不到时限的一半——几乎没有对抗感，" +
                    "玩家会觉得这段没有必要存在。把回雾调大或笔刷调小。", MessageType.Warning);
            else if (toPerfect > _def.timeLimit)
                EditorGUILayout.HelpBox(
                    $"「普通」拿得到，「完美」要 {toPerfect:0} 秒拿不到——" +
                    "作为「大部分人拿普通、高手才拿完美」的设定是合理的。", MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    $"「完美」约 {toPerfect:0} 秒 / 时限 {_def.timeLimit:0} 秒——" +
                    "紧张但拿得到，这是推荐的手感区间。", MessageType.Info);
        }

        /// <summary>每秒擦除面积占全屏的百分比（已扣重叠浪费）</summary>
        float CoveragePerSecond()
        {
            float area = _def.brushDiameter * AssumedDragSpeed;
            return area / (ReferenceWidth * ReferenceHeight) * 100f / OverlapFactor;
        }

        // ------------------------------------------------------------------
        // ② 可交互的掩码预览
        // ------------------------------------------------------------------

        void DrawCanvas()
        {
            EnsureSim();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"清晰度 {_mask.Clarity * 100f:0.0}%",
                    EditorStyles.boldLabel, GUILayout.Width(130f));
                _running = GUILayout.Toggle(_running, "回雾运行中", EditorStyles.miniButton,
                    GUILayout.Width(90f));
                if (GUILayout.Button("重置", EditorStyles.miniButton, GUILayout.Width(60f)))
                    ResetSim();
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("在画布上按住拖动 = 擦", EditorStyles.miniLabel);
            }

            float width = Mathf.Min(position.width - 30f, 480f);
            var rect = GUILayoutUtility.GetRect(width, width * 9f / 16f, GUILayout.ExpandWidth(false));

            if (_preview != null)
            {
                EditorGUI.DrawPreviewTexture(rect, _preview, null, ScaleMode.StretchToFill);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), Color.gray);
            }

            HandleCanvasInput(rect);
        }

        void HandleCanvasInput(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseUp) { _hasLastUv = false; _mask.EndStroke(); }
                return;
            }

            // 画布是上下翻的（GUI 的 y 向下、uv 的 y 向上）
            var uv = new Vector2(
                (e.mousePosition.x - rect.x) / rect.width,
                1f - (e.mousePosition.y - rect.y) / rect.height);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _mask.BeginStroke();
                _hasLastUv = true;
                _lastUv = uv;
                Stroke(uv);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _hasLastUv)
            {
                Stroke(uv);
                _lastUv = uv;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                _hasLastUv = false;
                _mask.EndStroke();
            }
        }

        void Stroke(Vector2 uv)
        {
            _mask.StrokeTo(uv, ScreenToMask(_def.brushDiameter * 0.5f),
                _def.brushFeather, _def.wipeStrength);
            _mask.Flush();
            RefreshPreview();
            Repaint();
        }

        void DrawInspector()
        {
            EditorGUILayout.LabelField("参数（直接改资产，改完立刻反映在上面）",
                EditorStyles.boldLabel);
            if (_defEditor == null) _defEditor = Editor.CreateEditor(_def);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _defEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------

        void EnsureSim()
        {
            if (_built) return;
            _mask.Build();
            _built = true;
            _blobTimer = 0f;
            RefreshPreview();
        }

        void ResetSim()
        {
            if (!_built) { EnsureSim(); return; }
            _mask.Reset();
            _blobTimer = 0f;
            _hasLastUv = false;
            RefreshPreview();
            Repaint();
        }

        /// <summary>把掩码画成灰度预览：黑 = 全是雾，白 = 擦净</summary>
        void RefreshPreview()
        {
            if (!_built) return;
            if (_preview == null)
                _preview = new Texture2D(VNFogMask.Width, VNFogMask.Height,
                    TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };

            var pixels = new Color32[VNFogMask.Width * VNFogMask.Height];
            for (int y = 0; y < VNFogMask.Height; y++)
                for (int x = 0; x < VNFogMask.Width; x++)
                {
                    byte v = (byte)(_mask.ValueAt(x, y) * 255f);
                    pixels[y * VNFogMask.Width + x] = new Color32(v, v, v, 255);
                }
            _preview.SetPixels32(pixels);
            _preview.Apply(false);
        }

        static float ScreenToMask(float screenPixels) =>
            screenPixels * VNFogMask.Width / ReferenceWidth;
    }
}
