using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 把秘密偷拍模式**装进工程**（Tools → VN Effects → 场景装机 Install To Scene → 秘密偷拍 Secret Photo）。
    ///
    /// 它**不动场景**——模式由 VNScriptRunner 启动时自己创建，没有模板要挂。
    /// 做的是三件资产层面的事（重复执行安全，已有的原样保留）：
    ///   ① 参数资产 VNSecretPhotoDef（宽松档默认值）→ 登记进 VNGameConfig.secretPhoto
    ///   ② 教程资产 VNTutorialDef「秘密相机」（三步：察觉条 / 快门 / 胶卷）→ 登记进教程库
    ///   ③ 工程里没有任何商店卖「胶卷」时，造一间示例商店「杂货店」→ 登记进商店库
    /// 资产造在**既有同类资产所在的目录**（工程整理过，商店实际在 Assets/Art/VNEffects/Shops），
    /// 找不到同类资产才退回 Assets/VNEffects/<类型>。
    /// </summary>
    public static class VNSecretPhotoInstaller
    {
        const string DefaultRoot = "Assets/VNEffects";
        const string DefName = "秘密相机";
        const string TutorialId = "秘密相机";
        const string FilmItemId = "胶卷";
        const string DemoShopId = "杂货店";

        [MenuItem("Tools/VN Effects/场景装机 Install To Scene/秘密偷拍 Secret Photo", priority = 160)]
        public static void Install()
        {
            var report = new List<string>();
            var config = VNGameConfigTools.LoadOrCreate();
            if (config == null)
            {
                EditorUtility.DisplayDialog("VN Secret Photo", "找不到也建不出 VNGameConfig，安装中止。", "OK");
                return;
            }
            Undo.RecordObject(config, "Install secret photo mode");

            // ① 参数资产
            var def = EnsureDef(report);
            if (config.secretPhoto == null && def != null)
            {
                config.secretPhoto = def;
                report.Add("VNGameConfig.secretPhoto ← " + def.name);
            }

            // ② 教程资产
            var tutorial = EnsureTutorial(report);
            if (tutorial != null && !config.tutorials.Contains(tutorial))
            {
                config.tutorials.Add(tutorial);
                report.Add("VNGameConfig 教程库 += " + tutorial.name);
            }

            // ③ 胶卷商店
            var shop = EnsureFilmShop(report);
            if (shop != null && !config.shops.Contains(shop))
            {
                config.shops.Add(shop);
                report.Add("VNGameConfig 商店库 += " + shop.name);
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            VNGameConfig.ClearCache();
            VNAssetLibraryEvents.RaiseChanged();

            string summary = string.Join("\n", report);
            Debug.Log($"[VNSecretPhoto] 已装入工程：\n{summary}");
            EditorUtility.DisplayDialog("VN Secret Photo",
                $"秘密偷拍模式已装入工程：\n\n{summary}\n\n" +
                "剧本里这样用：\n" +
                "  flag 秘密相机=1        ← 解锁，右上角出现相机图标\n" +
                "  flag 道具_胶卷+10       ← 直接给胶卷（或 event shop id:杂货店 去买）\n" +
                "被发现：好感 -3，flag 偷拍_警惕_<角色> +10（下次察觉涨得更快）\n" +
                "照片在 G 键画廊的「私密」页。数值全在参数资产上改。", "OK");
            Selection.activeObject = def != null ? (Object)def : config;
            if (def != null) EditorGUIUtility.PingObject(def);
        }

        // ------------------------------------------------------------------

        static VNSecretPhotoDef EnsureDef(List<string> report)
        {
            var existing = FindAll<VNSecretPhotoDef>();
            if (existing.Count > 0)
            {
                report.Add($"参数资产已存在 ×{existing.Count}（{AssetDatabase.GetAssetPath(existing[0])}）");
                return existing[0];
            }

            string dir = DefaultRoot + "/SecretPhoto";
            VNEffectsDemoSetup.EnsureFolder(dir);
            var def = ScriptableObject.CreateInstance<VNSecretPhotoDef>();
            def.name = DefName;
            def.tutorialId = TutorialId;
            def.filmItemId = FilmItemId;
            string path = $"{dir}/{DefName}.asset";
            AssetDatabase.CreateAsset(def, path);
            report.Add($"新建参数资产 {path}（宽松档：中距离约 20 秒、扣 3 好感、一卷 10 张）");
            return def;
        }

        static VNTutorialDef EnsureTutorial(List<string> report)
        {
            var all = FindAll<VNTutorialDef>();
            foreach (var t in all)
            {
                string key = string.IsNullOrEmpty(t.id) ? t.name : t.id;
                if (key == TutorialId)
                {
                    report.Add($"教程资产已存在（{AssetDatabase.GetAssetPath(t)}）");
                    return t;
                }
            }

            string dir = DirOfExisting(all, DefaultRoot + "/Tutorials");
            VNEffectsDemoSetup.EnsureFolder(dir);
            var def = ScriptableObject.CreateInstance<VNTutorialDef>();
            def.name = TutorialId;
            def.id = TutorialId;
            def.once = true;
            def.allowSkip = true;
            def.steps = new List<VNTutorialStep>
            {
                new VNTutorialStep
                {
                    anchor = VNSecretPhotoUi.AnchorAlert,
                    title = "察觉条",
                    titleEn = "Alert bar",
                    titleJa = "警戒ゲージ",
                    body = "镜头对着她、推得越近、停得越久，察觉度涨得越快。\n满了就会被发现：扣好感，而且她以后会更警惕。\n她不在取景框里时不会涨。",
                    bodyEn = "Aiming at her, zooming in and lingering all raise the alert.\nWhen it fills she catches you: affection drops and she stays more wary.\nIt does not rise while she is out of frame.",
                    bodyJa = "彼女に向ける・寄る・長く留まるほど警戒が上がります。\n満タンになるとバレて好感度が下がり、以後さらに警戒されます。\nフレーム外なら上がりません。",
                    card = VNTutorialCardSpot.Bottom,
                },
                new VNTutorialStep
                {
                    anchor = VNSecretPhotoUi.AnchorShutter,
                    title = "快门",
                    titleEn = "Shutter",
                    titleJa = "シャッター",
                    body = "滚轮缩放、拖动画面平移，构好图就按快门（或空格）。\n照片会存进画廊的「私密」页。",
                    bodyEn = "Wheel to zoom, drag to pan, then press the shutter (or Space).\nPhotos go to the Private page of the gallery.",
                    bodyJa = "ホイールでズーム、ドラッグで移動、決まったらシャッター（またはスペース）。\n写真はギャラリーの「プライベート」ページに保存されます。",
                    card = VNTutorialCardSpot.Top,
                },
                new VNTutorialStep
                {
                    anchor = VNSecretPhotoUi.AnchorFilm,
                    title = "胶卷",
                    titleEn = "Film",
                    titleJa = "フィルム",
                    body = "按一次快门用一卷，被发现那一下也照样用掉。\n胶卷在商店买。Esc 随时退出。",
                    bodyEn = "Each shot uses one roll, even the one that gets you caught.\nBuy film at a shop. Esc leaves anytime.",
                    bodyJa = "1回撮るごとに1本消費。バレた瞬間のシャッターも消費します。\nフィルムはショップで購入。Escでいつでも終了。",
                    card = VNTutorialCardSpot.Bottom,
                },
            };
            string path = $"{dir}/{TutorialId}.asset";
            AssetDatabase.CreateAsset(def, path);
            report.Add($"新建教程资产 {path}");
            return def;
        }

        static VNShopDef EnsureFilmShop(List<string> report)
        {
            var shops = FindAll<VNShopDef>();
            foreach (var s in shops)
                if (s.items != null && s.items.Any(i => i != null && i.id == FilmItemId))
                {
                    report.Add($"已有商店卖「{FilmItemId}」：{s.name}");
                    return null; // 不新增、也不改动用户的商店
                }

            string dir = DirOfExisting(shops, DefaultRoot + "/Shops");
            VNEffectsDemoSetup.EnsureFolder(dir);
            var shop = ScriptableObject.CreateInstance<VNShopDef>();
            shop.name = DemoShopId;
            shop.shopId = DemoShopId;
            shop.shopName = DemoShopId;
            shop.shopNameEn = "General Store";
            shop.shopNameJa = "雑貨店";
            shop.items = new List<VNShopDef.Item>
            {
                new VNShopDef.Item
                {
                    id = FilmItemId,
                    displayName = "胶卷",
                    displayNameEn = "Film",
                    displayNameJa = "フィルム",
                    description = "秘密相机用的胶卷，一卷拍一张。",
                    descriptionEn = "Film for the secret camera. One roll, one shot.",
                    descriptionJa = "隠しカメラ用のフィルム。1本で1枚。",
                    price = 200,
                    sellPrice = 0,
                    maxOwned = 0,
                },
            };
            string path = $"{dir}/{DemoShopId}.asset";
            AssetDatabase.CreateAsset(shop, path);
            report.Add($"新建示例商店 {path}（卖「{FilmItemId}」200 金钱；剧本 event shop id:{DemoShopId}）");
            return shop;
        }

        // ------------------------------------------------------------------

        static List<T> FindAll<T>() where T : Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .ToList();

        /// <summary>同类资产已经在哪个目录就造在哪（工程整理过目录，别硬写死）</summary>
        static string DirOfExisting<T>(List<T> existing, string fallback) where T : Object
        {
            if (existing.Count == 0) return fallback;
            string path = AssetDatabase.GetAssetPath(existing[0]);
            if (string.IsNullOrEmpty(path)) return fallback;
            return System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        }
    }
}
