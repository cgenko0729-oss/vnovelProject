using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 飘落天气预览与调参窗口：Tools → VN Effects → Weather Preview。
    ///
    /// 【为什么要这个窗口】
    /// 飘落效果的参数（密度/风力/摆幅/翻转速度/三层景深…）光看数字没人调得出来，
    /// 必须边看边调。这里做了两件事：
    ///   1. 编辑模式下就能看形状 —— 直接把程序化图集按帧播放出来，
    ///      四种形态变体一起放大显示，一眼看出樱花瓣/枫叶/银杏长什么样、翻转是否自然
    ///   2. 运行模式下滑杆实时生效 —— 改一个数字，场景里的天气立刻重灌参数
    /// 满意之后「另存为资产」+「登记进 VNGameConfig」两个按钮收工。
    /// </summary>
    public class VNWeatherPreviewWindow : EditorWindow
    {
        [MenuItem("Tools/VN Effects/Weather Preview", priority = 110)]
        static void Open()
        {
            var w = GetWindow<VNWeatherPreviewWindow>("飘落天气");
            w.minSize = new Vector2(400f, 560f);
        }

        const string DefaultFolder = "Assets/VNEffects/Weather";

        VNWeatherDef _def;              // 正在编辑的对象（资产或内置临时件）
        VNLeafShape _newShape = VNLeafShape.Sakura;
        Editor _inspector;
        Vector2 _scroll;
        bool _autoApply = true;
        bool _animate = true;
        int _frame;
        double _lastStep;
        int _lastHash;

        void OnEnable()
        {
            EnsureDef();
            EditorApplication.update += Step;
        }

        void OnDisable()
        {
            EditorApplication.update -= Step;
            DisposeInspector();
            DisposeTempDef();
        }

        void DisposeInspector()
        {
            if (_inspector != null) { DestroyImmediate(_inspector); _inspector = null; }
        }

        /// <summary>只销毁自己造的临时件，资产绝不能碰</summary>
        void DisposeTempDef()
        {
            if (_def != null && !AssetDatabase.Contains(_def)) DestroyImmediate(_def);
            _def = null;
        }

        void EnsureDef()
        {
            if (_def == null) _def = VNWeatherDef.CreateBuiltin(_newShape);
            _def.EnsureLayers();
        }

        void Step()
        {
            if (!_animate) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastStep < 0.085) return;
            _lastStep = now;
            _frame = (_frame + 1) % VNFoliageTextures.FlipFrames;
            Repaint();
        }

        // ------------------------------------------------------------------

        void OnGUI()
        {
            EnsureDef();

            DrawSourceBar();
            EditorGUILayout.Space(4f);
            DrawFlipPreview();
            EditorGUILayout.Space(4f);
            DrawApplyBar();
            EditorGUILayout.Space(6f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawInspector();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            DrawSaveBar();
        }

        void DrawSourceBar()
        {
            EditorGUILayout.LabelField("参数来源", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var picked = (VNWeatherDef)EditorGUILayout.ObjectField(
                    "资产", AssetDatabase.Contains(_def) ? _def : null,
                    typeof(VNWeatherDef), false);
                if (picked != null && picked != _def)
                {
                    DisposeTempDef();
                    DisposeInspector();
                    _def = picked;
                    _def.EnsureLayers();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _newShape = (VNLeafShape)EditorGUILayout.EnumPopup("内置预设", _newShape);
                if (GUILayout.Button("载入", GUILayout.Width(60f)))
                {
                    DisposeTempDef();
                    DisposeInspector();
                    _def = VNWeatherDef.CreateBuiltin(_newShape);
                }
            }

            if (!AssetDatabase.Contains(_def))
                EditorGUILayout.HelpBox(
                    "当前是内置临时预设，改动不会保存。满意后点下面的「另存为资产」。",
                    MessageType.Info);
        }

        /// <summary>
        /// 翻转帧预览：四种形态变体各画一格，按帧循环播放。
        /// 这一栏就是判断叶型做得像不像的地方 —— 宽度随翻转呼吸、背面明显更暗，
        /// 这两条对上了，扔进场景里就不会是「纸片」。
        /// </summary>
        void DrawFlipPreview()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"翻转预览　帧 {_frame + 1}/{VNFoliageTextures.FlipFrames}",
                    EditorStyles.boldLabel);
                _animate = GUILayout.Toggle(_animate, "播放", EditorStyles.miniButton,
                    GUILayout.Width(50f));
                if (GUILayout.Button("◀", EditorStyles.miniButton, GUILayout.Width(26f)))
                    _frame = (_frame + VNFoliageTextures.FlipFrames - 1) % VNFoliageTextures.FlipFrames;
                if (GUILayout.Button("▶", EditorStyles.miniButton, GUILayout.Width(26f)))
                    _frame = (_frame + 1) % VNFoliageTextures.FlipFrames;
            }

            var atlas = VNFoliageTextures.Atlas(_def.shape);
            if (atlas == null) return;

            float cell = Mathf.Min(96f, (position.width - 40f) / VNFoliageTextures.Variants);
            var rect = GUILayoutUtility.GetRect(position.width, cell + 6f);
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.17f, 0.19f));

            float uvW = 1f / VNFoliageTextures.FlipFrames;
            float uvH = 1f / VNFoliageTextures.Variants;
            var prevColor = GUI.color;
            for (int row = 0; row < VNFoliageTextures.Variants; row++)
            {
                var cellRect = new Rect(rect.x + 8f + row * (cell + 4f), rect.y + 3f, cell, cell);
                // 用色带上对应位置的颜色着色 —— 预览要能看出秋叶的红橙黄褐色差
                float t = VNFoliageTextures.Variants > 1
                    ? row / (float)(VNFoliageTextures.Variants - 1) : 0f;
                GUI.color = _def.colors != null ? _def.colors.Evaluate(t) : Color.white;
                // 图集行 0 在贴图顶部，GUI 的 texcoord y 向上 → 行序要倒过来换算
                GUI.DrawTextureWithTexCoords(cellRect, atlas,
                    new Rect(_frame * uvW, 1f - (row + 1f) * uvH, uvW, uvH));
            }
            GUI.color = prevColor;
        }

        void DrawApplyBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _autoApply = EditorGUILayout.ToggleLeft(
                    "运行时实时应用", _autoApply, GUILayout.Width(120f));

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("应用到场景"))
                        ApplyToScene();
                    if (GUILayout.Button("刮一阵风", GUILayout.Width(80f)))
                        FindController()?.Gust(3.2f);
                }
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox(
                    "进入 Play Mode 后，改动会实时反映到场景里的 VNWeatherController。",
                    MessageType.None);
        }

        void DrawInspector()
        {
            if (_inspector == null || _inspector.target != _def)
            {
                DisposeInspector();
                _inspector = Editor.CreateEditor(_def);
            }
            if (_inspector == null) return;

            EditorGUI.BeginChangeCheck();
            _inspector.OnInspectorGUI();
            bool changed = EditorGUI.EndChangeCheck();

            // Gradient 之类的控件不总能被 ChangeCheck 抓到，补一层内容哈希兜底
            int hash = ContentHash();
            if (hash != _lastHash) { _lastHash = hash; changed = true; }

            if (changed && _autoApply && Application.isPlaying) ApplyToScene();
        }

        int ContentHash()
        {
            unchecked
            {
                int h = (int)_def.shape * 397;
                h = h * 31 + _def.density.GetHashCode();
                h = h * 31 + _def.fallSpeed.GetHashCode();
                h = h * 31 + _def.size.GetHashCode();
                h = h * 31 + _def.swayAmplitude.GetHashCode();
                h = h * 31 + _def.swayFrequency.GetHashCode();
                h = h * 31 + _def.spinSpeed.GetHashCode();
                h = h * 31 + _def.flipSpeed.GetHashCode();
                h = h * 31 + _def.windBase.GetHashCode();
                h = h * 31 + _def.gustStrength.GetHashCode();
                h = h * 31 + _def.gustFrequency.GetHashCode();
                h = h * 31 + _def.hdrBoost.GetHashCode();
                h = h * 31 + _def.sizeSpeedLink.GetHashCode();
                h = h * 31 + (_def.groundPile ? 1 : 0);
                if (_def.colors != null)
                    foreach (var k in _def.colors.colorKeys)
                        h = h * 31 + k.color.GetHashCode() + k.time.GetHashCode();
                foreach (var l in new[] { _def.far, _def.mid, _def.near })
                {
                    if (l == null) continue;
                    h = h * 31 + l.rateMul.GetHashCode() + l.sizeMul.GetHashCode() +
                        l.speedMul.GetHashCode() + l.alpha.GetHashCode() +
                        l.aerial.GetHashCode() + l.blur.GetHashCode() +
                        l.sortingOrder + (l.enabled ? 1 : 0);
                }
                return h;
            }
        }

        void DrawSaveBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(AssetDatabase.Contains(_def) ? "保存资产" : "另存为资产"))
                    SaveAsset();
                using (new EditorGUI.DisabledScope(!AssetDatabase.Contains(_def)))
                {
                    if (GUILayout.Button("登记进 VNGameConfig"))
                        RegisterToConfig();
                }
            }
        }

        // ------------------------------------------------------------------

        static VNWeatherController FindController() =>
            UnityEngine.Object.FindFirstObjectByType<VNWeatherController>();

        void ApplyToScene()
        {
            var ctrl = FindController();
            if (ctrl == null)
            {
                Debug.LogWarning("[VNEffects] 场景里没有 VNWeatherController，无法预览。");
                return;
            }
            ctrl.PreviewDef(_def);
        }

        void SaveAsset()
        {
            if (AssetDatabase.Contains(_def))
            {
                EditorUtility.SetDirty(_def);
                AssetDatabase.SaveAssets();
                Debug.Log($"[VNEffects] 已保存飘落天气资产「{_def.id}」。", _def);
                return;
            }

            System.IO.Directory.CreateDirectory(DefaultFolder);
            string suggested = string.IsNullOrEmpty(_def.id)
                ? $"VNWeather_{_def.shape}" : $"VNWeather_{_def.id}";
            string path = EditorUtility.SaveFilePanelInProject(
                "保存飘落天气资产", suggested, "asset", "选择保存位置", DefaultFolder);
            if (string.IsNullOrEmpty(path)) return;

            // 资产必须是干净的新实例：临时件带着 DontSave，直接 CreateAsset 会存不进去
            var asset = ScriptableObject.CreateInstance<VNWeatherDef>();
            asset.CopyFrom(_def);
            asset.id = string.IsNullOrEmpty(_def.id)
                ? System.IO.Path.GetFileNameWithoutExtension(path) : _def.id;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            DisposeTempDef();
            DisposeInspector();
            _def = asset;
            EditorGUIUtility.PingObject(asset);
            Debug.Log($"[VNEffects] 已创建飘落天气资产「{asset.id}」：{path}", asset);
        }

        void RegisterToConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (cfg == null)
            {
                Debug.LogWarning($"[VNEffects] 找不到 {VNGameConfig.AssetPath}，无法登记。");
                return;
            }
            if (cfg.weatherDefs == null)
                cfg.weatherDefs = new System.Collections.Generic.List<VNWeatherDef>();
            if (cfg.weatherDefs.Contains(_def))
            {
                Debug.Log($"[VNEffects] 「{_def.id}」已经在飘落天气库里了。", cfg);
                return;
            }
            cfg.weatherDefs.Add(_def);
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            VNGameConfig.ClearCache();
            Debug.Log($"[VNEffects] 已把「{_def.id}」登记进 VNGameConfig 的飘落天气库。", cfg);
        }
    }
}
