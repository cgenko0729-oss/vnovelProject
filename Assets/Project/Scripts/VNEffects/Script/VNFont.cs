using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VNEffects
{
    /// <summary>
    /// VNFont —— 全项目文字的统一字体入口（TextMeshPro 版）。
    /// 取代原先各处 Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf") 的写法。
    ///
    /// 多语言：按 VNLocale.Language 返回对应字体 ——
    ///   中文 / 英文：Noto Sans SC（拉丁字形齐全，共用一套图集）
    ///   日文：Noto Sans JP（SC 的假名字形不合日文排印规范，必须独立字体），
    ///        并把 SC 设为 TMP fallback，日文文本里偶发的简体汉字也不缺字
    ///
    /// 每种语言的解析顺序（三级兜底，保证任何情况下都能显示）：
    ///   1. 预烘焙动态 TMP 字体资产（Assets/Resources/VNFonts/&lt;名字&gt;-Dynamic.asset，
    ///      由 Tools → VN Effects → Create TMP Font Asset 生成，多图集动态填充）
    ///   2. 运行时从随包 OTF（Resources/VNFonts/&lt;名字&gt;-Regular）动态创建 TMP 字体资产
    ///   3. 运行时从操作系统字体（雅黑 / Yu Gothic 等）动态创建
    /// 日文三级全失败时最终回退中文字体（汉字/假名可读，字形略不标准）。
    ///
    /// 动态图集：需要的字形按需光栅化进图集（多图集自动扩展），生僻字零缺字；
    /// 用 Prewarm() 把剧本全文预热进图集可避免台词首次渲染时的逐字光栅化卡顿。
    ///
    /// 语言切换：VNLocale 在触发 LanguageChanged 前调用 HandleLanguageChanged()，
    /// 把场景里所有仍引用旧语言字体的 TMP 文本换成新语言字体。
    /// </summary>
    public static class VNFont
    {
        /// <summary>中文预烘焙 TMP 字体资产的 Resources 路径（编辑器场景生成器也引用它）</summary>
        public const string BakedAssetPath = "VNFonts/NotoSansSC-Dynamic";
        /// <summary>中文随包源字体（OTF）的 Resources 路径</summary>
        public const string SourceFontPath = "VNFonts/NotoSansSC-Regular";
        /// <summary>日文预烘焙 TMP 字体资产的 Resources 路径</summary>
        public const string BakedAssetPathJa = "VNFonts/NotoSansJP-Dynamic";
        /// <summary>日文随包源字体（OTF）的 Resources 路径</summary>
        public const string SourceFontPathJa = "VNFonts/NotoSansJP-Regular";

        /// <summary>动态创建字体资产时的采样点大小 / 图集内边距 / 图集尺寸</summary>
        const int SamplePointSize = 64;
        const int AtlasPadding = 6;
        const int AtlasSize = 1024;

        /// <summary>UI 常用符号预热集（界面按键提示、箭头、省略号等，启动即备好）</summary>
        const string CommonUiChars =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~" +
            "，。、；：？！…—·「」『』（）《》【】“”‘’▼▲◀▶★☆♪％×";

        /// <summary>一种语言的字体来源描述（zh/en 共用 SC 档案，ja 用 JP 档案）</summary>
        class Profile
        {
            public string bakedPath;
            public string sourcePath;
            public string[] osCandidates;
        }

        static readonly Profile ScProfile = new Profile
        {
            bakedPath = BakedAssetPath,
            sourcePath = SourceFontPath,
            osCandidates = new[]
            {
                "Microsoft YaHei", "微软雅黑", "PingFang SC", "Hiragino Sans GB",
                "Noto Sans CJK SC", "Source Han Sans SC", "SimHei", "SimSun",
            },
        };

        static readonly Profile JaProfile = new Profile
        {
            bakedPath = BakedAssetPathJa,
            sourcePath = SourceFontPathJa,
            osCandidates = new[]
            {
                "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic",
                "Hiragino Kaku Gothic ProN", "Noto Sans CJK JP", "Source Han Sans",
            },
        };

        /// <summary>档案 → 已解析字体（zh/en 命中同一份缓存）</summary>
        static readonly Dictionary<Profile, TMP_FontAsset> _cache =
            new Dictionary<Profile, TMP_FontAsset>();

        /// <summary>全项目共用的 TMP 字体资产（当前语言，惰性解析，进程内缓存）</summary>
        public static TMP_FontAsset Asset => AssetFor(VNLocale.Language);

        /// <summary>指定语言的 TMP 字体资产</summary>
        public static TMP_FontAsset AssetFor(VNLanguage language)
        {
            var profile = language == VNLanguage.Japanese ? JaProfile : ScProfile;
            if (_cache.TryGetValue(profile, out var cached) && cached != null) return cached;

            var asset = LoadBaked(profile) ?? CreateFromBundledFont(profile) ?? CreateFromOsFont(profile);
            if (asset == null)
            {
                if (profile == JaProfile)
                {
                    Debug.LogWarning("[VNFont] 日文字体三级来源均不可用，回退中文字体（假名字形略不标准）");
                    asset = AssetFor(VNLanguage.Chinese);
                }
                else
                {
                    Debug.LogError("[VNFont] 所有中文字体来源均不可用，回退 TMP 默认字体（无中文字形）");
                    asset = TMP_Settings.defaultFontAsset;
                }
                _cache[profile] = asset;
                return asset;
            }

            // 日文字体挂中文 fallback：日文文本里偶发的简体专用汉字不缺字
            if (profile == JaProfile)
            {
                var sc = AssetFor(VNLanguage.Chinese);
                if (sc != null && sc != asset && asset.fallbackFontAssetTable != null &&
                    !asset.fallbackFontAssetTable.Contains(sc))
                    asset.fallbackFontAssetTable.Add(sc);
            }

            _cache[profile] = asset;
            PrewarmAsset(asset, CommonUiChars);
            return asset;
        }

        /// <summary>
        /// 把一段文本包含的全部字符预热进当前语言字体的动态图集（去重由 TMP 内部处理）。
        /// 建议在剧本加载完成时对全文调用一次，把逐字光栅化成本挪到加载期。
        /// </summary>
        public static void Prewarm(string text) => PrewarmAsset(Asset, text);

        static void PrewarmAsset(TMP_FontAsset asset, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (asset == null || asset.atlasPopulationMode != AtlasPopulationMode.Dynamic) return;
            asset.TryAddCharacters(text);
        }

        /// <summary>
        /// 语言切换（由 VNLocale.Language 的 setter 调用，先于 LanguageChanged 事件）：
        /// 把场景里所有仍引用本入口旧字体的 TMP 文本换成新语言字体。
        /// 只替换 VNFont 管理的字体，编辑期手动指定其他字体的文本不受影响。
        /// </summary>
        public static void HandleLanguageChanged()
        {
            var managed = new HashSet<TMP_FontAsset>();
            foreach (var kv in _cache)
                if (kv.Value != null) managed.Add(kv.Value);
            if (managed.Count == 0) return; // 还没有任何文本用过 VNFont

            var target = Asset;
            if (target == null) return;

            var texts = Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var text in texts)
            {
                if (text.font == target || !managed.Contains(text.font)) continue;
                text.font = target;
            }
        }

        // ------------------------------------------------------------------
        // 三级来源
        // ------------------------------------------------------------------

        static TMP_FontAsset LoadBaked(Profile profile)
        {
            var asset = Resources.Load<TMP_FontAsset>(profile.bakedPath);
            if (asset != null) Debug.Log("[VNFont] 使用预烘焙字体资产 " + profile.bakedPath);
            return asset;
        }

        static TMP_FontAsset CreateFromBundledFont(Profile profile)
        {
            var font = Resources.Load<Font>(profile.sourcePath);
            if (font == null) return null;
            var asset = CreateDynamic(font);
            if (asset != null)
                Debug.Log("[VNFont] 由随包字体运行时创建动态字体资产 " + profile.sourcePath);
            return asset;
        }

        static TMP_FontAsset CreateFromOsFont(Profile profile)
        {
            foreach (var name in profile.osCandidates)
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
