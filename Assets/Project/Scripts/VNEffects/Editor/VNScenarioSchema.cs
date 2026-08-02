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
            // ---- Scene ----
            Add("bg", "Scene", "bg <id> [transition:Type]",
                Pos("id", "bg", VNParamSource.Background),
                Kw("transition", "transition", VNParamSource.Options, EnumNames<VNTransition>()));
            Add("cg", "Scene", "cg <id|off> [transition:Type] [chars:keep] [fx:keep]",
                Pos("id", "cg", VNParamSource.Cg),
                Kw("transition", "transition", VNParamSource.Options, EnumNames<VNTransition>()),
                Kw("chars", "chars", VNParamSource.Options, new[] { "keep" }),
                Kw("fx", "fx", VNParamSource.Options, new[] { "keep" }));
            Add("weather", "Scene", "weather <type>",
                Pos("type", "type", VNParamSource.Options, EnumNames<VNWeather>(), "None"));
            Add("mood", "Scene", "mood <type>",
                Pos("type", "type", VNParamSource.Options, EnumNames<VNMood>(), "Neutral"));
            Add("reset", "Scene", "reset effects  (weather + mood + persistent VFX)",
                Pos("target", "target", VNParamSource.Options, new[] { "effects" }, "effects"));
            Add("transition", "Scene", "transition <type>  (fullscreen, no bg change)",
                Pos("type", "type", VNParamSource.Options, EnumNames<VNTransition>(), "NoiseDissolve"));

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
            Add("move", "Character", "move <char> <slot> [seconds]",
                Pos("character", "char", VNParamSource.Character),
                Pos("at", "to", VNParamSource.Options, Slots, "center"),
                Pos("seconds", "sec", VNParamSource.Number, def: "0.6", weight: 0.5f));
            Add("portrait", "Character", "portrait on|off  (dialogue portrait)",
                Pos("value", "", VNParamSource.Options, new[] { "on", "off" }, "on"));
            Add("ui", "Scene", "ui dialogue|choice <skinId|default>\n" +
                "对话框/选项面板皮肤切换（皮肤在 VNGameConfig 的 UI 皮肤区登记）",
                Pos("kind", "kind", VNParamSource.Options,
                    new[] { "dialogue", "choice" }, "dialogue"),
                Pos("id", "skin", VNParamSource.Text, def: "default"));

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
            Add("camseq", "Camera", "camseq [start:cut|fade] [end:fade] + '>' waypoint lines",
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
                "     battle 小战斗 / quiz 限时问答（id:题库 count:题数 time:秒 pass:及格线）",
                Pos("id", "id", VNParamSource.EventId));
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
