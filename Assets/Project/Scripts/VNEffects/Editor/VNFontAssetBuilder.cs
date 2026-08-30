using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 预烘焙 TMP 字体资产生成器（中文 霞鹜文楷 + 英文/兜底 Noto Sans SC + 日文 Noto Sans JP）。
    /// 由随包字体文件创建"动态填充 + 多图集"模式的 TMP_FontAsset，
    /// 存到 Assets/Resources/VNFonts/ 供 VNFont 运行时按语言加载。
    ///
    /// 为什么要预烘焙成资产而不是每次运行时创建：
    ///   场景生成器在编辑期创建的 TMP 文字若引用运行时临时字体资产，
    ///   保存场景后会变成 Missing 引用；持久化资产才能被场景安全序列化。
    /// </summary>
    public static class VNFontAssetBuilder
    {
        const string ZhAssetPath = "Assets/Resources/VNFonts/NotoSansSC-Dynamic.asset";
        const string ZhSourcePath = "Assets/Resources/VNFonts/LXGWWenKaiTC-Regular.ttf";
        const string GeneralAssetPath = "Assets/Resources/VNFonts/NotoSansSC-General-Dynamic.asset";
        const string GeneralSourcePath = "Assets/Resources/VNFonts/NotoSansSC-Regular.otf";
        const string JaAssetPath = "Assets/Resources/VNFonts/NotoSansJP-Dynamic.asset";
        const string JaSourcePath = "Assets/Resources/VNFonts/NotoSansJP-Regular.otf";

        /// <summary>换字体后必须核对的 GUID：全项目 199 处 TMP 文本按它引用中文字体资产</summary>
        const string ZhAssetExpectedGuid = "fdf08363d8a023d4d929f785c67e4c59";

        [MenuItem("Tools/VN Effects/字体 Fonts/生成 TMP 字体资产 Create TMP Font Asset", priority = 150)]
        public static void CreateMenu()
        {
            var asset = EnsureFontAsset();
            EnsureGeneralFontAsset(); // 英文 / 缺字兜底用，源缺失时只警告，不阻塞中文流程
            EnsureJapaneseFontAsset(); // 日文源字体缺失时只警告，不阻塞中文流程
            RepairFontMaterialReferences();
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// 换中文字体：删掉旧的中文烘焙资产 → 用当前 ZhSourcePath 重新烘焙 → 修复全项目材质引用。
        ///
        /// 为什么不能只删了重烘焙就完事：字体资产的材质是**子资产**，重新生成会拿到新的
        /// fileID，而场景 / UI 皮肤 prefab 里每个 TMP 文本都同时序列化了
        /// m_fontAsset（主资产，靠 GUID，路径不变就还在）和 m_sharedMaterial（子资产，靠 fileID）。
        /// 于是字体引用还在、材质引用变成 Missing，TMP 退回默认的 LiberationSans SDF ——
        /// 那套字体没有任何汉字，界面就整片变成 □ 方框。
        /// 所以重烘焙之后必须跑一遍 RepairFontMaterialReferences() 把材质重新指回去。
        /// </summary>
        [MenuItem("Tools/VN Effects/字体 Fonts/重烘中文字体·换字体源 Rebake Chinese Font", priority = 151)]
        public static void RebakeChineseFont()
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(ZhSourcePath);
            if (source == null)
            {
                Debug.LogError("[VNFontAssetBuilder] 源字体不存在，中止重烘焙：" + ZhSourcePath);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ZhAssetPath) != null)
                AssetDatabase.DeleteAsset(ZhAssetPath);

            var asset = EnsureFontAsset();
            if (asset == null) return;

            // 路径不变时 Unity 会复用原 GUID；万一没复用，全项目 199 处引用会一起断，必须当场发现
            var guid = AssetDatabase.AssetPathToGUID(ZhAssetPath);
            if (guid != ZhAssetExpectedGuid)
                Debug.LogError("[VNFontAssetBuilder] 中文字体资产 GUID 变了：" + guid +
                               "（期望 " + ZhAssetExpectedGuid + "）——场景与 UI 皮肤 prefab 的字体引用" +
                               "会全部断掉，需要把旧 GUID 批量替换成新的");

            RepairFontMaterialReferences();
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>确保中文（霞鹜文楷）预烘焙字体资产存在（场景生成器在生成前调用），已存在则直接返回。</summary>
        public static TMP_FontAsset EnsureFontAsset() =>
            Ensure(ZhAssetPath, ZhSourcePath, "NotoSansSC-Dynamic");

        /// <summary>确保英文 / 缺字兜底（Noto Sans SC）预烘焙字体资产存在，已存在则直接返回。</summary>
        public static TMP_FontAsset EnsureGeneralFontAsset() =>
            Ensure(GeneralAssetPath, GeneralSourcePath, "NotoSansSC-General-Dynamic");

        /// <summary>确保日文预烘焙字体资产存在；源字体缺失时返回 null（运行时回退兜底档案）。</summary>
        public static TMP_FontAsset EnsureJapaneseFontAsset() =>
            Ensure(JaAssetPath, JaSourcePath, "NotoSansJP-Dynamic");

        // ------------------------------------------------------------------
        // 材质引用修复
        // ------------------------------------------------------------------

        /// <summary>
        /// 扫全项目的 prefab 与当前打开的场景，把「字体资产是 VN 三套字体之一、材质却已失效」
        /// 的 TMP 文本的材质重新指回字体自带材质。重烘焙字体后必跑（见 RebakeChineseFont 注释）。
        ///
        /// 判定"失效"只认两种情况：材质为空（引用已 Missing），或材质贴的不是这套字体的图集。
        /// 这样描边 / 阴影等共用同一张图集的材质变体不会被误伤。
        /// </summary>
        [MenuItem("Tools/VN Effects/字体 Fonts/修复字体材质引用 Repair Font Material References", priority = 152)]
        public static void RepairFontMaterialReferences()
        {
            var fonts = new List<TMP_FontAsset>();
            foreach (var path in new[] { ZhAssetPath, GeneralAssetPath, JaAssetPath })
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null) fonts.Add(font);
            }
            if (fonts.Count == 0)
            {
                Debug.LogWarning("[VNFontAssetBuilder] 没有找到任何 VN 字体资产，跳过材质修复");
                return;
            }

            int fixedCount = 0;
            int prefabCount = 0;
            var failed = new List<string>();

            // 只扫项目自己的目录，别去动 Plugins 下第三方插件的 prefab
            var folders = new[] { "Assets/Art", "Assets/Project", "Assets/Resources" };
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", folders))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    int n = RepairIn(root, fonts);
                    if (n <= 0) continue;

                    // SaveAsPrefabAsset 会静默失败（prefab 在场景里有活实例时尤其容易），
                    // 不看返回值的话修复器会报告"全部修好"，实际留着一堆方框
                    bool saved;
                    PrefabUtility.SaveAsPrefabAsset(root, path, out saved);
                    if (!saved)
                    {
                        failed.Add(path);
                        continue;
                    }
                    fixedCount += n;
                    prefabCount++;
                }
                finally
                {
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
                }
            }

            // 场景只处理当前打开的（强行开关场景会打断用户手上的工作），改完标脏由用户存盘
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                int n = 0;
                foreach (var go in scene.GetRootGameObjects()) n += RepairIn(go, fonts);
                if (n <= 0) continue;
                EditorSceneManager.MarkSceneDirty(scene);
                fixedCount += n;
                Debug.Log("[VNFontAssetBuilder] 场景 " + scene.name + " 修好 " + n +
                          " 处字体材质引用，记得存盘（Ctrl+S）");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[VNFontAssetBuilder] 字体材质引用修复完成：共 " + fixedCount +
                      " 处（其中 " + prefabCount + " 个 prefab）");

            if (failed.Count > 0)
                Debug.LogError("[VNFontAssetBuilder] 下列 prefab 存盘失败，材质引用仍是断的" +
                               "（界面上会是 □ 方框），需要手动处理：\n  " + string.Join("\n  ", failed));
        }

        static int RepairIn(GameObject root, List<TMP_FontAsset> fonts)
        {
            int n = 0;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var font = text.font;
                if (font == null || !fonts.Contains(font)) continue;

                var mat = text.fontSharedMaterial;
                bool broken = mat == null || mat.mainTexture != font.atlasTexture;
                if (!broken) continue;

                text.fontSharedMaterial = font.material;
                EditorUtility.SetDirty(text);
                n++;
            }
            return n;
        }

        static TMP_FontAsset Ensure(string assetPath, string sourcePath, string assetName)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (source == null)
            {
                Debug.LogError("[VNFontAssetBuilder] 未找到源字体 " + sourcePath +
                               "，无法生成 TMP 字体资产（运行时 VNFont 仍会自行动态创建兜底）");
                return null;
            }

            var fontAsset = TMP_FontAsset.CreateFontAsset(
                source, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null)
            {
                Debug.LogError("[VNFontAssetBuilder] TMP_FontAsset.CreateFontAsset 失败：" + sourcePath);
                return null;
            }

            fontAsset.name = assetName;
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // 材质与图集必须作为子资产一并持久化，否则场景引用会丢
            fontAsset.material.name = fontAsset.name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            fontAsset.atlasTexture.name = fontAsset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[VNFontAssetBuilder] 已生成 TMP 字体资产：" + assetPath);
            return fontAsset;
        }
    }
}
