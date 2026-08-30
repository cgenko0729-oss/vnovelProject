using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VNEffects.EditorTools; // VNFontAssetBuilder（预烘焙字体资产）

namespace VNEffects
{
    /// <summary>
    /// 「柔和渐变」对话框皮肤导出器：白渐变 / 粉渐变 / 黑渐变 三套。
    ///
    /// 【与经典款的区别】没有面板、没有边框、没有圆角——底是一条从屏幕底部
    /// 向上淡出的整屏渐变带（左右满屏，35% 屏高），台词居中压在画面上，
    /// 靠文字自身的描边 + 柔光保证亮背景下的可读性。参考商业 Galgame 的
    /// 「无框式」对话表现。经典款（程序化默认）完全不动，随时 `ui dialogue default` 切回。
    ///
    /// 【产物】Assets/VNEffects/UISkins/
    ///   Textures/VN_BottomGradient.png   底部渐变带（纯白 + alpha 曲线，颜色靠 Image.color 染）
    ///   Textures/VN_RoundedRect.png      名牌底板（与经典款共用，已存在则重新烘焙）
    ///   DialogueSkin_SoftWhite/Pink/Dark.prefab
    ///   Materials/VN_SoftText_*.mat      正文 TMP 材质（描边 + 柔光，Inspector 可调）
    /// 并自动登记进 VNGameConfig：id = 白渐变 / 粉渐变 / 黑渐变。
    ///
    /// 【重复执行】默认菜单「已存在就跳过」，不会冲掉你在 Inspector 里调过的
    /// 颜色/描边/布局；想恢复出厂设置走「(覆盖重建)」那一项。
    /// </summary>
    public static class VNSoftSkinExporter
    {
        const string MaterialDir = VNUiSkinExporter.SkinDir + "/Materials";
        const string GradientName = "VN_BottomGradient";

        // ---- 布局常数（1920×1080 参考分辨率；改这里就能整体调版式）----
        const float PanelHeight = 378f;      // 渐变带高度 = 35% 屏高
        const float BodySideMargin = 300f;   // 正文左右留白（决定居中文字的换行宽度）
        const float BodyBottomPad = 70f;     // 正文距屏幕底
        const float BodyTopPad = 140f;       // 正文顶部留给名牌的一行
        const float BodyFontSize = 36f;      // 正文字号（无框式靠字本身撑场面，比经典款大一号）
        const float NameTagX = 300f;         // 名牌左缘 = 正文左缘，视觉成一列
        const float NameTagY = -80f;         // 名牌相对渐变带顶边下移量

        /// <summary>一套皮肤的全部可调外观参数（新增一套 = 在 Specs 里加一行）</summary>
        struct SkinSpec
        {
            public string id;             // 剧本 ui dialogue <id> 写的字
            public string prefabName;
            public Color panelColor;      // 渐变带染色（alpha = 最浓处不透明度）
            public Color textColor;       // 正文顶点色
            public Color outlineColor;
            public float outlineWidth;
            public Color underlayColor;   // 柔光/投影
            public Vector2 underlayOffset;
            public float underlayDilate;
            public float underlaySoftness;
        }

        static readonly SkinSpec[] Specs =
        {
            // 白渐变：深墨字 + 白描边 + 白柔光（亮背景上最通透的一套）
            new SkinSpec
            {
                id = "白渐变", prefabName = "DialogueSkin_SoftWhite",
                panelColor = new Color(1f, 1f, 1f, 0.55f),
                textColor = new Color(0.12f, 0.10f, 0.11f, 1f),
                outlineColor = new Color(1f, 1f, 1f, 1f), outlineWidth = 0.10f,
                underlayColor = new Color(1f, 1f, 1f, 0.85f),
                underlayOffset = Vector2.zero, underlayDilate = 0.2f, underlaySoftness = 0.35f,
            },
            // 粉渐变：深粉字 + 白描边（截图那种少女向粉纱）
            new SkinSpec
            {
                id = "粉渐变", prefabName = "DialogueSkin_SoftPink",
                panelColor = new Color(1f, 0.70f, 0.78f, 0.55f),
                textColor = new Color(0.78f, 0.20f, 0.38f, 1f),
                outlineColor = new Color(1f, 1f, 1f, 1f), outlineWidth = 0.12f,
                underlayColor = new Color(1f, 1f, 1f, 0.9f),
                underlayOffset = Vector2.zero, underlayDilate = 0.22f, underlaySoftness = 0.35f,
            },
            // 黑渐变：经典的黑底白字，但去掉金色描边框、底改成整屏渐变
            new SkinSpec
            {
                id = "黑渐变", prefabName = "DialogueSkin_SoftDark",
                // 比白/粉两套浓（0.55 → 0.80）：白字要压住亮背景，得有经典款那种黑底的
                // 遮盖力，0.55 的黑纱在浅色背景上白字会发虚看不清
                panelColor = new Color(0.04f, 0.05f, 0.09f, 0.80f),
                textColor = new Color(1f, 1f, 1f, 1f),
                outlineColor = new Color(0f, 0f, 0f, 0.85f), outlineWidth = 0.10f,
                underlayColor = new Color(0f, 0f, 0f, 0.75f),
                underlayOffset = new Vector2(0.5f, -0.5f), underlayDilate = 0.15f,
                underlaySoftness = 0.35f,
            },
        };

        [MenuItem("Tools/VN Effects/UI 皮肤 UI Skins/导出无框渐变皮肤（白·粉·黑）Export Soft Gradient Skins", priority = 121)]
        public static void Export() => Run(overwrite: false);

        [MenuItem("Tools/VN Effects/UI 皮肤 UI Skins/导出无框渐变皮肤·覆盖重建 Export Soft Gradient Skins (Overwrite)", priority = 122)]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog("覆盖重建柔和渐变皮肤",
                    "会用出厂参数覆盖 DialogueSkin_SoftWhite/Pink/Dark 三个 prefab 与它们的正文材质，\n" +
                    "你在 Inspector 里调过的颜色、描边、布局都会丢失。继续？", "覆盖重建", "取消"))
                return;
            Run(overwrite: true);
        }

        static void Run(bool overwrite)
        {
            VNUiSkinExporter.EnsureFolder(VNUiSkinExporter.SkinDir);
            VNUiSkinExporter.EnsureFolder(VNUiSkinExporter.TextureDir);
            VNUiSkinExporter.EnsureFolder(MaterialDir);

            var gradient = BakeGradientSprite();
            // 名牌底板沿用经典款那张圆角图（用户选择「三套都沿用现在的紫色名牌」）
            var rounded = VNUiSkinExporter.BakeSprite("VN_RoundedRect",
                VNProceduralTextures.RoundedRectSprite.texture, new Vector4(22, 22, 22, 22));

            var built = new GameObject[Specs.Length];
            int skipped = 0;
            for (int i = 0; i < Specs.Length; i++)
            {
                string path = $"{VNUiSkinExporter.SkinDir}/{Specs[i].prefabName}.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (existing != null && !overwrite)
                {
                    built[i] = existing; // 已存在：保留用户改动，只确保仍登记在配置里
                    skipped++;
                    continue;
                }
                built[i] = BuildPrefab(Specs[i], gradient, rounded, overwrite);
            }

            RegisterInConfig(built);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(VNUiSkinExporter.SkinDir));

            Debug.Log($"[VNSoftSkin] 柔和渐变皮肤已就绪（新建 {Specs.Length - skipped} 套 / 沿用 {skipped} 套）：\n" +
                      "  白渐变 / 粉渐变 / 黑渐变 → 剧本写 `ui dialogue 白渐变` 切换，" +
                      "`ui dialogue default` 回经典款。\n" +
                      $"  想微调外观：直接改 {VNUiSkinExporter.SkinDir} 下的 prefab" +
                      "（正文颜色在 Body 的 TextMeshPro Color；描边/柔光在 Materials 里那三个 .mat）。");
        }

        // ==============================================================
        // 底部渐变带贴图：纯白 + alpha 曲线，颜色交给 Image.color
        // ==============================================================

        /// <summary>
        /// 8×512 的竖直渐变（y=0 是底部 = 最浓）。做成白色只留 alpha 的原因：
        /// 三套皮肤共用同一张图，换色 = 在 Inspector 里拉 Image 的 Color，无需重烘。
        ///
        /// 曲线是「下半段满浓 + 上半段 SmoothStep 淡出」而不是从底一路衰减：
        /// 台词落在渐变带的中上部（正文顶 ≈ 带高的 63%），一路衰减的话文字那一带
        /// 只剩两三成遮盖，白字压在亮背景上会发虚——实测过，底部暗了一半而文字处只暗了 0.08。
        /// SmoothStep 保证顶边导数为 0：线性淡出会在收尾处留一条肉眼可见的硬边。
        /// </summary>
        static Sprite BakeGradientSprite()
        {
            const int w = 8, h = 512;
            const float solidTop = 0.5f;                  // 满浓区占带高的比例
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);            // 0 = 底部，1 = 顶部
                float a = t <= solidTop
                    ? 1f
                    : Mathf.SmoothStep(1f, 0f, (t - solidTop) / (1f - solidTop));
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = new Color32(255, 255, 255, alpha);
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            string path = $"{VNUiSkinExporter.TextureDir}/{GradientName}.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;   // 拉伸铺满时别让顶/底像素回卷
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed; // 压缩会让渐变起色带
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ==============================================================
        // 皮肤 prefab
        // ==============================================================

        static GameObject BuildPrefab(SkinSpec spec, Sprite gradient, Sprite rounded, bool overwrite)
        {
            var font = VNFontAssetBuilder.EnsureFontAsset(); // prefab 引用运行时字体会 Missing

            var root = new GameObject(spec.prefabName, typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            VNUiSkinExporter.Stretch(rootRect);
            var skin = root.AddComponent<VNDialogueSkin>();

            // ---- 渐变带 = panel：整屏宽，贴底 ----
            var panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(rootRect, false);
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(0f, PanelHeight);
            skin.panel = panelRect;

            var bg = VNUiSkinExporter.CreateImage(panelRect, "Gradient", gradient,
                spec.panelColor, Image.Type.Simple);
            VNUiSkinExporter.Stretch(bg.rectTransform);

            // shineFrame 故意留空 = 无边框、无流光（这套皮肤的立身之本）

            // ---- 名牌：沿用经典款的紫底圆角块 ----
            var nameTag = new GameObject("NameTag", typeof(RectTransform));
            var tagRect = (RectTransform)nameTag.transform;
            tagRect.SetParent(panelRect, false);
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = new Vector2(0f, 0.5f);
            tagRect.anchoredPosition = new Vector2(NameTagX, NameTagY);
            tagRect.sizeDelta = new Vector2(210f, 50f);
            var tagBg = VNUiSkinExporter.CreateImage(tagRect, "Bg", rounded,
                new Color(0.45f, 0.3f, 0.75f, 0.9f), Image.Type.Sliced);
            VNUiSkinExporter.Stretch(tagBg.rectTransform);
            var nameText = VNUiSkinExporter.CreateText(tagRect, "Name", font, 26,
                TextAlignmentOptions.Center);
            VNUiSkinExporter.Stretch(nameText.rectTransform);
            nameText.fontStyle = FontStyles.Bold;
            skin.nameTag = nameTag;
            skin.nameText = nameText;
            // 名牌文字的描边/渐变由 VNDialogueBox.nameplateStyle 全局接管，这里不碰材质

            // ---- 正文：居中，独立 TMP 材质（描边 + 柔光）----
            // 顶对齐（水平仍居中）：文字从上往下长，名牌永远贴在第一行上方，
            // 不会像垂直居中那样「一行台词沉到渐变带中段、名牌孤零零飘在上面」
            var body = VNUiSkinExporter.CreateText(panelRect, "Body", font, (int)BodyFontSize,
                TextAlignmentOptions.Top);
            VNUiSkinExporter.Stretch(body.rectTransform);
            body.rectTransform.offsetMin = new Vector2(BodySideMargin, BodyBottomPad);
            body.rectTransform.offsetMax = new Vector2(-BodySideMargin, -BodyTopPad);
            body.lineSpacing = 20f;
            body.color = spec.textColor;
            body.fontSharedMaterial = EnsureTextMaterial(spec, font, overwrite);
            skin.bodyText = body;

            // 头像窗落在正文左侧的空白里，所以正文/名牌都不必避让（0 = 不动）
            skin.portraitBodyInset = 0f;
            skin.portraitTagShift = 0f;

            // ---- 头像窗（左下，默认隐藏，剧本给了头像才显示）----
            var window = new GameObject("PortraitWindow", typeof(RectTransform), typeof(RectMask2D));
            var windowRect = (RectTransform)window.transform;
            windowRect.SetParent(panelRect, false);
            windowRect.anchorMin = windowRect.anchorMax = Vector2.zero;
            windowRect.pivot = Vector2.zero;
            windowRect.anchoredPosition = new Vector2(50f, 8f);
            windowRect.sizeDelta = new Vector2(230f, 300f);
            var portrait = VNUiSkinExporter.CreateImage(windowRect, "Portrait", null,
                Color.white, Image.Type.Simple);
            portrait.rectTransform.anchorMin = portrait.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            portrait.rectTransform.pivot = new Vector2(0.5f, 1f);
            window.SetActive(false);
            skin.portraitWindow = windowRect;
            skin.portraitImage = portrait;

            // ---- 继续箭头 ----
            var arrow = VNUiSkinExporter.CreateText(panelRect, "Arrow", font, 26,
                TextAlignmentOptions.Center);
            arrow.text = "▼";
            arrow.color = spec.textColor;
            arrow.fontSharedMaterial = EnsureTextMaterial(spec, font, false); // 与正文共用一份材质
            var arrowRect = arrow.rectTransform;
            arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(1f, 0f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowRect.anchoredPosition = new Vector2(-(BodySideMargin - 10f), 70f);
            arrowRect.sizeDelta = new Vector2(40f, 34f);
            skin.arrow = arrow;

            // ---- 快捷功能条停靠点：沿用经典款的位置，免得功能条被渐变带顶到半空 ----
            var dock = new GameObject("ToolbarAnchor", typeof(RectTransform));
            var dockRect = (RectTransform)dock.transform;
            dockRect.SetParent(rootRect, false);
            dockRect.anchorMin = new Vector2(0.05f, 0f);
            dockRect.anchorMax = new Vector2(0.95f, 0f);
            dockRect.pivot = new Vector2(0.5f, 0f);
            dockRect.anchoredPosition = new Vector2(0f, 28f);
            dockRect.sizeDelta = new Vector2(0f, 230f);
            skin.toolbarAnchor = dockRect;

            return VNUiSkinExporter.SavePrefab(root, spec.prefabName);
        }

        // ==============================================================
        // 正文 TMP 材质（描边 + 柔光）
        // ==============================================================

        /// <summary>
        /// 每套皮肤一份 TMP 材质资产。**不能直接改字体的 sharedMaterial**——
        /// 那会污染全项目所有用同一字体的文字（项目硬约定）。已存在的材质默认不覆盖，
        /// 这样在 Inspector 里调过的描边粗细能保住。
        /// </summary>
        static Material EnsureTextMaterial(SkinSpec spec, TMP_FontAsset font, bool overwrite)
        {
            string path = $"{MaterialDir}/VN_SoftText_{spec.prefabName.Replace("DialogueSkin_", "")}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && !overwrite) return mat;

            if (mat == null)
            {
                mat = new Material(font.material) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.CopyPropertiesFromMaterial(font.material); // 覆盖重建：先回到字体默认再上样式
            }

            // 描边（TMP SDF：宽度 > 0 即生效，keyword 显式打开更保险）
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetColor(ShaderUtilities.ID_OutlineColor, spec.outlineColor);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, spec.outlineWidth);
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.05f);

            // 柔光 / 投影：underlay 通道必须先开 keyword，否则参数写了也不显示
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, spec.underlayColor);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, spec.underlayOffset.x);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, spec.underlayOffset.y);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, spec.underlayDilate);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, spec.underlaySoftness);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ==============================================================
        // 登记进 VNGameConfig
        // ==============================================================

        static void RegisterInConfig(GameObject[] prefabs)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (cfg == null)
            {
                Debug.LogWarning("[VNSoftSkin] 未找到 " + VNGameConfig.AssetPath +
                                 "：prefab 已导出但未登记，建了配置资产后重跑本菜单即可。");
                return;
            }

            bool changed = false;
            for (int i = 0; i < Specs.Length; i++)
            {
                if (prefabs[i] == null) continue;
                // upsert 而不是「有同名就跳过」：覆盖重建会换掉 prefab 资产，
                // 只跳过的话配置里会留一个指向已删除资产的空引用，剧本切皮肤就报「未登记」
                var entry = cfg.dialogueSkins.Find(e => e != null && e.id == Specs[i].id);
                if (entry == null)
                {
                    cfg.dialogueSkins.Add(new VNGameConfig.UiSkinEntry
                        { id = Specs[i].id, prefab = prefabs[i] });
                    changed = true;
                }
                else if (entry.prefab != prefabs[i])
                {
                    entry.prefab = prefabs[i];
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(cfg);
                Debug.Log("[VNSoftSkin] 已登记进 VNGameConfig.dialogueSkins：白渐变 / 粉渐变 / 黑渐变");
            }
        }
    }
}
