using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 游戏内容总配置（ScriptableObject）——把"重建场景会丢"的人工绑定搬进资产。
    ///
    /// 【为什么存在】
    /// Tools → VN Effects → 演示场景 Demo Scenes → 重建剧本演示场景 Create Script Demo Scene 内部会
    /// EditorSceneManager.NewScene(EmptyScene)，即**丢弃当前场景从零重造**。
    /// 于是所有挂在场景组件上的引用（背景库/音频库/入口剧本/地图坐标…）每次重建全部清空。
    /// 本资产把这些数据搬到 Assets/ 里，场景怎么重建都不受影响。
    ///
    /// 【放在 Resources 下的理由】
    /// 资产路径固定为 Assets/Resources/VNGameConfig.asset，运行时用 Resources.Load 直接取，
    /// **场景里一个引用字段都不需要**（组件上的 config 字段只是可选覆盖）。
    /// 这样"引用面积"归零 —— 重建场景后无需拖任何东西。
    ///
    /// 【覆盖语义】★ 只有一条规则
    ///   本资产里**填了的**列表/字段 → 覆盖场景组件上的同名设置；
    ///   **留空的** → 保持场景组件原样。
    /// 所以想临时在场景里试别的东西，把这里对应项清空即可，不用删资产。
    ///
    /// 【什么该放这儿，什么不该】
    ///   放这儿：无法从文件名推断、需要人工调的数据
    ///           —— 背景 id→图 的映射、每条音频的基准音量、地图地点坐标与条件、入口剧本
    ///   不放这儿：能靠"扫目录"自动得到的东西
    ///           —— 角色/属性/商店/日程/任务定义资产、章节剧本、CG
    ///             （这些由生成器扫 Assets/ 目录自动登记，见 VNGameConfigTools）
    ///   下面依然为后者留了字段：填了就覆盖扫描结果，用于"我就要手动指定顺序"的场合。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Game Config", fileName = "VNGameConfig")]
    public class VNGameConfig : ScriptableObject
    {
        /// <summary>Resources 下的固定资产名（不带扩展名）</summary>
        public const string ResourcesName = "VNGameConfig";
        /// <summary>编辑器工具与生成器共用的固定路径</summary>
        public const string AssetPath = "Assets/Resources/VNGameConfig.asset";

        static VNGameConfig _active;
        static bool _lookedUp;

        /// <summary>
        /// 当前生效的配置：优先返回已缓存的，否则从 Resources 加载。
        /// 找不到时返回 null（此时全系统退回"用场景组件上的设置"，与旧版行为一致）。
        /// </summary>
        public static VNGameConfig Active
        {
            get
            {
                if (_active != null) return _active;
                if (_lookedUp) return null;
                _lookedUp = true;
                _active = Resources.Load<VNGameConfig>(ResourcesName);
                return _active;
            }
        }

        /// <summary>组件上显式指定了 config 时调用，让后续查找都用它。</summary>
        public static void SetActive(VNGameConfig config)
        {
            if (config == null) return;
            _active = config;
            _lookedUp = true;
        }

        /// <summary>编辑器改完资产后清缓存（play mode 之间不残留旧引用）。</summary>
        public static void ClearCache()
        {
            _active = null;
            _lookedUp = false;
            // AI 那两处也各缓存了一份从 config 里读出来的东西（默认供应商 / 单价表）。
            // 不一起清的话，在 Inspector 里把供应商从 Gemini 改成 DeepSeek 之后
            // 还会继续发给 Gemini，直到下次域重载——这种「改了没反应」最难查。
            VNAiProviders.Invalidate();
            VNAiPricing.Invalidate();
        }

        // ==============================================================
        // 剧本入口
        // ==============================================================

        [Header("──────── 剧本 ────────")]
        [Header("入口剧本（留空 = 用 VNScriptRunner 上原有的 script）")]
        public TextAsset entryScript;

        [Header("章节剧本列表（jump 文件::标签 / chapter 的目标必须登记在此）\n" +
                "留空 = 由生成器扫 Assets/Scenarios/*.vn.txt 自动登记")]
        public List<TextAsset> chapters = new List<TextAsset>();

        // ==============================================================
        // 标题画面（VNTitleMenu 读取；全部留空也能工作）
        // ==============================================================

        [Header("──────── 标题画面 ────────")]
        [Header("游戏标题（留空 = \"Visual Novel\"；En/Ja 留空回退中文）")]
        public string gameTitle;
        public string gameTitleEn;
        public string gameTitleJa;

        [Header("标题背景 id（须在背景库登记；留空 = 背景库第一张）")]
        public string titleBackground;

        [Header("标题 BGM id（须在 BGM 库登记；留空 = 标题不播音乐）")]
        public string titleBgm;

        // ==============================================================
        // UI 皮肤（对话框 / 选项面板；剧本 ui dialogue|choice <id> 切换）
        // ==============================================================

        // ★ 列表元素里的字段说明一律用 [Tooltip]，**绝对不要用 [Header]**——
        //   它会把控件区域往下推，在自定义 drawer 的固定 rect 里表现为
        //   文字叠印 + 输入框点不进去。详见 VNStage.BackgroundEntry 上的注释。
        [System.Serializable]
        public class UiSkinEntry
        {
            [Tooltip("剧本 ui 命令引用的 id（可中文，如 华丽 / 顶部）")]
            public string id;
            [Tooltip("皮肤 prefab（根上挂 VNDialogueSkin 或 VNChoiceSkin）")]
            public GameObject prefab;
        }

        [Header("──────── UI 皮肤 ────────")]
        [Header("对话框皮肤库（起点：Tools → VN Effects → UI 皮肤 UI Skins 导出默认皮肤后复制修改）")]
        public List<UiSkinEntry> dialogueSkins = new List<UiSkinEntry>();
        [Header("选项面板皮肤库")]
        public List<UiSkinEntry> choiceSkins = new List<UiSkinEntry>();

        [Header("系统菜单全局主题（留空 = 标题/设置/CG/回想/工具条/存读档/状态均使用程序化默认 UI）")]
        public VNSystemUiSkinSet systemUiSkin;

        /// <summary>按 id 查皮肤 prefab；找不到或 id 为空返回 null</summary>
        public static GameObject FindSkin(List<UiSkinEntry> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id)) return null;
            foreach (var e in list)
                if (e != null && e.id == id) return e.prefab;
            return null;
        }

        // ==============================================================
        // 舞台内容
        // ==============================================================

        [Header("──────── 舞台 ────────")]
        [Header("角色定义（留空 = 生成器扫 Assets/VNEffects/Characters 自动登记）")]
        public List<VNCharacterDef> characters = new List<VNCharacterDef>();

        [Header("背景库：剧本 bg 命令的 id → 图\n" +
                "★ 素材散放时靠这里手工映射，拖一次永久保留，重建场景不丢")]
        public List<VNStage.BackgroundEntry> backgrounds = new List<VNStage.BackgroundEntry>();

        [Header("CG 库（留空 = 生成器扫 Assets/CG 自动登记，文件名 = id）")]
        public List<VNStage.CgEntry> cgLibrary = new List<VNStage.CgEntry>();

        [Header("飘落天气库：剧本 weather 命令的 id → 参数资产（落樱/枫叶/银杏/落叶/竹叶）\n" +
                "留空也能用 —— 内置五套预设走 petals/maple/ginkgo/leaves/bamboo 这些名字；\n" +
                "登记自定义资产后可以用中文 id，并覆盖同名内置预设")]
        public List<VNWeatherDef> weatherDefs = new List<VNWeatherDef>();

        [Header("过场库：剧本 interlude 命令的 id -> 过场资产（章节标题卡）\n" +
                "留空 = 生成器扫全项目的 VNInterludeDef 自动登记")]
        public List<VNInterludeDef> interludes = new List<VNInterludeDef>();

        [Header("全局转场图池：过场资产没写自己的图池时，从这里随机抽一张铺满\n" +
                "这些图不进 CG 鉴赏画廊 —— 它们是演出素材，不是收集品")]
        public List<Sprite> interludeImages = new List<Sprite>();

        [Header("教程库：剧本 tutorial 命令与模块首次自动播引用的 id → 教程资产\n" +
                "留空 = 生成器扫全项目的 VNTutorialDef 自动登记")]
        public List<VNTutorialDef> tutorials = new List<VNTutorialDef>();

        // ==============================================================
        // 音频
        // ==============================================================

        [Header("──────── 音频 ────────")]
        [Header("BGM 库（id + 素材 + 基准音量标定）")]
        public List<VNAudio.AudioEntry> bgmLibrary = new List<VNAudio.AudioEntry>();
        [Header("SE 库")]
        public List<VNAudio.AudioEntry> seLibrary = new List<VNAudio.AudioEntry>();
        [Header("Voice 库")]
        public List<VNAudio.AudioEntry> voiceLibrary = new List<VNAudio.AudioEntry>();

        [Header("打字音（留空 = 保持场景 VNAudio 上原有设置）")]
        public AudioClip typingTick;

        [Header("通道音量覆盖（勾上才生效，否则用场景 VNAudio 的值）")]
        public bool overrideChannelVolumes;
        [Range(0f, 1f)] public float bgmVolume = 0.75f;
        [Range(0f, 1f)] public float seVolume = 1f;
        [Range(0f, 1f)] public float voiceVolume = 1f;

        // ==============================================================
        // 玩法模块
        // ==============================================================

        [Header("──────── 玩法 ────────")]
        [Header("地图底图（event map 用；留空 = 保持模板原设置）")]
        public Sprite mapSprite;
        [Header("地图地点（名字 = 剧本结果名；坐标 0~1；条件为 VNFlags 表达式）")]
        public List<VNMapModule.Location> mapLocations = new List<VNMapModule.Location>();

        [Header("属性 / 商店 / 日程 / 任务 / 题库 / 羽球对手定义\n留空 = 生成器扫对应目录自动登记")]
        public List<VNStatDef> stats = new List<VNStatDef>();
        public List<VNShopDef> shops = new List<VNShopDef>();
        public List<VNPlanDef> plans = new List<VNPlanDef>();
        public List<VNQuestDef> quests = new List<VNQuestDef>();
        public List<VNQuizDef> quizzes = new List<VNQuizDef>();
        public List<VNBadmintonDef> badmintons = new List<VNBadmintonDef>();

        [Header("AI 自由聊天人格（event aitalk persona: 引用）\n" +
                "留空 = 生成器扫全工程 VNAiPersonaDef 自动登记")]
        public List<VNAiPersonaDef> aiPersonas = new List<VNAiPersonaDef>();

        [Header("AI 默认供应商。人格资产的「供应商」选「跟随全局」时用这个\n" +
                "→ 一处改，全部人格跟着换（Gemini ⇄ DeepSeek）")]
        public VNAiProvider aiProvider = VNAiProvider.DeepSeek;

        [Header("默认模型名。留空 = 该供应商的默认模型\n" +
                "Gemini：gemini-3.5-flash-lite　DeepSeek：deepseek-v4-flash / deepseek-v4-pro")]
        public string aiModel;

        [Header("AI 模型单价表（算成本用）。留空 = 用内置默认表\n" +
                "换了模型却不改这里的话，日志里的成本数字会静默偏低")]
        public VNAiPricingDef aiPricing;

        /// <summary>按 id 查 AI 人格；找不到返回 null（调用方负责告警）</summary>
        public VNAiPersonaDef FindAiPersona(string personaId)
        {
            if (aiPersonas == null || string.IsNullOrEmpty(personaId)) return null;
            foreach (var p in aiPersonas)
                if (p != null && p.id == personaId) return p;
            return null;
        }

        [Header("大头贴：边框样式 / 贴纸 / 背景 / 拍照主题（event photo 用）")]
        public List<VNPhotoFrameDef> photoFrames = new List<VNPhotoFrameDef>();
        public List<VNPhotoStickerDef> photoStickers = new List<VNPhotoStickerDef>();
        public List<VNPhotoBackdropDef> photoBackdrops = new List<VNPhotoBackdropDef>();
        public List<VNPhotoThemeDef> photoThemes = new List<VNPhotoThemeDef>();

        [Header("大头贴里「我」默认用哪个角色的立绘（剧本 me: 可覆盖）")]
        public string photoMeCharacterId = "";

        // ==============================================================
        // 应用辅助
        // ==============================================================

        /// <summary>
        /// 覆盖语义的统一实现：source 非空则把 target 换成 source 的副本，否则原样不动。
        /// 返回是否发生了覆盖（供日志使用）。
        /// </summary>
        public static bool ApplyList<T>(List<T> source, ref List<T> target)
        {
            if (source == null || source.Count == 0) return false;
            target = new List<T>(source);
            return true;
        }
    }
}
