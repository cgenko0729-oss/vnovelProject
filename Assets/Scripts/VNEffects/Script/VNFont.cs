using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VNEffects
{
    /// <summary>
    /// VNFont —— 全项目文字的统一字体入口（TextMeshPro 版）。
    /// 取代原先各处 Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf") 的写法。
    ///
    /// 解析顺序（三级兜底，保证任何情况下中文都能显示）：
    ///   1. 预烘焙动态 TMP 字体资产（Assets/Resources/VNFonts/NotoSansSC-Dynamic.asset，
    ///      由 Tools → VN Effects → Create TMP Font Asset 生成，多图集动态填充）
    ///   2. 运行时从随包 OTF（Resources/VNFonts/NotoSansSC-Regular）动态创建 TMP 字体资产
    ///   3. 运行时从操作系统中文字体（微软雅黑 / PingFang 等）动态创建
    ///
    /// 动态图集：需要的字形按需光栅化进图集（多图集自动扩展），生僻字零缺字；
    /// 用 Prewarm() 把剧本全文预热进图集可避免台词首次渲染时的逐字光栅化卡顿。
    /// </summary>
    public static class VNFont
    {
        /// <summary>预烘焙 TMP 字体资产的 Resources 路径</summary>
        public const string BakedAssetPath = "VNFonts/NotoSansSC-Dynamic";
        /// <summary>随包源字体（OTF）的 Resources 路径</summary>
        public const string SourceFontPath = "VNFonts/NotoSansSC-Regular";

        /// <summary>动态创建字体资产时的采样点大小 / 图集内边距 / 图集尺寸</summary>
        const int SamplePointSize = 64;
        const int AtlasPadding = 6;
        const int AtlasSize = 1024;

        /// <summary>UI 常用符号预热集（界面按键提示、箭头、省略号等，启动即备好）</summary>
        const string CommonUiChars =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~" +
            "，。、；：？！…—·「」『』（）《》【】“”‘’▼▲◀▶★☆♪％×";

        static TMP_FontAsset _asset;

        /// <summary>全项目共用的 TMP 字体资产（惰性解析，进程内缓存）</summary>
        public static TMP_FontAsset Asset
        {
            get
            {
                if (_asset != null) return _asset;

                _asset = LoadBaked() ?? CreateFromBundledFont() ?? CreateFromOsFont();
                if (_asset == null)
                {
                    Debug.LogError("[VNFont] 所有中文字体来源均不可用，回退 TMP 默认字体（无中文字形）");
                    _asset = TMP_Settings.defaultFontAsset;
                    return _asset;
                }

                Prewarm(CommonUiChars);
                return _asset;
            }
        }

        /// <summary>
        /// 把一段文本包含的全部字符预热进动态图集（去重由 TMP 内部处理）。
        /// 建议在剧本加载完成时对全文调用一次，把逐字光栅化成本挪到加载期。
        /// </summary>
        public static void Prewarm(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var asset = Asset;
            if (asset == null || asset.atlasPopulationMode != AtlasPopulationMode.Dynamic) return;
            asset.TryAddCharacters(text);
        }

        // ------------------------------------------------------------------
        // 三级来源
        // ------------------------------------------------------------------

        static TMP_FontAsset LoadBaked()
        {
            var asset = Resources.Load<TMP_FontAsset>(BakedAssetPath);
            if (asset != null) Debug.Log("[VNFont] 使用预烘焙字体资产 " + BakedAssetPath);
            return asset;
        }

        static TMP_FontAsset CreateFromBundledFont()
        {
            var font = Resources.Load<Font>(SourceFontPath);
            if (font == null) return null;
            var asset = CreateDynamic(font);
            if (asset != null)
                Debug.Log("[VNFont] 由随包字体运行时创建动态字体资产 " + SourceFontPath);
            return asset;
        }

        static TMP_FontAsset CreateFromOsFont()
        {
            string[] candidates =
            {
                "Microsoft YaHei", "微软雅黑", "PingFang SC", "Hiragino Sans GB",
                "Noto Sans CJK SC", "Source Han Sans SC", "SimHei", "SimSun",
            };
            foreach (var name in candidates)
            {
                var font = Font.CreateDynamicFontFromOSFont(name, SamplePointSize);
                if (font == null) continue;
                var asset = CreateDynamic(font);
                if (asset != null)
                {
                    Debug.LogWarning("[VNFont] 随包字体缺失，回退操作系统字体：" + name);
                    return asset;
                }
            }
            return null;
        }

        static TMP_FontAsset CreateDynamic(Font source)
        {
            var asset = TMP_FontAsset.CreateFontAsset(
                source, SamplePointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
            if (asset != null) asset.name = source.name + " (VNFont Dynamic)";
            return asset;
        }
    }
}
