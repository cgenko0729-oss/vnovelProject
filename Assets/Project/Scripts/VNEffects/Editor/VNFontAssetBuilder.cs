using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 预烘焙 TMP 字体资产生成器（中文 Noto Sans SC + 日文 Noto Sans JP）。
    /// 由随包 OTF 创建"动态填充 + 多图集"模式的 TMP_FontAsset，
    /// 存到 Assets/Resources/VNFonts/ 供 VNFont 运行时按语言加载。
    ///
    /// 为什么要预烘焙成资产而不是每次运行时创建：
    ///   场景生成器在编辑期创建的 TMP 文字若引用运行时临时字体资产，
    ///   保存场景后会变成 Missing 引用；持久化资产才能被场景安全序列化。
    /// </summary>
    public static class VNFontAssetBuilder
    {
        const string ScAssetPath = "Assets/Resources/VNFonts/NotoSansSC-Dynamic.asset";
        const string ScSourcePath = "Assets/Resources/VNFonts/NotoSansSC-Regular.otf";
        const string JaAssetPath = "Assets/Resources/VNFonts/NotoSansJP-Dynamic.asset";
        const string JaSourcePath = "Assets/Resources/VNFonts/NotoSansJP-Regular.otf";

        [MenuItem("Tools/VN Effects/Create TMP Font Asset")]
        public static void CreateMenu()
        {
            var asset = EnsureFontAsset();
            EnsureJapaneseFontAsset(); // 日文源字体缺失时只警告，不阻塞中文流程
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        /// <summary>确保中文预烘焙字体资产存在（场景生成器在生成前调用），已存在则直接返回。</summary>
        public static TMP_FontAsset EnsureFontAsset() =>
            Ensure(ScAssetPath, ScSourcePath, "NotoSansSC-Dynamic");

        /// <summary>确保日文预烘焙字体资产存在；源 OTF 缺失时返回 null（运行时回退中文字体）。</summary>
        public static TMP_FontAsset EnsureJapaneseFontAsset() =>
            Ensure(JaAssetPath, JaSourcePath, "NotoSansJP-Dynamic");

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
