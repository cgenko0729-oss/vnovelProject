using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// VNGameConfig 的编辑器工具：创建 / 从场景导入 / 扫描资产目录。
    ///
    /// 【解决的问题】
    /// Create Script Demo Scene 内部会 NewScene(EmptyScene)，即把当前场景丢弃重造，
    /// 所有挂在场景组件上的人工绑定（背景库/音频库/章节列表/地图坐标…）全部清空。
    /// 本工具把这些绑定搬进 Assets/Resources/VNGameConfig.asset，重建场景后自动生效。
    ///
    /// 【推荐使用顺序】
    ///   ① 场景已经配好 → Import From Scene（一次性搬家，不用重配）
    ///   ② 之后每次新增角色/剧本/CG/属性资产 → Rescan Asset Folders
    ///   ③ 背景 id→图、音频音量标定 → 直接在 VNGameConfig 资产的 Inspector 里维护
    /// </summary>
    public static class VNGameConfigTools
    {
        const string ScenariosDir = "Assets/Scenarios";
        const string CgDir = "Assets/CG";

        // ==================================================================
        // 创建 / 定位
        // ==================================================================

        [MenuItem("Tools/VN Effects/Game Config/Create or Select", priority = 200)]
        public static void CreateOrSelect()
        {
            var config = LoadOrCreate();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        /// <summary>取得配置资产；不存在则创建在 Assets/Resources/VNGameConfig.asset。</summary>
        public static VNGameConfig LoadOrCreate()
        {
            var config = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (config != null) return config;

            // 允许用户把资产放在别处：先全局找一遍，找到就用现成的
            var found = AssetDatabase.FindAssets("t:VNGameConfig");
            if (found.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(found[0]);
                var existing = AssetDatabase.LoadAssetAtPath<VNGameConfig>(path);
                if (existing != null)
                {
                    if (!path.Contains("/Resources/"))
                        Debug.LogWarning($"[VNGameConfig] 资产在 {path}，不在 Resources 下：" +
                                         "运行时无法自动加载，需要在场景组件的 config 字段里手动指定。" +
                                         $"建议移动到 {VNGameConfig.AssetPath}。");
                    return existing;
                }
            }

            EnsureFolder("Assets/Resources");
            config = ScriptableObject.CreateInstance<VNGameConfig>();
            AssetDatabase.CreateAsset(config, VNGameConfig.AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[VNGameConfig] 已创建 {VNGameConfig.AssetPath}");
            return config;
        }

        // ==================================================================
        // 从当前场景导入（一次性搬家）
        // ==================================================================

        [MenuItem("Tools/VN Effects/Game Config/Import From Scene", priority = 201)]
        public static void ImportFromScene()
        {
            var config = LoadOrCreate();
            var stage = Object.FindFirstObjectByType<VNStage>();
            var audio = Object.FindFirstObjectByType<VNAudio>();
            var runner = Object.FindFirstObjectByType<VNScriptRunner>();

            if (stage == null && audio == null && runner == null)
            {
                EditorUtility.DisplayDialog("VN Game Config",
                    "当前场景里没有 VNStage / VNAudio / VNScriptRunner，没有可导入的绑定。", "OK");
                return;
            }

            Undo.RecordObject(config, "Import VN bindings from scene");
            var report = new List<string>();

            if (stage != null)
            {
                Take(stage.characters, config.characters, "角色", report);
                Take(stage.backgrounds, config.backgrounds, "背景", report);
                Take(stage.cgLibrary, config.cgLibrary, "CG", report);
            }

            if (audio != null)
            {
                Take(audio.bgmLibrary, config.bgmLibrary, "BGM", report);
                Take(audio.seLibrary, config.seLibrary, "SE", report);
                Take(audio.voiceLibrary, config.voiceLibrary, "Voice", report);
                if (audio.typingTick != null && config.typingTick == null)
                {
                    config.typingTick = audio.typingTick;
                    report.Add("打字音 ×1");
                }
            }

            if (runner != null)
            {
                if (runner.script != null && config.entryScript == null)
                {
                    config.entryScript = runner.script;
                    report.Add($"入口剧本「{runner.script.name}」");
                }
                Take(runner.chapters, config.chapters, "章节", report);
            }

            // 地图地点（模板是禁用状态的子物体，要用 includeInactive 找）
            var map = Object.FindFirstObjectByType<VNMapModule>(FindObjectsInactive.Include);
            if (map != null)
            {
                Take(map.locations, config.mapLocations, "地图地点", report);
                if (map.mapSprite != null && config.mapSprite == null)
                {
                    config.mapSprite = map.mapSprite;
                    report.Add("地图底图 ×1");
                }
            }

            // 玩法定义资产列表
            var statsHud = Object.FindFirstObjectByType<VNStatsHud>(FindObjectsInactive.Include);
            if (statsHud != null) Take(statsHud.stats, config.stats, "属性定义", report);
            var questLog = Object.FindFirstObjectByType<VNQuestLog>(FindObjectsInactive.Include);
            if (questLog != null) Take(questLog.quests, config.quests, "任务定义", report);
            var shopModule = Object.FindFirstObjectByType<VNShopModule>(FindObjectsInactive.Include);
            if (shopModule != null) Take(shopModule.shops, config.shops, "商店定义", report);
            var planModule = Object.FindFirstObjectByType<VNPlanModule>(FindObjectsInactive.Include);
            if (planModule != null) Take(planModule.plans, config.plans, "日程定义", report);

            Save(config);
            string summary = report.Count > 0 ? string.Join("\n", report) : "（没有发现新的绑定）";
            Debug.Log($"[VNGameConfig] 已从场景导入绑定：\n{summary}");
            EditorUtility.DisplayDialog("VN Game Config",
                $"已从当前场景导入到 {VNGameConfig.AssetPath}：\n\n{summary}\n\n" +
                "现在重建场景也不会再丢这些设置了。", "OK");
            Selection.activeObject = config;
        }

        /// <summary>
        /// 只在目标为空时整体搬运——避免第二次执行时把资产里已经手工调好的数据覆盖掉。
        /// （典型场景：导入后你在资产里调了音量标定，然后又点了一次导入。）
        /// </summary>
        static void Take<T>(List<T> from, List<T> to, string label, List<string> report)
        {
            if (from == null || from.Count == 0) return;
            if (to.Count > 0)
            {
                report.Add($"{label} 已有 {to.Count} 条，跳过（清空资产里的列表才会重新导入）");
                return;
            }
            to.AddRange(from);
            report.Add($"{label} ×{from.Count}");
        }

        // ==================================================================
        // 扫描资产目录（约定优于配置）
        // ==================================================================

        [MenuItem("Tools/VN Effects/Game Config/Rescan Asset Folders", priority = 202)]
        public static void RescanAssetFolders()
        {
            var config = LoadOrCreate();
            Undo.RecordObject(config, "Rescan VN asset folders");
            var report = new List<string>();

            // 这些都能靠"扫目录 + 文件名"完全确定，所以每次都重新扫、直接覆盖
            config.characters = FindAll<VNCharacterDef>();
            report.Add($"角色定义 ×{config.characters.Count}");
            config.stats = FindAll<VNStatDef>();
            report.Add($"属性定义 ×{config.stats.Count}");
            config.shops = FindAll<VNShopDef>();
            report.Add($"商店定义 ×{config.shops.Count}");
            config.plans = FindAll<VNPlanDef>();
            report.Add($"日程定义 ×{config.plans.Count}");
            config.quests = FindAll<VNQuestDef>();
            report.Add($"任务定义 ×{config.quests.Count}");

            config.chapters = ScanChapters();
            report.Add($"章节剧本 ×{config.chapters.Count}");

            int cg = ScanCg(config);
            report.Add($"CG ×{cg}");

            // 背景与音频**不扫**：素材散放在 Assets/Assets 里，文件名推不出剧本 id，
            // 而且音频还有基准音量标定这种纯人工数据。它们由你在资产 Inspector 里维护。
            report.Add("背景库 / 音频库：保持手工维护（不扫描，不会被覆盖）");

            Save(config);
            Debug.Log($"[VNGameConfig] 目录扫描完成：\n{string.Join("\n", report)}");
            EditorUtility.DisplayDialog("VN Game Config",
                $"扫描完成：\n\n{string.Join("\n", report)}", "OK");
            Selection.activeObject = config;
        }

        static List<T> FindAll<T>() where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .ToList();
        }

        static List<TextAsset> ScanChapters()
        {
            if (!AssetDatabase.IsValidFolder(ScenariosDir)) return new List<TextAsset>();
            return AssetDatabase.FindAssets("t:TextAsset", new[] { ScenariosDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".vn.txt"))
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<TextAsset>)
                .Where(a => a != null)
                .ToList();
        }

        static int ScanCg(VNGameConfig config)
        {
            if (!AssetDatabase.IsValidFolder(CgDir)) return 0;
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { CgDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png") || p.EndsWith(".jpg"))
                .OrderBy(p => p)
                .ToList();

            var list = new List<VNStage.CgEntry>();
            foreach (string p in paths)
            {
                EnsureSpriteImport(p);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                if (sprite == null) continue;
                // 已有条目保留 group（差分分组是人工数据）
                string id = Path.GetFileNameWithoutExtension(p);
                var old = config.cgLibrary.Find(c => c != null && c.id == id);
                list.Add(new VNStage.CgEntry
                {
                    id = id,
                    sprite = sprite,
                    group = old != null ? old.group : null,
                });
            }
            config.cgLibrary = list;
            return list.Count;
        }

        // ==================================================================
        // 杂项
        // ==================================================================

        static void EnsureSpriteImport(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.Sprite) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void Save(VNGameConfig config)
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            VNGameConfig.ClearCache();
        }

        /// <summary>进出 Play 模式时清掉静态缓存，避免拿到旧的资产引用。</summary>
        [InitializeOnLoadMethod]
        static void HookPlayModeCacheClear()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange _) => VNGameConfig.ClearCache();
    }
}
