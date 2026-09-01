using System.Collections.Generic;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>参数值的来源：决定编辑器画什么控件、下拉候选从哪来</summary>
    public enum VNParamSource
    {
        Text,        // 自由文本
        Number,      // 数字
        Options,     // 固定候选（含枚举反射出来的名字）
        Character,   // 角色 id（扫 VNCharacterDef 资产）
        Expression,  // 表情名（依赖同行的角色参数，见 dependsOn）
        Background,  // 背景 id（场景 VNStage.backgrounds）
        Cg,          // CG id（场景 VNStage.cgLibrary，或 off）
        AudioBgm,    // BGM id（场景 VNAudio.bgmLibrary + 旧 library）
        AudioSe,     // SE id（场景 VNAudio.seLibrary + 旧 library）
        AudioVoice,  // 语音 id（场景 VNAudio.voiceLibrary + 旧 library）
        Label,       // 跳转目标（当前文档的 label 列表）
        Flag,        // flag 名（当前文档收集）
        EventId,     // 事件模块 id（场景 VNEventRegistry.modules）
        QuestId,     // 任务 id（项目中的 VNQuestDef 资产）
        WeatherId,   // 天气 id（内置叶型 + VNWeatherDef 资产 + 雨雪萤火虫枚举）
        UiSkinId,    // ui 命令的第二参数：候选跟着同行的 kind 变（见 dependsOn）
        InterludeId, // 过场 id（VNGameConfig 过场库里的 VNInterludeDef 资产）
        TutorialId,  // 教程 id（VNGameConfig 教程库里的 VNTutorialDef 资产）
        AssetId,     // 任意定义资产的 id（扫 t:<assetType>，取 assetIdField 字段）
    }

    /// <summary>一个命令参数的模式定义</summary>
    public class VNParamDef
    {
        public string id;              // kwarg 的键名；位置参数为内部名
        public string label;           // 界面短标签
        public bool kwarg;             // true = 生成为 key:value
        public VNParamSource source = VNParamSource.Text;
        public string[] options = System.Array.Empty<string>();
        public string defaultValue = "";   // 生成时等于默认可省略；位置参数补位用
        public string dependsOn;       // source == Expression 时指向角色参数 id
        public float weight = 1f;      // 横向布局权重

        /// <summary>
        /// 候选只是补全提示，认不出**不报错**。
        /// 用于「运行时本来就容忍陌生值」的参数：比如 event badminton 的 vs:，
        /// 对手长相与名字全由 id: 指的那份 VNBadmintonDef 决定，vs: 只是
        /// 「def 没配立绘时去角色库碰碰运气」的兜底，写个没登记的称呼完全正常。
        /// </summary>
        public bool softRef;

        // source == AssetId 时用：扫哪个 ScriptableObject 类型、id 存在哪个字段。
        // 字段名各资产不统一（quizId / badmintonId / themeId / id …），所以显式写死，
        // 不用反射猜——猜错的表现是下拉里一片空白，很难联想到原因。
        public string assetType;
        public string assetIdField;
    }

    /// <summary>一个剧本命令的模式定义</summary>
    public class VNCommandDef
    {
        public string keyword;
        public string category;        // Add 菜单分组
        public string hint;            // 提示（tooltip）
        public VNParamDef[] parameters = System.Array.Empty<VNParamDef>();
        public bool blockChoice;       // choice 特殊块
        public bool blockCamseq;       // camseq 特殊块（路径点行原样保留）

        public IEnumerable<VNParamDef> Positional()
        {
            foreach (var p in parameters) if (!p.kwarg) yield return p;
        }

        public VNParamDef FindKwarg(string key)
        {
            foreach (var p in parameters) if (p.kwarg && p.id == key) return p;
            return null;
        }
    }

    /// <summary>
    /// 剧本命令模式表：编辑器 UI / 生成 / 校验的单一数据来源。
    /// 与 VNScriptParser.Keywords 和 VNScriptRunner.Dispatch 的真实语法保持一致；
    /// 加新命令时这里同步补一条，编辑器界面自动长出对应控件。
    /// </summary>
    public static class VNScenarioSchema
    {
        public static readonly List<VNCommandDef> Commands = new List<VNCommandDef>();
        static readonly Dictionary<string, VNCommandDef> ByKeyword =
            new Dictionary<string, VNCommandDef>();

        public static VNCommandDef Find(string keyword) =>
            ByKeyword.TryGetValue(keyword, out var d) ? d : null;

        /// <summary>
        /// 取命令定义。<paramref name="variant"/> 目前只对 <c>event</c> 有意义 —— 传模块 id
        /// （badminton / photo / quiz …），拿到的定义里就带上那个模块专属的 kwarg。
        ///
        /// 【为什么要分变体】
        /// event 是通用入口，`vs:` `target:` `powerstat:` 这些是各模块自己定义的，
        /// 全塞进一张表的话每个 event 行都会画出二十几个格子；一个都不写又会让它们
        /// 全变成「unrecognized token」警告，只能在一长条文本里手打。按模块 id 取
        /// 变体，两边的毛病都没有。
        ///
        /// 认不出的模块 id（自定义模块、写成 flag 变量的动态 id）退回基础定义，
        /// 它的 kwarg 照旧走 extraTokens 原样保留 —— 只是仍会有警告，不会丢内容。
        /// </summary>
        public static VNCommandDef Find(string keyword, string variant)
        {
            var baseDef = Find(keyword);
            if (baseDef == null || keyword != "event" || string.IsNullOrEmpty(variant))
                return baseDef;
            if (!EventVariants.TryGetValue(variant, out var extra)) return baseDef;

            if (_eventVariantCache.TryGetValue(variant, out var cached)) return cached;

            var merged = new List<VNParamDef>(baseDef.parameters);
            merged.AddRange(extra);
            var def = new VNCommandDef
            {
                keyword = baseDef.keyword,
                category = baseDef.category,
                hint = baseDef.hint,
                parameters = merged.ToArray(),
                blockChoice = baseDef.blockChoice,
                blockCamseq = baseDef.blockCamseq,
            };
            _eventVariantCache[variant] = def;
            return def;
        }

        /// <summary>这个模块 id 有没有登记专属参数（编辑器决定要不要多画一行）</summary>
        public static bool HasEventVariant(string moduleId) =>
            !string.IsNullOrEmpty(moduleId) && EventVariants.ContainsKey(moduleId);

        /// <summary>基础 event 定义里的参数个数（画行时用来切分「哪些属于模块专属」）</summary>
        public static int EventBaseParamCount =>
            ByKeyword.TryGetValue("event", out var d) ? d.parameters.Length : 0;

        /// <summary>全部模块专属 kwarg 的键名集合（换模块时用来保住写过的值，见 VNScenarioDoc）</summary>
        public static readonly HashSet<string> EventKwargUniverse = new HashSet<string>();

        static readonly Dictionary<string, VNCommandDef> _eventVariantCache =
            new Dictionary<string, VNCommandDef>();

        /// <summary>
        /// event &lt;模块 id&gt; → 该模块专属的 kwarg。
        /// **唯一真相是各模块 OnLaunch 里的 ctx.Kw(...)**，加模块参数时这里同步补一行，
        /// 编辑器界面就自动长出对应控件、Lint 也不再报未知 token。
        /// </summary>
        static readonly Dictionary<string, VNParamDef[]> EventVariants =
            new Dictionary<string, VNParamDef[]>
            {
                ["qte"] = new[]
                {
                    Kw("target", "目标", VNParamSource.Number, weight: 0.5f),
                    Kw("time", "秒", VNParamSource.Number, weight: 0.5f),
                    Kw("title", "标题", VNParamSource.Text, weight: 0.8f),
                },
                ["map"] = new[]
                {
                    Kw("bg", "背景", VNParamSource.Background, weight: 0.8f),
                    Kw("title", "标题", VNParamSource.Text, weight: 0.8f),
                },
                ["shop"] = new[]
                {
                    KwAsset("id", "商店", "VNShopDef", "shopId"),
                },
                ["plan"] = new[]
                {
                    KwAsset("id", "方案", "VNPlanDef", "planId"),
                    Kw("op", "操作", VNParamSource.Options, new[] { "next" }, weight: 0.5f),
                    Kw("pool", "行动池", VNParamSource.Text, weight: 0.8f),
                    Kw("slots", "格数", VNParamSource.Number, weight: 0.4f),
                    Kw("title", "标题", VNParamSource.Text, weight: 0.7f),
                },
                ["result"] = new[]
                {
                    Kw("grade", "档位", VNParamSource.Options,
                        new[] { "fail", "normal", "good", "great" }, "normal", weight: 0.6f),
                    Kw("title", "标题", VNParamSource.Text, weight: 0.8f),
                    Kw("sub", "副标题", VNParamSource.Text, weight: 0.8f),
                    Kw("se", "音效", VNParamSource.AudioSe, weight: 0.6f),
                },
                ["battle"] = new[]
                {
                    Kw("enemy", "敌人", VNParamSource.Text, weight: 0.7f),
                    Kw("ehp", "敌HP", VNParamSource.Number, weight: 0.4f),
                    Kw("eatk", "敌攻", VNParamSource.Number, weight: 0.4f),
                    Kw("escape", "逃跑%", VNParamSource.Number, weight: 0.4f),
                    Kw("php", "我HP", VNParamSource.Number, weight: 0.4f),
                    Kw("patk", "我攻", VNParamSource.Number, weight: 0.4f),
                    Kw("pdef", "我防", VNParamSource.Number, weight: 0.4f),
                    // stat 版本优先于上面的固定值：属性名走 flag 候选，同 stat 命令
                    Kw("phpstat", "HP属性", VNParamSource.Flag, weight: 0.6f),
                    Kw("patkstat", "攻属性", VNParamSource.Flag, weight: 0.6f),
                    Kw("pdefstat", "防属性", VNParamSource.Flag, weight: 0.6f),
                    Kw("pname", "我方名", VNParamSource.Text, weight: 0.6f),
                    Kw("title", "标题", VNParamSource.Text, weight: 0.7f),
                },
                ["quiz"] = new[]
                {
                    KwAsset("id", "题库", "VNQuizDef", "quizId"),
                    Kw("count", "题数", VNParamSource.Number, weight: 0.4f),
                    Kw("time", "秒", VNParamSource.Number, weight: 0.4f),
                    Kw("pass", "及格", VNParamSource.Number, weight: 0.4f),
                    Kw("pick", "指定题号", VNParamSource.Text, weight: 0.6f),
                    Kw("flag", "成绩前缀", VNParamSource.Text, weight: 0.6f),
                    Kw("title", "标题", VNParamSource.Text, weight: 0.7f),
                },
                ["badminton"] = new[]
                {
                    // 软引用：对手的立绘与名字都由 id: 那份资产决定，vs: 只是兜底，
                    // 写「学姐」这种没登记的称呼是常态，不该报错
                    Soft(Kw("vs", "对手角色", VNParamSource.Character, weight: 0.8f)),
                    KwAsset("id", "对手资产", "VNBadmintonDef", "badmintonId"),
                    Kw("target", "目标分", VNParamSource.Number, weight: 0.4f),
                    Kw("first", "先发球", VNParamSource.Options,
                        new[] { "me", "opponent", "random" }, weight: 0.6f),
                    Kw("mode", "赛制", VNParamSource.Options,
                        new[] { "match", "free" }, weight: 0.5f),
                    Kw("powerstat", "力量属性", VNParamSource.Flag, weight: 0.7f),
                    Kw("speedstat", "速度属性", VNParamSource.Flag, weight: 0.7f),
                    Kw("jumpstat", "弹跳属性", VNParamSource.Flag, weight: 0.7f),
                    Kw("pname", "我方名", VNParamSource.Text, weight: 0.6f),
                    Kw("flag", "成绩前缀", VNParamSource.Text, weight: 0.6f),
                },
                ["photo"] = new[]
                {
                    Kw("vs", "对方角色", VNParamSource.Character, weight: 0.8f),
                    Kw("me", "主角", VNParamSource.Character, weight: 0.8f),
                    KwAsset("theme", "主题", "VNPhotoThemeDef", "themeId"),
                    KwAsset("frame", "边框", "VNPhotoFrameDef", "frameId"),
                    KwAsset("bg", "背景", "VNPhotoBackdropDef", "backdropId"),
                    Kw("mode", "模式", VNParamSource.Options,
                        new[] { "match", "free" }, weight: 0.5f),
                    Kw("time", "秒", VNParamSource.Number, weight: 0.4f),
                    Kw("stat", "加属性", VNParamSource.Flag, weight: 0.6f),
                    Kw("rate", "换算率", VNParamSource.Number, weight: 0.4f),
                    Kw("flag", "成绩前缀", VNParamSource.Text, weight: 0.6f),
                    Kw("title", "标题", VNParamSource.Text, weight: 0.7f),
                },
                ["wipefog"] = new[]
                {
                    KwAsset("id", "擦雾定义", "VNFogWipeDef", "fogWipeId"),
                    // cg 留空 = 擦舞台当前显示的那张，所以不是必填
                    Kw("cg", "要擦的CG", VNParamSource.Cg, weight: 0.8f),
                    // 软引用：只用来取台词条上的显示名，写没登记的称呼不该报错
                    Soft(Kw("vs", "角色", VNParamSource.Character, weight: 0.7f)),
                    Kw("time", "秒", VNParamSource.Number, weight: 0.4f),
                    Kw("target", "普通门槛%", VNParamSource.Number, weight: 0.5f),
                    Kw("perfect", "完美门槛%", VNParamSource.Number, weight: 0.5f),
                    Kw("stat", "加属性", VNParamSource.Flag, weight: 0.6f),
                    Kw("rate", "换算率", VNParamSource.Number, weight: 0.4f),
                    Kw("flag", "成绩前缀", VNParamSource.Text, weight: 0.6f),
                },
                ["aitalk"] = new[]
                {
                    Kw("vs", "角色", VNParamSource.Character, weight: 0.8f),
                    KwAsset("persona", "人格", "VNAiPersonaDef", "id"),
                    Kw("turns", "轮数", VNParamSource.Number, weight: 0.4f),
                    Kw("topic", "话题", VNParamSource.Text, weight: 0.9f),
                    Kw("place", "场景", VNParamSource.Text, weight: 0.8f),
                    Kw("me", "玩家名", VNParamSource.Text, weight: 0.6f),
                    Kw("options", "候选数", VNParamSource.Number, weight: 0.4f),
                    Kw("stat", "加属性", VNParamSource.Flag, weight: 0.6f),
                    Kw("rate", "换算率", VNParamSource.Number, weight: 0.4f),
                    Kw("memory", "手写往事", VNParamSource.Text, weight: 0.9f),
                    Kw("flag", "成绩前缀", VNParamSource.Text, weight: 0.6f),
                },
                ["interact"] = new[]
                {
                    Kw("vs", "角色", VNParamSource.Character, weight: 0.8f),
                    KwAsset("id", "互动定义", "VNInteractionDef", "id"),
                    Kw("items", "道具清单", VNParamSource.Text, weight: 0.9f),
                    Kw("time", "秒", VNParamSource.Number, weight: 0.4f),
                    Kw("zones", "显示部位框", VNParamSource.Options,
                        new[] { "on", "off" }, weight: 0.6f),
                    Kw("flag", "成绩前缀", VNParamSource.Text, weight: 0.6f),
                },
            };

        public static readonly string[] EaseNames =
        {
            "Linear", "InSine", "OutSine", "InOutSine", "InQuad", "OutQuad",
            "InOutQuad", "InCubic", "OutCubic", "InOutCubic", "OutBack", "InOutBack", "OutExpo",
        };

        public static readonly string[] Slots = { "left", "center", "right" };

        /// <summary>出入场方向（留空 = 按站位自动推断）</summary>
        public static readonly string[] Sides = { "left", "right", "top", "bottom" };

        public static readonly string[] EmoteNames =
            { "Surprise", "Angry", "Shy", "Dejected", "Recover", "Nod", "HeadShake" };

        /// <summary>漫符名 + clear（正名清单取自组件，保持单一真相）</summary>
        public static readonly string[] MarkNames = BuildMarkNames();

        static string[] BuildMarkNames()
        {
            var names = new List<string>(VNCharacterMarks.CanonicalNames) { "clear" };
            return names.ToArray();
        }

        public static readonly string[] FxNames =
            { "godrays", "dof", "clouds", "haze", "shimmer", "heartbeat", "dutch",
              "speedlines", "shockwave", "filmgrain", "crt", "kenburns", "letterbox",
              "meteor", "skycloud", "focus" };

        /// <summary>camseq 的缩放模式（与运行时 VNCamZoomMode 同名同序）</summary>
        public static readonly string[] CamZoomModes = { "both", "depth", "bg", "char" };

        public static readonly string[] CamAnchors =
        {
            "topleft", "top", "topright", "left", "middle", "right",
            "bottomleft", "bottom", "bottomright",
        };

        static string[] EnumNames<T>() => System.Enum.GetNames(typeof(T));

        // ---- 简写构造 ----
        static VNParamDef Pos(string id, string label, VNParamSource src,
            string[] options = null, string def = "", string dependsOn = null, float weight = 1f)
            => new VNParamDef
            {
                id = id, label = label, source = src,
                options = options ?? System.Array.Empty<string>(),
                defaultValue = def, dependsOn = dependsOn, weight = weight,
            };

        static VNParamDef Kw(string id, string label, VNParamSource src,
            string[] options = null, string def = "", string dependsOn = null, float weight = 1f)
        {
            var p = Pos(id, label, src, options, def, dependsOn, weight);
            p.kwarg = true;
            return p;
        }

        /// <summary>把参数标成「候选只是提示，认不出不报错」（见 VNParamDef.softRef）</summary>
        static VNParamDef Soft(VNParamDef p) { p.softRef = true; return p; }

        /// <summary>指向某类定义资产 id 的 kwarg（下拉候选 = 项目里该类型资产的 id）</summary>
        static VNParamDef KwAsset(string id, string label, string assetType, string assetIdField,
            float weight = 1f)
        {
            var p = Kw(id, label, VNParamSource.AssetId, weight: weight);
            p.assetType = assetType;
            p.assetIdField = assetIdField;
            return p;
        }

        static void Add(string keyword, string category, string hint,
            params VNParamDef[] parameters)
        {
            var def = new VNCommandDef
                { keyword = keyword, category = category, hint = hint, parameters = parameters };
            Commands.Add(def);
            ByKeyword[keyword] = def;
        }

        static VNScenarioSchema()
        {
            // 模块专属 kwarg 的键名总表：换模块 id 时靠它认出「这是别的模块的参数」，
            // 保住玩家写过的值而不是静默丢掉（见 VNScenarioDoc.GenerateText）
            foreach (var kv in EventVariants)
                foreach (var p in kv.Value)
                    EventKwargUniverse.Add(p.id);

            // ---- Scene ----
            Add("bg", "Scene", "bg <id> [transition:Type] [via:black]\n" +
                "转场默认**直接过渡**：新图从图案缝隙里长出来，不经过中间那片纯色。\n" +
                "写 via:black 才回到老行为（先被纯色盖满再散开），时间跳跃/章节切换那种\n" +
                "刻意要黑一下的地方用它。白闪/光斑/眨眼三种本来就是罩子，永远走老行为",
                Pos("id", "bg", VNParamSource.Background),
                Kw("transition", "transition", VNParamSource.Options, EnumNames<VNTransition>()),
                Kw("via", "via", VNParamSource.Options, new[] { "black" }, weight: 0.5f));
            Add("cg", "Scene", "cg <id|off> [transition:Type] [chars:keep] [fx:keep] [via:black]",
                Pos("id", "cg", VNParamSource.Cg),
                Kw("transition", "transition", VNParamSource.Options, EnumNames<VNTransition>()),
                Kw("chars", "chars", VNParamSource.Options, new[] { "keep" }),
                Kw("fx", "fx", VNParamSource.Options, new[] { "keep" }),
                Kw("via", "via", VNParamSource.Options, new[] { "black" }, weight: 0.5f));
            Add("weather", "Scene",
                "weather <id> [density:] [wind:] [speed:] [size:]\n" +
                "飘落类：petals/sakura（落樱）· maple（枫叶）· ginkgo（银杏）· " +
                "leaves（阔叶）· bamboo（竹叶/柳叶），也可用 VNGameConfig 飘落天气库里登记的自定义 id；\n" +
                "其余：Rain / Snow / Fireflies / None。\n" +
                "四个覆盖参数留空 = 用资产里的值；wind 为负表示向左吹（阵风由系统自动生成）",
                Pos("id", "id", VNParamSource.WeatherId, def: "None"),
                Kw("density", "密度", VNParamSource.Number, weight: 0.6f),
                Kw("wind", "风力", VNParamSource.Number, weight: 0.6f),
                Kw("speed", "速度×", VNParamSource.Number, weight: 0.6f),
                Kw("size", "大小×", VNParamSource.Number, weight: 0.6f));
            Add("bgscroll", "Scene",
                "bgscroll on|off [speed:] [dir:] [mode:] [time:]\n" +
                "背景无限滚动：一张背景图永远往一个方向流（走路/坐车/云飘）。\n" +
                "speed 是画布像素/秒（走路≈120，环境氛围≈6，默认 80）；\n" +
                "dir 可写 left/right/up/down 或角度（0=往右流 90=往上 180=往左，默认 left）；\n" +
                "mode：mirror（镜像平铺，任何图都不出接缝，默认）/ repeat（要求图本身无缝）；\n" +
                "time 是开关时速度缓入缓出的秒数（默认 0.6）。换背景不停滚动，状态进存档",
                Pos("state", "", VNParamSource.Options, new[] { "on", "off" }, "on"),
                Kw("speed", "速度", VNParamSource.Number, def: "80", weight: 0.8f),
                Kw("dir", "方向", VNParamSource.Options,
                    new[] { "left", "right", "up", "down" }, weight: 0.7f),
                Kw("mode", "平铺", VNParamSource.Options,
                    new[] { "mirror", "repeat" }, weight: 0.6f),
                Kw("time", "缓入秒", VNParamSource.Number, def: "0.6", weight: 0.4f));
            Add("mood", "Scene", "mood <type>",
                Pos("type", "type", VNParamSource.Options, EnumNames<VNMood>(), "Neutral"));
            Add("reset", "Scene", "reset effects  (weather + mood + persistent VFX)",
                Pos("target", "target", VNParamSource.Options, new[] { "effects" }, "effects"));
            Add("transition", "Scene", "transition <type>  (fullscreen, no bg change)",
                Pos("type", "type", VNParamSource.Options, EnumNames<VNTransition>(), "NoiseDissolve"));
            Add("interlude", "Scene",
                "interlude <过场id> [time:秒]\n" +
                "过场（章节标题卡）：转场图铺满 + 标题居中 + loading 图标转固定时长 + 随机一句语音。\n" +
                "内容全在 VNGameConfig「过场库」里的 VNInterludeDef 资产上配；\n" +
                "time 留空 = 用资产里的 loading 时长（默认 1.5 秒）。\n" +
                "转完自动继续，玩家点击不能提前跳过；SKIP 快进时整段跳过",
                Pos("id", "过场", VNParamSource.InterludeId),
                Kw("time", "秒", VNParamSource.Number, weight: 0.5f));
            Add("tutorial", "Scene",
                "tutorial <教程id> [force:on]\n" +
                "教程：压暗全屏 + 挖洞高亮某块 UI + 图文卡片，点一下讲下一条。\n" +
                "讲解期间整个玩法冻结（含小游戏），ESC 可跳过整篇。\n" +
                "内容全在 VNGameConfig「教程库」里的 VNTutorialDef 资产上配；\n" +
                "默认「看过就跳过」（记录是全局的，读旧档 / 新周目都不会重看），\n" +
                "force:on 强制重看；SKIP 快进时整段跳过",
                Pos("id", "教程", VNParamSource.TutorialId),
                Kw("force", "强制", VNParamSource.Options, new[] { "on", "off" }, weight: 0.5f));

            // ---- Character ----
            Add("show", "Character", "show <char> [at:] [expr:] [with:预设] [from:方向] [dur:秒]\n" +
                "with 留空 = crossfade（原地淡入）；from 留空 = 按站位推断" +
                "（站左从左边进来）；dur = 目标时长秒，留空用预设自己的节奏",
                Pos("character", "char", VNParamSource.Character),
                Kw("at", "at", VNParamSource.Options, Slots),
                Kw("expr", "expr", VNParamSource.Expression, dependsOn: "character"),
                Kw("with", "with", VNParamSource.Options, EnumNames<VNEntrancePreset>()),
                Kw("from", "从", VNParamSource.Options, Sides, weight: 0.6f),
                Kw("dur", "时长", VNParamSource.Number, weight: 0.5f));
            Add("hide", "Character", "hide <char> [with:预设] [to:方向] [dur:秒]\n" +
                "with 留空 = fade（淡出下滑）；to 留空 = 按站位推断（站左往左边走）",
                Pos("character", "char", VNParamSource.Character),
                Kw("with", "with", VNParamSource.Options, EnumNames<VNExitPreset>(), "Fade"),
                Kw("to", "往", VNParamSource.Options, Sides, weight: 0.6f),
                Kw("dur", "时长", VNParamSource.Number, weight: 0.5f));
            Add("emote", "Character", "emote <char> <motion>",
                Pos("character", "char", VNParamSource.Character),
                Pos("emote", "motion", VNParamSource.Options, EmoteNames));
            Add("mark", "Character", "mark <char> <symbol|clear> [keep|off] [pos:x,y] [size:1] [dur:1.1]\n" +
                "立绘漫符（汗滴/井字怒气/感叹号…）。默认弹一下就消失；keep = 常驻直到 off/clear。\n" +
                "位置取角色资产的 markAnchor，pos:0.2,0.36 可临时覆盖（归一化偏移，(0,0) 是立绘中心）",
                Pos("character", "char", VNParamSource.Character),
                Pos("mark", "symbol", VNParamSource.Options, MarkNames),
                Pos("mode", "mode", VNParamSource.Options,
                    new[] { "", "keep", "off" }, weight: 0.5f),
                Kw("pos", "pos", VNParamSource.Text),
                Kw("size", "size", VNParamSource.Number, def: "1"),
                Kw("dur", "dur", VNParamSource.Number, def: "1.1"));
            Add("imprint", "Character",
                "imprint <char> <trace|clear> [pos:x,y] [size:1] [life:秒] [rot:度]" + "\n" +
                "立绘痕迹（掌印/口红印/绳痕…）：印在 pos 指定的位置，随时间褪色并自行消失。" + "\n" +
                "pos 是立绘归一化坐标，(0,0) = 立绘中心，与部位框/markAnchor 同一套；" + "\n" +
                "痕迹在角色资产 VNCharacterDef.imprints 里登记。临时演出，不进存档",
                Pos("character", "char", VNParamSource.Character),
                Pos("imprint", "trace", VNParamSource.Text),
                Kw("pos", "pos", VNParamSource.Text),
                Kw("size", "size", VNParamSource.Number, def: "1"),
                Kw("life", "life", VNParamSource.Number),
                Kw("rot", "rot", VNParamSource.Number));
            Add("overlay", "Character",
                "overlay <char> <layer|clear> [strength 0~1] [time:0.35]\n" +
                "情绪叠加层（潮红/汗/泪）：与表情是加法关系，可多层共存、强度连续变化。\n" +
                "层在角色资产 VNCharacterDef.overlays 里登记；clear = 全部清空。状态进存档",
                Pos("character", "char", VNParamSource.Character),
                Pos("layer", "layer", VNParamSource.Text),
                Pos("strength", "0~1", VNParamSource.Number, def: "1", weight: 0.6f),
                Kw("time", "time", VNParamSource.Number, def: "0.35"));
            Add("move", "Character", "move <char> <slot> [seconds]",
                Pos("character", "char", VNParamSource.Character),
                Pos("at", "to", VNParamSource.Options, Slots, "center"),
                Pos("seconds", "sec", VNParamSource.Number, def: "0.6", weight: 0.5f));
            Add("portrait", "Character", "portrait on|off  (dialogue portrait)",
                Pos("value", "", VNParamSource.Options, new[] { "on", "off" }, "on"));
            Add("ui", "Scene", "ui dialogue|choice|name <id|default>\n" +
                "dialogue/choice = 皮肤切换（皮肤在 VNGameConfig 的 UI 皮肤区登记）\n" +
                "name = 名字样式切换（内置预设，不用登记）：双描边 金边 银边 霓虹 墨影 糖果 粗体 描边 底板 朴素",
                Pos("kind", "kind", VNParamSource.Options,
                    new[] { "dialogue", "choice", "name" }, "dialogue"),
                // 候选跟着 kind 走：dialogue/choice 列 VNGameConfig 里登记的皮肤，
                // name 列内置的名字样式预设
                Pos("id", "skin", VNParamSource.UiSkinId, def: "default", dependsOn: "kind"));
            // 三个位置格子都给同一张候选表：参数是「按 token 分类」而不是按位置（见
            // VNScriptRunner.ParseHideHudArgs），所以 off / keep / 部件名随便哪格都对，
            // 中间留空也不会错位。
            var hideHudTargets = new[]
            {
                "", "off", "keep", "dialogue", "stats", "calendar", "all",
            };
            Add("hideHUD", "Scene",
                "hideHUD [off] [keep] [dialogue|stats|calendar|all]…\n" +
                "隐藏界面。不写目标 = 全藏（dialogue = 对话框+快捷功能条、stats = 顶部属性栏、calendar = 日历）\n" +
                "keep = 锁定：玩家点击只推进台词不会把界面弹回来，直到剧本写 hideHUD off\n" +
                "不写 keep = 老行为：玩家按 U / Enter / Space / 鼠标左右键任意一下即恢复（该操作不推进台词）\n" +
                "例：hideHUD keep stats calendar / hideHUD off stats",
                Pos("a", "", VNParamSource.Options, hideHudTargets),
                Pos("b", "", VNParamSource.Options, hideHudTargets, weight: 0.8f),
                Pos("c", "", VNParamSource.Options, hideHudTargets, weight: 0.8f));

            // ---- Camera ----
            Add("camera", "Camera", "camera <move> [a] [b] [focus:char]\n" +
                "pushin zoom sec / snapzoom zoom / pan target sec / dolly zoom sec / reset sec",
                Pos("move", "move", VNParamSource.Options,
                    new[] { "pushin", "snapzoom", "pan", "dolly", "reset" }, "reset"),
                Pos("a", "a", VNParamSource.Text, weight: 0.5f),
                Pos("b", "b", VNParamSource.Text, weight: 0.5f),
                Kw("focus", "focus", VNParamSource.Character));
            Add("camcut", "Camera", "camcut <point> [zoom]  point = anchor / x,y / char[:part]",
                Pos("point", "point", VNParamSource.Options, CamAnchors, "middle"),
                Pos("zoom", "zoom", VNParamSource.Number, def: "1.5", weight: 0.5f));
            Add("camto", "Camera", "camto <point> [zoom] [sec] [ease:Name]",
                Pos("point", "point", VNParamSource.Options, CamAnchors, "middle"),
                Pos("zoom", "zoom", VNParamSource.Number, def: "1.4", weight: 0.5f),
                Pos("seconds", "sec", VNParamSource.Number, def: "0.8", weight: 0.5f),
                Kw("ease", "ease", VNParamSource.Options, EaseNames));
            Add("camseq", "Camera",
                "camseq [mode:both|depth|bg|char] [start:cut|fade] [end:fade] + '>' waypoint lines\n" +
                "  mode 决定「谁跟着 zoom 缩放」：\n" +
                "    both  背景+立绘一起（推拉镜 TU/TB，默认）\n" +
                "    depth 立绘比背景多缩放一点（有纵深的推拉镜，靠速度差伪 3D）\n" +
                "    bg    只有背景缩放、立绘尺寸不变（眩晕变焦，全篇 1~2 次）\n" +
                "    char  只放大立绘、背景纹丝不动（强调某人的反应；也避免背景被放糊）",
                Kw("mode", "mode", VNParamSource.Options, CamZoomModes, def: "both"),
                Kw("start", "start", VNParamSource.Options, new[] { "cut", "fade" }),
                Kw("startfade", "startfade", VNParamSource.Number, def: "0.6", weight: 0.5f),
                Kw("end", "end", VNParamSource.Options, new[] { "fade" }),
                Kw("endfade", "endfade", VNParamSource.Number, def: "0.6", weight: 0.5f));
            ByKeyword["camseq"].blockCamseq = true;

            // ---- FX ----
            Add("shake", "FX", "shake light|medium|heavy",
                Pos("level", "", VNParamSource.Options,
                    new[] { "light", "medium", "heavy" }, "medium"));
            Add("fx", "FX", "fx <name> on|off  (fx focus <char> / fx speedlines burst /\n" +
                "fx shockwave [light|heavy] 全屏水波一次性冲击)",
                Pos("name", "fx", VNParamSource.Options, FxNames),
                Pos("value", "", VNParamSource.Options,
                    new[] { "on", "off", "burst", "light", "heavy" }, "on"));
            Add("sakura", "FX", "sakura  (petal burst combo)");
            Add("liquid", "FX",
                "liquid splash|spray|click|wet|dry|cover [on|off] [x:] [y:] …\n" +
                "  splash  一次性大爆溅（x/y 为屏幕比例 0~1）\n" +
                "  dir 留空 = 朝镜头扑面而来（默认）；填了才是侧喷：0=右 90=上 180=左\n" +
                "  spray   间歇噗噗喷开关（rate 越大喷得越频繁）\n" +
                "  click   点击喷水模式：开着时左键点哪喷哪、不推进台词（Enter/空格照常推进）\n" +
                "  wet     常驻湿镜头开关（隔着车窗看雨那种，amount 是浓度）\n" +
                "  dry     把现有水渍擦干　cover  水渍层盖不盖住对话框\n" +
                "screen 是溅到镜头上的概率倍率，0 = 只有空中水花、绝不上屏",
                Pos("action", "动作", VNParamSource.Options,
                    new[] { "splash", "spray", "click", "wet", "dry", "cover" }, "splash"),
                Pos("value", "", VNParamSource.Options, new[] { "on", "off" }, "on"),
                Kw("type", "液体", VNParamSource.Options, EnumNames<VNLiquidType>(), weight: 0.7f),
                Kw("x", "x", VNParamSource.Number, def: "0.5", weight: 0.45f),
                Kw("y", "y", VNParamSource.Number, def: "0.35", weight: 0.45f),
                Kw("power", "力度", VNParamSource.Number, def: "1", weight: 0.5f),
                // def 留空：填了多少就生成多少。若把默认写成 90，用户手填 90 会被当成
                // "等于默认可省略"而消失，反而永远得不到 dir:90（那才是真正的侧喷向上）
                Kw("dir", "方向°", VNParamSource.Number, weight: 0.5f),
                Kw("spread", "张角°", VNParamSource.Number, def: "40", weight: 0.5f),
                Kw("rate", "频率×", VNParamSource.Number, def: "1", weight: 0.5f),
                Kw("screen", "上屏×", VNParamSource.Number, def: "1", weight: 0.5f),
                Kw("amount", "浓度×", VNParamSource.Number, def: "1", weight: 0.5f));
            Add("letterbox", "FX", "letterbox on|off [height:px] [time:sec]\n" +
                "电影黑边上下滑入；mood Memory（回忆）会自动上黑边",
                Pos("value", "", VNParamSource.Options, new[] { "on", "off" }, "on"),
                Kw("height", "height", VNParamSource.Number, def: "130", weight: 0.5f),
                Kw("time", "time", VNParamSource.Number, def: "0.7", weight: 0.5f));

            // ---- Audio ----
            Add("bgm", "Audio", "bgm play <id> [fade:sec] [vol:0..1] / bgm stop [fade:sec]",
                Pos("op", "", VNParamSource.Options, new[] { "play", "stop" }, "play"),
                Pos("id", "id", VNParamSource.AudioBgm),
                Kw("fade", "fade", VNParamSource.Number, def: "1.5", weight: 0.5f),
                Kw("vol", "vol", VNParamSource.Number, def: "1", weight: 0.5f));
            Add("se", "Audio", "se <id> [loop] [vol:0..1] / se stop <id>",
                Pos("a", "id/stop", VNParamSource.AudioSe),
                Pos("b", "loop/id", VNParamSource.Options, new[] { "loop" }),
                Kw("vol", "vol", VNParamSource.Number, def: "1", weight: 0.5f));
            Add("voice", "Audio", "voice <id> [vol:0..1]",
                Pos("id", "id", VNParamSource.AudioVoice),
                Kw("vol", "vol", VNParamSource.Number, def: "1", weight: 0.5f));
            Add("volume", "Audio", "volume bgm|se|voice <0..1>",
                Pos("channel", "", VNParamSource.Options, new[] { "bgm", "se", "voice" }, "bgm"),
                Pos("value", "vol", VNParamSource.Number, def: "1", weight: 0.5f));

            // ---- Flow ----
            Add("wait", "Flow", "wait <seconds>",
                Pos("seconds", "sec", VNParamSource.Number, def: "0.5"));
            Add("label", "Flow", "label <name>",
                Pos("name", "name", VNParamSource.Text));
            Add("jump", "Flow", "jump <label|scenario::label>",
                Pos("label", "to", VNParamSource.Label));
            Add("call", "Flow", "call <target> [name:value ...]  (values contain no spaces)",
                Pos("target", "to", VNParamSource.Label),
                Pos("arguments", "args", VNParamSource.Text, weight: 2f));
            Add("return", "Flow", "return  (finish the current call)");
            Add("params", "Flow", "params <required> [optional=default ...]  (place after label)",
                Pos("declaration", "declare", VNParamSource.Text, weight: 2f));
            Add("chapter", "Flow", "chapter <scenario file>",
                Pos("chapter", "file", VNParamSource.Text));
            Add("flag", "Flow", "flag <name> [value|+1|-1] [rand:min-max]\n" +
                "rand:1-100 = 区间内随机取整写入（与 value 二选一，rand 优先）",
                Pos("name", "flag", VNParamSource.Flag),
                Pos("value", "value", VNParamSource.Options,
                    new[] { "+1", "-1", "1", "0" }, weight: 0.5f),
                Kw("rand", "随机区间", VNParamSource.Text, weight: 0.5f));
            Add("stat", "Flow", "stat <name> <+n|-n|value>\n" +
                "养成属性读写：与 flag 同存 VNFlags，但按 VNStatDef 钳制范围并飘字提示",
                Pos("name", "属性", VNParamSource.Flag),
                Pos("value", "value", VNParamSource.Options,
                    new[] { "+1", "-1", "+5", "-5", "+10", "-10" }, weight: 0.5f));
            Add("time", "Flow", "time set <月份> [remain:N] / time pass [months:N] [refill:off]\n" +
                "养成日程：状态存 flag「月份/剩余月数」，右下日历 HUD 自动显示；\n" +
                "pass = 过月并把行动力回满（refill:off 关闭 / refill:<属性> 改回满对象）",
                Pos("op", "", VNParamSource.Options, new[] { "pass", "set" }, "pass"),
                Pos("month", "月份", VNParamSource.Number, weight: 0.5f),
                Kw("remain", "剩余月数", VNParamSource.Number, weight: 0.6f),
                Kw("months", "跨月数", VNParamSource.Number, weight: 0.6f),
                Kw("refill", "回满", VNParamSource.Options, new[] { "off" }, weight: 0.6f));
            Add("if", "Flow", "if <expression> jump <label>\n" +
                "supports spaces, !, + - * / %, comparisons, &&, || and parentheses",
                Pos("condition", "if", VNParamSource.Text),
                Pos("target", "jump", VNParamSource.Label));
            Add("choice", "Flow", "choice + '*' option lines\n" +
                "选项行：* 文本 [if:条件] [cost:金钱-100] [flag:好感度+1] [-> 标签]\n" +
                "if: 不满足隐藏；cost: 付不起置灰、选中自动扣（按 VNStatDef 钳制+飘字）");
            ByKeyword["choice"].blockChoice = true;
            Add("event", "Flow", "event <module id> [key:value…] + '*' outcome lines\n" +
                "运行 VNEventRegistry 登记的玩法模块（地图/战斗/迷你游戏），按结果分支\n" +
                "内置：qte 连打 / map 地图 / shop 商店 / plan 周日程排程 / result 结算弹窗\n" +
                "     battle 小战斗 / quiz 限时问答（id:题库 count:题数 time:秒 pass:及格线）\n" +
                "     badminton 羽球对战（vs:角色 id:对手 target:分 mode:match|free）\n" +
                "     photo 拍大头照（vs:角色 me:主角 theme:主题 frame:边框 bg:背景\n" +
                "           time:秒 stat:属性 rate:换算率）；写了 theme: 才评分（完美/普通/失败），\n" +
                "           不写 = 自由拍照只返回「完成」\n" +
                "     wipefog 擦雾（id:擦雾定义 cg:要擦的CG time:秒 target:普通门槛%\n" +
                "           perfect:完美门槛% stat:属性 rate:换算率）；结果 完美/普通/失败\n" +
                "           ★ 用 cg: 指定要擦的图，别在 event 之前先写 cg ——\n" +
                "             雾要到事件启动才铺得出来，先 cg 会让谜底提前揭晓；\n" +
                "             想让画面在事件后留下继续演，就在结果分支里再写 cg\n" +
                "     aitalk AI 自由聊天（vs:角色 persona:人格 turns:轮数 topic:话题 place:场景\n" +
                "           me:玩家名 stat:属性 rate:换算率 flag:成绩前缀 options:候选回复条数3~6\n" +
                "           memory:手写往事）；结果 好感提升/普通/冷场/失败\n" +
                "           ★ 必须接住「* 失败」，否则玩家断网时会卡在事件里\n" +
                "           ★ event 前先 show 角色，模块只换表情不负责出场\n" +
                "     模块专属参数（vs: / target: / powerstat: …）按上面这个 id 自动长出来，\n" +
                "     加新模块参数在 VNScenarioSchema.EventVariants 里补一行\n" +
                "tutorial: 是所有模块通用的：第一次进这个模块时先播一篇教程（看过就跳过）",
                // 存储键必须是 module 不能是 id：badminton/quiz/shop/plan/interact
                // 这些模块自己就有一个 id: 参数（对手资产 / 题库 / 商店…），
                // 两者同名的话会互相覆盖，写出 `event 新手 id:新手` 这种烂行
                Pos("module", "模块", VNParamSource.EventId),
                // 通用参数：VNEventModule 基类实现，与具体模块无关，所以放基础定义里
                Kw("tutorial", "教程", VNParamSource.TutorialId, weight: 0.7f));
            ByKeyword["event"].blockChoice = true; // 复用 choice 的「* 行」编辑与行号换算
            // ---- SNS 手机聊天 ----
            Add("sns", "SNS", "sns open <char> [id:会话] [title:标题] [me:玩家说话者名] / sns close\n" +
                "sns voice <发送者> <语音id> [text:文字稿] / sns image <发送者> <CG id> [unlock:no]\n" +
                "sns typing [秒] / sns read / sns time <自由文本> / sns system <自由文本>\n" +
                "sns reply [timeout:秒] [late:标签] [lateflag:好感-1] + '*' 回复行\n" +
                "打开后普通台词行渲染成聊天气泡（「我: 内容」= 右侧自己）",
                Pos("op", "", VNParamSource.Options,
                    new[] { "open", "close", "voice", "image", "typing", "read",
                            "time", "system", "reply" }, "open"),
                Pos("a", "对象/文本", VNParamSource.Text),
                Pos("b", "素材/秒", VNParamSource.Text, weight: 0.7f),
                Kw("id", "会话id", VNParamSource.Text, weight: 0.6f),
                Kw("title", "标题", VNParamSource.Text, weight: 0.6f),
                Kw("me", "玩家名", VNParamSource.Text, weight: 0.6f),
                Kw("text", "文字稿", VNParamSource.Text, weight: 0.6f),
                Kw("unlock", "解锁CG", VNParamSource.Options,
                    new[] { "yes", "no" }, weight: 0.5f),
                Kw("timeout", "限时", VNParamSource.Number, weight: 0.5f),
                Kw("late", "超时去向", VNParamSource.Label, weight: 0.7f),
                Kw("lateflag", "超时flag", VNParamSource.Text, weight: 0.7f));
            ByKeyword["sns"].blockChoice = true; // 复用「* 行」编辑与行号换算

            Add("quest", "Flow", "quest start|stage|done|fail <id> [阶段]\n" +
                "状态存 flag「任务_<id>」：1..n 进行中 / 100 完成 / -1 失败，J 键看日志",
                Pos("op", "", VNParamSource.Options,
                    new[] { "start", "stage", "done", "fail" }, "start"),
                Pos("id", "id", VNParamSource.QuestId),
                Pos("stage", "阶段", VNParamSource.Number, weight: 0.5f));

            Debug.Assert(Commands.Count > 0);
        }
    }
}
