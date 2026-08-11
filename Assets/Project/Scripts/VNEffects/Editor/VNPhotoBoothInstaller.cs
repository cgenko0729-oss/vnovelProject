using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 把大头贴模块**增量装进当前场景**，不重建场景（同 VNBadmintonInstaller 的路数）。
    ///
    /// 做四件事：
    ///   ① 没有资产就铺一套默认的（5 边框 / 10 贴纸 / 3 主题）——已存在的绝不覆盖，
    ///      因为那可能是用户改过的
    ///   ② 在场景 VNEventRegistry 下补一个**禁用的** PhotoBoothTemplate（必须带 RectTransform）
    ///   ③ 把工程里全部照片资产填进模板
    ///   ④ 登记进 VNGameConfig（重建场景也不会丢）
    /// 重复执行安全：已经装过就只刷新三个列表。
    /// </summary>
    public static class VNPhotoBoothInstaller
    {
        const string ModuleId = "photo";
        const string TemplateName = "PhotoBoothTemplate";
        const string RootDir = "Assets/VNEffects/Photo";
        const string FrameDir = RootDir + "/Frames";
        const string StickerDir = RootDir + "/Stickers";
        const string BackdropDir = RootDir + "/Backdrops";
        const string ThemeDir = RootDir + "/Themes";

        [MenuItem("Tools/VN Effects/Install Photo Booth Module To Scene", priority = 212)]
        public static void Install()
        {
            string report = InstallCore(out bool ok);
            if (!ok)
            {
                EditorUtility.DisplayDialog("VN Photo Booth", report, "OK");
                return;
            }
            EditorUtility.DisplayDialog("VN Photo Booth",
                $"大头贴模块已装进当前场景：\n\n{report}\n\n" +
                "场景已标记为未保存——记得 Ctrl+S。\n" +
                "剧本里就可以写：event photo vs:星野结衣 theme:甜蜜", "OK");
        }

        /// <summary>
        /// 真正干活的部分，**不弹任何对话框**——脚本 / 自动化只能走这个入口。
        /// （带 DisplayDialog 的方法一旦被 MCP 之类的脚本调用，模态框会把编辑器主线程
        ///  卡死，外部只能看到超时。踩过一次，别再踩。）
        /// </summary>
        public static string InstallCore(out bool ok)
        {
            ok = false;
            var registry = Object.FindFirstObjectByType<VNEventRegistry>(
                FindObjectsInactive.Include);
            if (registry == null)
            {
                return "当前场景里找不到 VNEventRegistry。\n\n" +
                       "事件模块要挂在注册表下面。请先打开剧本场景（含 VNEventRegistry 的那个），" +
                       "或用 Tools → VN Effects → Create Script Demo Scene 造一个新场景。";
            }

            var report = new List<string>();

            // ① 默认资产（缺什么补什么，已有的一律不动）
            int created = EnsureDefaultAssets();
            report.Add(created > 0
                ? $"新建默认资产 ×{created}（{RootDir}）"
                : $"资产已存在，未新建（{RootDir}）");

            var frames = LoadAll<VNPhotoFrameDef>();
            var stickers = LoadAll<VNPhotoStickerDef>();
            var backdrops = LoadAll<VNPhotoBackdropDef>();
            var themes = LoadAll<VNPhotoThemeDef>();
            report.Add($"边框 ×{frames.Count} / 贴纸 ×{stickers.Count} / " +
                       $"背景 ×{backdrops.Count} / 主题 ×{themes.Count}");

            // ② 场景里的模板
            var entry = registry.modules.FirstOrDefault(e => e != null && e.id == ModuleId);
            var module = entry != null ? entry.template as VNPhotoBoothModule : null;

            if (module == null)
            {
                Undo.RecordObject(registry, "Install photo booth module");

                // ★ 必须带 RectTransform：模块搭 UI 时直接 (RectTransform)transform
                var go = new GameObject(TemplateName, typeof(RectTransform));
                go.transform.SetParent(registry.transform, false);
                module = go.AddComponent<VNPhotoBoothModule>();
                go.SetActive(false);  // 模板保持禁用，运行时 Instantiate 后才激活
                Undo.RegisterCreatedObjectUndo(go, "Install photo booth module");

                if (entry == null)
                {
                    entry = new VNEventRegistry.Entry { id = ModuleId, template = module };
                    registry.modules.Add(entry);
                }
                else entry.template = module;

                report.Add($"注册表新增模块「{ModuleId}」→ {TemplateName}（已禁用）");
            }
            else
            {
                Undo.RecordObject(module, "Refresh photo booth module");
                report.Add($"模块「{ModuleId}」已存在，只刷新资产列表");
            }

            module.frames = new List<VNPhotoFrameDef>(frames);
            module.stickers = new List<VNPhotoStickerDef>(stickers);
            module.backdrops = new List<VNPhotoBackdropDef>(backdrops);
            module.themes = new List<VNPhotoThemeDef>(themes);
            EditorUtility.SetDirty(module);
            EditorUtility.SetDirty(registry);
            EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);

            // ③ 登记进 VNGameConfig
            var config = VNGameConfigTools.LoadOrCreate();
            if (config != null)
            {
                Undo.RecordObject(config, "Register photo assets");
                config.photoFrames = new List<VNPhotoFrameDef>(frames);
                config.photoStickers = new List<VNPhotoStickerDef>(stickers);
                config.photoBackdrops = new List<VNPhotoBackdropDef>(backdrops);
                config.photoThemes = new List<VNPhotoThemeDef>(themes);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                VNGameConfig.ClearCache();
                report.Add("VNGameConfig 已登记三套照片资产");

                if (string.IsNullOrEmpty(config.photoMeCharacterId))
                    report.Add("⚠ VNGameConfig 的「大头贴里我用哪个角色」还没填——" +
                               "不填的话取景框左边是空的，剧本可以用 me: 临时指定");
            }

            string summary = string.Join("\n", report);
            Debug.Log($"[VNPhoto] 已装入当前场景：\n{summary}");
            Selection.activeObject = module.gameObject;
            EditorGUIUtility.PingObject(module.gameObject);
            ok = true;
            return summary;
        }

        // ==============================================================
        // 默认资产
        // ==============================================================

        static int EnsureDefaultAssets()
        {
            EnsureFolder(FrameDir);
            EnsureFolder(StickerDir);
            EnsureFolder(BackdropDir);
            EnsureFolder(ThemeDir);

            int created = 0;

            // ---- 贴纸 ----
            created += Sticker("爱心", VNPhotoStickerShape.Heart, "Heart", "ハート",
                new Color(1f, 0.42f, 0.6f), 96f);
            created += Sticker("星星", VNPhotoStickerShape.Star, "Star", "スター",
                new Color(1f, 0.85f, 0.35f), 96f);
            created += Sticker("闪光", VNPhotoStickerShape.Sparkle, "Sparkle", "キラキラ",
                new Color(1f, 0.95f, 0.7f), 88f);
            created += Sticker("蝴蝶结", VNPhotoStickerShape.Ribbon, "Ribbon", "リボン",
                new Color(1f, 0.55f, 0.72f), 104f);
            created += Sticker("对话泡", VNPhotoStickerShape.SpeechBubble, "Bubble", "吹き出し",
                new Color(1f, 1f, 1f), 120f);
            created += Sticker("小花", VNPhotoStickerShape.Flower, "Flower", "お花",
                new Color(1f, 0.72f, 0.82f), 92f);
            created += Sticker("音符", VNPhotoStickerShape.Note, "Note", "音符",
                new Color(0.65f, 0.8f, 1f), 88f);
            created += Sticker("皇冠", VNPhotoStickerShape.Crown, "Crown", "王冠",
                new Color(1f, 0.83f, 0.3f), 104f);
            created += Sticker("猫耳", VNPhotoStickerShape.CatEars, "Cat ears", "猫耳",
                new Color(0.95f, 0.6f, 0.75f), 120f);
            created += Sticker("云朵", VNPhotoStickerShape.Cloud, "Cloud", "雲",
                new Color(1f, 1f, 1f), 128f);

            // ---- 边框 ----
            created += Frame("粉格子", VNPhotoFrameStyle.PinkCheck, "Pink check", "ピンクチェック",
                new Color(1f, 0.62f, 0.78f), VNPhotoMaskShape.Ellipse, "BE WITH YOU");
            created += Frame("星空", VNPhotoFrameStyle.StarrySky, "Starry sky", "星空",
                new Color(0.45f, 0.5f, 0.95f), VNPhotoMaskShape.RoundedRect, "GOOD NIGHT");
            created += Frame("胶片", VNPhotoFrameStyle.Film, "Film", "フィルム",
                new Color(0.62f, 0.62f, 0.7f), VNPhotoMaskShape.None, "");
            created += Frame("简约白框", VNPhotoFrameStyle.SimpleWhite, "Simple", "シンプル",
                new Color(0.9f, 0.55f, 0.68f), VNPhotoMaskShape.RoundedRect, "");
            created += Frame("樱花", VNPhotoFrameStyle.Sakura, "Sakura", "さくら",
                new Color(1f, 0.72f, 0.82f), VNPhotoMaskShape.Ellipse, "SPRING DAYS");

            // ---- 背景 ----
            created += Backdrop("放射线", VNPhotoBackdropStyle.RadialBurst, "Sunburst", "集中線",
                new Color(1f, 0.62f, 0.78f), new Color(1f, 0.93f, 0.96f));
            created += Backdrop("波点", VNPhotoBackdropStyle.Dots, "Polka dots", "水玉",
                new Color(1f, 0.72f, 0.82f), new Color(1f, 0.97f, 0.98f));
            created += Backdrop("斜条纹", VNPhotoBackdropStyle.Stripes, "Stripes", "ストライプ",
                new Color(0.68f, 0.85f, 1f), new Color(0.97f, 0.99f, 1f));
            created += Backdrop("黄昏", VNPhotoBackdropStyle.VerticalGradient, "Dusk", "夕暮れ",
                new Color(1f, 0.72f, 0.5f), new Color(0.55f, 0.5f, 0.8f));
            created += Backdrop("星夜", VNPhotoBackdropStyle.StarryNight, "Starry night", "星空",
                new Color(0.32f, 0.36f, 0.72f), new Color(1f, 1f, 0.9f));
            created += Backdrop("彩虹", VNPhotoBackdropStyle.Rainbow, "Rainbow", "レインボー",
                new Color(1f, 1f, 1f), new Color(1f, 1f, 1f));
            created += Backdrop("光斑", VNPhotoBackdropStyle.Bokeh, "Bokeh", "ボケ",
                new Color(1f, 0.85f, 0.62f), new Color(0.42f, 0.35f, 0.55f));
            created += Backdrop("纯白", VNPhotoBackdropStyle.SolidColor, "Plain white", "白",
                new Color(0.98f, 0.98f, 1f), new Color(0.98f, 0.98f, 1f));

            // ---- 主题 ----
            created += ThemeSweet();
            created += ThemeFunny();
            created += ThemeYouth();

            if (created > 0) AssetDatabase.SaveAssets();
            return created;
        }

        static int Sticker(string id, VNPhotoStickerShape shape, string en, string ja,
            Color tint, float size)
        {
            return Create<VNPhotoStickerDef>(StickerDir, id, a =>
            {
                a.stickerId = id;
                a.displayName = id;
                a.displayNameEn = en;
                a.displayNameJa = ja;
                a.shape = shape;
                a.tint = tint;
                a.defaultSize = size;
            });
        }

        static int Backdrop(string id, VNPhotoBackdropStyle style, string en, string ja,
            Color main, Color second)
        {
            return Create<VNPhotoBackdropDef>(BackdropDir, id, a =>
            {
                a.backdropId = id;
                a.displayName = id;
                a.displayNameEn = en;
                a.displayNameJa = ja;
                a.style = style;
                a.mainColor = main;
                a.secondColor = second;
            });
        }

        static int Frame(string id, VNPhotoFrameStyle style, string en, string ja,
            Color main, VNPhotoMaskShape mask, string watermark)
        {
            return Create<VNPhotoFrameDef>(FrameDir, id, a =>
            {
                a.frameId = id;
                a.displayName = id;
                a.displayNameEn = en;
                a.displayNameJa = ja;
                a.style = style;
                a.mainColor = main;
                a.maskShape = mask;
                a.watermark = watermark;
                a.watermarkEn = watermark;
                a.watermarkJa = watermark;

                // 不裁切的胶片风把开窗铺满，其余留出边框花纹
                if (mask == VNPhotoMaskShape.None)
                {
                    a.maskSize = new Vector2(0.86f, 0.74f);
                    a.maskEdgeWidth = 0f;
                    a.maskEdgeColor = new Color(0f, 0f, 0f, 0f);
                }
                else
                {
                    a.maskSize = new Vector2(0.72f, 0.82f);
                    a.maskEdgeWidth = 10f;
                    a.maskEdgeColor = Color.Lerp(main, Color.white, 0.1f);
                }
                a.windowColor = new Color(0.9f, 0.95f, 1f, 1f);
            });
        }

        static int ThemeSweet()
        {
            return Create<VNPhotoThemeDef>(ThemeDir, "甜蜜", a =>
            {
                a.themeId = "甜蜜";
                a.displayName = "甜蜜";
                a.displayNameEn = "Sweet";
                a.displayNameJa = "あまあま";
                a.hint = Line("拍一张甜甜的合照吧",
                    "Let's take a sweet one together", "あまーい一枚を撮ろう");
                a.timeLimit = 60f;
                a.baseScore = 20;
                a.perfectLine = 75;
                a.passLine = 45;
                a.stickerScoreCap = 5;

                a.expressionRules.Add(Expr(VNPhotoSlot.Her, "害羞", 25,
                    Line("她害羞的表情抓得真好", "You caught her shy face perfectly",
                        "照れ顔がいい感じ")));
                a.expressionRules.Add(Expr(VNPhotoSlot.Any, "微笑", 18,
                    Line("笑容很自然", "A natural smile", "自然な笑顔")));
                a.expressionRules.Add(Expr(VNPhotoSlot.Any, "生气", -10, null));

                a.frameRules.Add(FrameRule("粉格子", 20,
                    Line("粉格子边框很配这个主题", "The pink check fits the mood",
                        "ピンクチェックがぴったり")));
                a.frameRules.Add(FrameRule("樱花", 15, null));

                a.backdropRules.Add(BackdropRule("放射线", 15,
                    Line("放射线一衬，整张都甜起来了", "The sunburst really sells the sweetness",
                        "集中線で一気に甘くなった")));
                a.backdropRules.Add(BackdropRule("波点", 12, null));

                a.stickerRules.Add(StickerRule("爱心", 8, 3,
                    Line("爱心贴得恰到好处", "Just the right amount of hearts",
                        "ハートの量がちょうどいい")));
                a.stickerRules.Add(StickerRule("小花", 5, 2, null));

                a.perfectComment = Line("这张可以当壁纸了", "This one's wallpaper material",
                    "これは壁紙にできる");
                a.normalComment = Line("挺好的一张", "Not bad at all", "なかなかいい一枚");
                a.failComment = Line("下次再努力吧……", "Maybe next time...",
                    "次はもっと頑張ろう……");
            });
        }

        static int ThemeFunny()
        {
            return Create<VNPhotoThemeDef>(ThemeDir, "搞怪", a =>
            {
                a.themeId = "搞怪";
                a.displayName = "搞怪";
                a.displayNameEn = "Silly";
                a.displayNameJa = "おふざけ";
                a.hint = Line("越夸张越好，别正经",
                    "The sillier the better", "とにかく変顔で");
                a.timeLimit = 45f;
                a.baseScore = 20;
                a.perfectLine = 70;
                a.passLine = 40;
                a.stickerScoreCap = 6;

                a.expressionRules.Add(Expr(VNPhotoSlot.Any, "坏笑", 22,
                    Line("这个坏笑很有戏", "That smirk sells it", "そのニヤリがいい")));
                a.expressionRules.Add(Expr(VNPhotoSlot.Any, "惊讶", 18, null));
                a.expressionRules.Add(Expr(VNPhotoSlot.Any, "生气", 12, null));

                a.frameRules.Add(FrameRule("胶片", 18, null));
                a.frameRules.Add(FrameRule("星空", 10, null));

                a.backdropRules.Add(BackdropRule("彩虹", 15,
                    Line("彩虹背景把荒唐感拉满了", "The rainbow maxes out the absurdity",
                        "レインボーでバカバカしさ全開")));
                a.backdropRules.Add(BackdropRule("斜条纹", 10, null));

                a.stickerRules.Add(StickerRule("猫耳", 10, 2,
                    Line("猫耳是犯规的", "The cat ears are cheating", "猫耳は反則")));
                a.stickerRules.Add(StickerRule("皇冠", 8, 2, null));
                a.stickerRules.Add(StickerRule("对话泡", 6, 3, null));

                a.perfectComment = Line("笑到停不下来", "Can't stop laughing", "笑いが止まらない");
                a.normalComment = Line("还算好笑", "Mildly funny", "そこそこ面白い");
                a.failComment = Line("太正经了，不好笑", "Way too serious", "真面目すぎ");
            });
        }

        static int ThemeYouth()
        {
            return Create<VNPhotoThemeDef>(ThemeDir, "青春", a =>
            {
                a.themeId = "青春";
                a.displayName = "青春";
                a.displayNameEn = "Youth";
                a.displayNameJa = "青春";
                a.hint = Line("留下这个夏天的样子",
                    "Capture this summer", "この夏を残そう");
                a.timeLimit = 60f;
                a.baseScore = 25;
                a.perfectLine = 75;
                a.passLine = 45;
                a.stickerScoreCap = 4;

                a.expressionRules.Add(Expr(VNPhotoSlot.Any, "微笑", 25,
                    Line("这个笑容就是青春本身", "That smile is what youth looks like",
                        "この笑顔こそ青春")));
                a.expressionRules.Add(Expr(VNPhotoSlot.Any, "惊讶", 12, null));
                a.expressionRules.Add(Expr(VNPhotoSlot.Any, "沮丧", -8, null));

                a.frameRules.Add(FrameRule("简约白框", 20, null));
                a.frameRules.Add(FrameRule("星空", 15, null));

                a.backdropRules.Add(BackdropRule("黄昏", 18,
                    Line("黄昏的光线太犯规了", "That dusk light is unfair",
                        "夕暮れの光は反則")));
                a.backdropRules.Add(BackdropRule("星夜", 12, null));

                a.stickerRules.Add(StickerRule("星星", 8, 3, null));
                a.stickerRules.Add(StickerRule("音符", 6, 2, null));

                a.perfectComment = Line("多年以后翻出来还会笑",
                    "You'll smile at this years from now", "何年後に見ても笑える一枚");
                a.normalComment = Line("普通但真实", "Plain but honest", "普通だけど本物");
                a.failComment = Line("好像少了点什么", "Something's missing",
                    "何かが足りない");
            });
        }

        // ==============================================================
        // 工具
        // ==============================================================

        static VNPhotoLine Line(string zh, string en, string ja) =>
            new VNPhotoLine { text = zh, textEn = en, textJa = ja };

        static VNPhotoThemeDef.ExpressionRule Expr(VNPhotoSlot slot, string expression,
            int score, VNPhotoLine comment) =>
            new VNPhotoThemeDef.ExpressionRule
            {
                slot = slot,
                expression = expression,
                score = score,
                comment = comment ?? new VNPhotoLine(),
            };

        static VNPhotoThemeDef.FrameRule FrameRule(string frameId, int score,
            VNPhotoLine comment) =>
            new VNPhotoThemeDef.FrameRule
            {
                frameId = frameId,
                score = score,
                comment = comment ?? new VNPhotoLine(),
            };

        static VNPhotoThemeDef.BackdropRule BackdropRule(string backdropId, int score,
            VNPhotoLine comment) =>
            new VNPhotoThemeDef.BackdropRule
            {
                backdropId = backdropId,
                score = score,
                comment = comment ?? new VNPhotoLine(),
            };

        static VNPhotoThemeDef.StickerRule StickerRule(string stickerId, int score,
            int maxCount, VNPhotoLine comment) =>
            new VNPhotoThemeDef.StickerRule
            {
                stickerId = stickerId,
                score = score,
                maxCount = maxCount,
                comment = comment ?? new VNPhotoLine(),
            };

        /// <summary>建资产；已存在则原样保留（用户可能改过参数，绝不覆盖）。返回新建数量。</summary>
        static int Create<T>(string dir, string name, System.Action<T> init)
            where T : ScriptableObject
        {
            string path = $"{dir}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<T>(path) != null) return 0;

            var asset = ScriptableObject.CreateInstance<T>();
            init(asset);
            AssetDatabase.CreateAsset(asset, path);
            return 1;
        }

        static List<T> LoadAll<T>() where T : ScriptableObject =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .ToList();

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
