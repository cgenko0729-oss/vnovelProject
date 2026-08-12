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
    ///   中文：霞鹜文楷 LXGW WenKai（手写楷体），生僻字缺字时兜底 Noto Sans SC
    ///   英文：Noto Sans SC（拉丁字形齐全）
    ///   日文：Noto Sans JP（SC 的假名字形不合日文排印规范，必须独立字体），
    ///        生僻字缺字时同样兜底 Noto Sans SC（不用中文的楷体，避免风格突兀且缺字覆盖不如 Noto 全）
    ///
    /// 每种语言的解析顺序（三级兜底，保证任何情况下都能显示）：
    ///   1. 预烘焙动态 TMP 字体资产（Assets/Resources/VNFonts/&lt;名字&gt;-Dynamic.asset，
    ///      由 Tools → VN Effects → Create TMP Font Asset 生成，多图集动态填充）
    ///   2. 运行时从随包字体文件（Resources/VNFonts/&lt;名字&gt;-Regular）动态创建 TMP 字体资产
    ///   3. 运行时从操作系统字体（雅黑 / Yu Gothic 等）动态创建
    /// 三级全失败时回退到该语言登记的 fallback 档案（见 Profile.fallback）。
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
        /// <summary>中文随包源字体（霞鹜文楷 LXGW WenKai）的 Resources 路径</summary>
        public const string SourceFontPath = "VNFonts/LXGWWenKaiTC-Regular";
        /// <summary>英文 / 兜底通用 CJK 预烘焙 TMP 字体资产（Noto Sans SC）的 Resources 路径</summary>
        public const string BakedAssetPathGeneral = "VNFonts/NotoSansSC-General-Dynamic";
        /// <summary>英文 / 兜底通用 CJK 随包源字体（Noto Sans SC）的 Resources 路径</summary>
        public const string SourceFontPathGeneral = "VNFonts/NotoSansSC-Regular";
        /// <summary>日文预烘焙 TMP 字体资产的 Resources 路径</summary>
        public const string BakedAssetPathJa = "VNFonts/NotoSansJP-Dynamic";
        /// <summary>日文随包源字体（OTF）的 Resources 路径</summary>
        public const string SourceFontPathJa = "VNFonts/NotoSansJP-Regular";

        // ---- 装饰字体（Display）：名牌等少量大字用的 Heavy 字重 ----
        /// <summary>中文 / 英文装饰字体预烘焙资产路径（思源黑体 Black，暂未烘焙时自动走随包源字体）</summary>
        public const string BakedAssetPathDisplay = "VNFonts/NotoSansSC-Black-Dynamic";
        /// <summary>中文 / 英文装饰随包源字体（思源黑体 Black）</summary>
        public const string SourceFontPathDisplay = "VNFonts/NotoSansSC-Black";
        /// <summary>日文装饰字体预烘焙资产路径</summary>
        public const string BakedAssetPathDisplayJa = "VNFonts/NotoSansJP-Black-Dynamic";
        /// <summary>日文装饰随包源字体（思源黑体 JP Black）。
        /// 不能拿 SC Black 顶：SC 的假名字形不合日文排印规范（与正文字体同一条原则）。</summary>
        public const string SourceFontPathDisplayJa = "VNFonts/NotoSansJP-Black";

        /// <summary>动态创建字体资产时的采样点大小 / 图集内边距 / 图集尺寸</summary>
        const int SamplePointSize = 64;
        const int AtlasPadding = 6;
        const int AtlasSize = 1024;

        /// <summary>
        /// 装饰字体的采样点大小。比正文的 64 大得多，是因为 padding 必须跟着
        /// 采样点等比例放大——**padding 相对采样点过大反而会毁掉描边**：
        /// 实测 64pt 采样配 padding 24（37%）时，字形在图集里的有效分辨率被挤掉，
        /// SDF 梯度变缓，描边和投影一起糊成一层淡影，比 padding 14 还差。
        /// 120pt 采样配 padding 22 保持在 ~18%，既能撑住厚描边又不糊。
        /// </summary>
        const int DisplaySamplePointSize = 120;

        /// <summary>
        /// 装饰字体的图集内边距。SDF 的描边 / 外发光只能长在 padding 里：
        /// 描边实际像素厚度 ≈ outlineWidth ×(padding+1)×(显示字号/采样点)，
        /// 所以 padding 是描边粗细的天花板——padding 14 时描边推到 0.2 就饱和，
        /// 再调大数值没有任何视觉变化（不是被切角，是直接被钳住）。
        /// 正文那几千个汉字用不起这么大的采样点和 padding（图集会爆），
        /// 所以装饰字体单开一套资产：反正只有角色名那十几个字。
        /// </summary>
        const int DisplayAtlasPadding = 22;
        /// <summary>装饰字体图集尺寸（字少但每个字占地大，1024 起步，不够时自动多图集扩展）</summary>
        const int DisplayAtlasSize = 1024;

        /// <summary>UI 常用符号预热集（界面按键提示、箭头、省略号等，启动即备好）</summary>
        const string CommonUiChars =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~" +
            "，。、；：？！…—·「」『』（）《》【】“”‘’▼▲◀▶★☆♪％×";

        /// <summary>一种语言的字体来源描述</summary>
        class Profile
        {
            public string bakedPath;
            public string sourcePath;
            public string[] osCandidates;
            /// <summary>解析失败时兜底改用的档案；解析成功时也会把它挂进 TMP fallback 表补缺字</summary>
            public Profile fallback;
            /// <summary>动态创建时的图集内边距（装饰字体要留出粗描边的空间）</summary>
            public int padding = AtlasPadding;
            /// <summary>动态创建时的采样点大小（必须与 padding 等比例，见 DisplaySamplePointSize）</summary>
            public int samplePointSize = SamplePointSize;
            /// <summary>动态创建时的图集边长</summary>
            public int atlasSize = AtlasSize;
            /// <summary>是否为装饰字体档案（语言切换时与正文字体分开替换）</summary>
            public bool isDisplay;
        }

        static readonly string[] ScOsCandidates =
        {
            "Microsoft YaHei", "微软雅黑", "PingFang SC", "Hiragino Sans GB",
            "Noto Sans CJK SC", "Source Han Sans SC", "SimHei", "SimSun",
        };

        /// <summary>英文 UI 用，同时也是中文毛笔体 / 日文字体的缺字兜底（拉丁 + CJK 字形齐全）</summary>
        static readonly Profile GeneralCjkProfile = new Profile
        {
            bakedPath = BakedAssetPathGeneral,
            sourcePath = SourceFontPathGeneral,
            osCandidates = ScOsCandidates,
        };

        static readonly Profile ZhProfile = new Profile
        {
            bakedPath = BakedAssetPath,
            sourcePath = SourceFontPath,
            osCandidates = ScOsCandidates,
            fallback = GeneralCjkProfile,
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
            fallback = GeneralCjkProfile,
        };

        // ------------------------------------------------------------------
        // 装饰字体档案（Heavy 字重，只给名牌这类少量大字用）
        // ------------------------------------------------------------------

        /// <summary>中文 / 英文装饰字体（思源黑体 Black）。缺字兜底回正文 Noto Regular。</summary>
        static readonly Profile DisplayZhProfile = new Profile
        {
            bakedPath = BakedAssetPathDisplay,
            sourcePath = SourceFontPathDisplay,
            osCandidates = ScOsCandidates,
            fallback = GeneralCjkProfile,
            padding = DisplayAtlasPadding,
            samplePointSize = DisplaySamplePointSize,
            atlasSize = DisplayAtlasSize,
            isDisplay = true,
        };

        /// <summary>日文装饰字体（思源黑体 JP Black）。
        /// 缺字先回中文 Black 保住粗字重，再由它接力兜到 Regular。</summary>
        static readonly Profile DisplayJaProfile = new Profile
        {
            bakedPath = BakedAssetPathDisplayJa,
            sourcePath = SourceFontPathDisplayJa,
            osCandidates = new[]
            {
                "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic",
                "Hiragino Kaku Gothic ProN", "Noto Sans CJK JP", "Source Han Sans",
            },
            fallback = DisplayZhProfile,
            padding = DisplayAtlasPadding,
            samplePointSize = DisplaySamplePointSize,
            atlasSize = DisplayAtlasSize,
            isDisplay = true,
        };

        /// <summary>档案 → 已解析字体</summary>
        static readonly Dictionary<Profile, TMP_FontAsset> _cache =
            new Dictionary<Profile, TMP_FontAsset>();

        /// <summary>
        /// 场景加载前抢跑三语解析，把缺字兜底表提前挂好。
        /// 不这样做的话：UI 皮肤 prefab 直接烘焙引用的中文字体对象，
        /// 如果在本局游戏里第一次显示文字时还没人调用过 VNFont.Asset(Chinese)，
        /// fallback 还没挂上去，缺字就会先露一次方框（标题/存档界面常见，
        /// 因为它们往往在对话框第一次 Say() 之前就显示了）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Warmup()
        {
            AssetFor(VNLanguage.Chinese);
            AssetFor(VNLanguage.English);
            AssetFor(VNLanguage.Japanese);
        }

        /// <summary>全项目共用的 TMP 字体资产（当前语言，惰性解析，进程内缓存）</summary>
        public static TMP_FontAsset Asset => AssetFor(VNLocale.Language);

        /// <summary>指定语言的 TMP 字体资产</summary>
        public static TMP_FontAsset AssetFor(VNLanguage language)
        {
            Profile profile;
            switch (language)
            {
                case VNLanguage.Chinese: profile = ZhProfile; break;
                case VNLanguage.Japanese: profile = JaProfile; break;
                default: profile = GeneralCjkProfile; break;
            }
            return Resolve(profile);
        }

        /// <summary>
        /// 装饰字体（Heavy 字重，当前语言）：名牌等少量大字专用，图集 padding 大，
        /// 撑得住粗描边 / 外发光。正文一律别用它——图集成本和字形气质都不对。
        /// </summary>
        public static TMP_FontAsset DisplayAsset => DisplayAssetFor(VNLocale.Language);

        /// <summary>指定语言的装饰字体资产</summary>
        public static TMP_FontAsset DisplayAssetFor(VNLanguage language)
        {
            var profile = language == VNLanguage.Japanese ? DisplayJaProfile : DisplayZhProfile;
            return Resolve(profile);
        }

        static TMP_FontAsset Resolve(Profile profile)
        {
            if (_cache.TryGetValue(profile, out var cached) && cached != null) return cached;

            var asset = LoadBaked(profile) ?? CreateFromBundledFont(profile) ?? CreateFromOsFont(profile);
            if (asset == null)
            {
                if (profile.fallback != null)
                {
                    Debug.LogWarning("[VNFont] " + profile.sourcePath + " 字体来源均不可用，回退 " + profile.fallback.sourcePath);
                    asset = Resolve(profile.fallback);
                }
                else
                {
                    Debug.LogError("[VNFont] 所有字体来源均不可用，回退 TMP 默认字体（可能无中文字形）");
                    asset = TMP_Settings.defaultFontAsset;
                }
                _cache[profile] = asset;
                return asset;
            }

            // 挂上缺字兜底：生僻字在主字体里找不到时，TMP 自动去 fallback 表里找
            if (profile.fallback != null)
            {
                var fb = Resolve(profile.fallback);
                if (fb != null && fb != asset && asset.fallbackFontAssetTable != null &&
                    !asset.fallbackFontAssetTable.Contains(fb))
                    asset.fallbackFontAssetTable.Add(fb);
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
            // 正文字体与装饰字体必须分开替换：把名牌的 Heavy 字体也换成正文字体，
            // 粗描边样式会当场垮掉（这正是加装饰字体后最容易踩的坑）。
            var managedBody = new HashSet<TMP_FontAsset>();
            var managedDisplay = new HashSet<TMP_FontAsset>();
            foreach (var kv in _cache)
            {
                if (kv.Value == null) continue;
                if (kv.Key.isDisplay) managedDisplay.Add(kv.Value);
                else managedBody.Add(kv.Value);
            }
            if (managedBody.Count == 0 && managedDisplay.Count == 0)
                return; // 还没有任何文本用过 VNFont

            var bodyTarget = Asset;
            var displayTarget = managedDisplay.Count > 0 ? DisplayAsset : null;
            if (bodyTarget == null && displayTarget == null) return;

            var texts = Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var text in texts)
            {
                var font = text.font;
                if (font == null) continue;
                // 先判正文：装饰字体解析失败降级成正文字体时两个集合会重合，
                // 这种情况下它本来就已经退化成正文字体了，按正文处理即可。
                if (managedBody.Contains(font))
                {
                    if (bodyTarget != null && font != bodyTarget) text.font = bodyTarget;
                }
                else if (managedDisplay.Contains(font))
                {
                    if (displayTarget != null && font != displayTarget) text.font = displayTarget;
                }
            }

            DisplayFontChanged?.Invoke();
        }

        /// <summary>
        /// 装饰字体因语言切换而更换后触发。换 font 会让 TMP 丢掉之前的材质实例，
        /// 名牌这类自定义了描边 / 渐变的文本必须收到通知后重新应用样式。
        /// </summary>
        public static event System.Action DisplayFontChanged;

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
            var asset = CreateDynamic(font, profile);
            if (asset != null)
                Debug.Log("[VNFont] 由随包字体运行时创建动态字体资产 " + profile.sourcePath);
            return asset;
        }

        static TMP_FontAsset CreateFromOsFont(Profile profile)
        {
            foreach (var name in profile.osCandidates)
            {
                var font = Font.CreateDynamicFontFromOSFont(name, profile.samplePointSize);
                if (font == null) continue;
                var asset = CreateDynamic(font, profile);
                if (asset != null)
                {
                    Debug.LogWarning("[VNFont] 随包字体缺失，回退操作系统字体：" + name);
                    return asset;
                }
            }
            return null;
        }

        static TMP_FontAsset CreateDynamic(Font source, Profile profile)
        {
            var asset = TMP_FontAsset.CreateFontAsset(
                source, profile.samplePointSize, profile.padding, GlyphRenderMode.SDFAA,
                profile.atlasSize, profile.atlasSize, AtlasPopulationMode.Dynamic, true);
            if (asset != null)
                asset.name = source.name + (profile.isDisplay ? " (VNFont Display)" : " (VNFont Dynamic)");
            return asset;
        }
    }
}
