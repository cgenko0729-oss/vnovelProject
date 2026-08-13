using System.Text;

namespace VNEffects
{
    /// <summary>
    /// AI 输出文本的兜底清洗 —— 目前只做一件事：**繁体转简体**。
    ///
    /// 【为什么需要它】
    /// 提示词里已经写了「全部使用简体中文，禁止繁体字」，还把这条挪到了
    /// system prompt 的最后一行（权重最高的位置）。但实测**仍然会漏**：
    /// 三次抽查三次都混出了繁体（「才、才沒有」「我还沒想好」「眞希望」）。
    /// 模型对这种字级约束的遵守本质上是概率性的，提示词压不到 100%。
    ///
    /// 而这是玩家**直接看得见**的东西——对话框里蹦出一个「沒」，
    /// 和游戏其余所有文本的字体风格都对不上。所以在代码层加一道确定性的兜底：
    /// 提示词负责让它「大部分时候写对」，这里负责让它「永远不出错」。
    ///
    /// 【范围】
    /// 只覆盖中文 AI 输出里最常漏的那批高频字，不是完整的繁简转换方案
    /// （那需要 OpenCC 级别的词库，且要处理一简对多繁的歧义）。
    /// 漏了新的字就往表里加一对——这个表是给「兜底」用的，不追求学术完备。
    ///
    /// 【刻意不处理的】
    /// 一简对多繁的反向歧义（如「后/後」「面/麵」「几/幾」）在**繁→简**方向
    /// 是安全的：多个繁体字映射到同一个简体字不会产生歧义。
    /// 真正危险的是简→繁，本文件不做那个方向。
    /// </summary>
    public static class VNAiTextNormalize
    {
        // 成对写：偶数位是繁体，奇数位是对应简体。
        // 顺序无所谓，查表是逐字符 O(1)。
        const string Pairs =
            "沒没眞真說说來来時时個个們们這这樣样爲为對对後后點点還还會会覺觉開开關关" +
            "門门問问間间與与體体實实現现學学於于無无術术風风雲云電电話话語语讀读寫写" +
            "聽听見见愛爱樂乐錢钱買买賣卖車车東东西西馬马鳥鸟魚鱼鷄鸡龍龙飛飞書书畫画" +
            "紙纸筆笔師师長长員员務务動动務务參参發发變变總总經经過过運运遠远進进" +
            "選选連连達达遊游適适邊边師师張张陳陈劉刘楊杨黃黄趙赵週周歲岁對对將将" +
            "強强練练習习題题課课業业類类數数統统傳传統统條条線线結结給给續续終终" +
            "說说認认識识記记憶忆懷怀戀恋離离歸归難难願愿興兴舉举處处態态氣气熱热" +
            "點点煩烦驚惊嚇吓歡欢傷伤緊紧鬆松輕轻聲声響响靜静轉转彎弯錯错準准" +
            "節节當当兩两倆俩幫帮讓让該该應应須须將将決决擇择贏赢輸输戰战爭争" +
            "報报導导視视覺觉聞闻據据專专屬属幾几麼么麽么嗎吗嘛嘛呢呢啊啊";

        static readonly System.Collections.Generic.Dictionary<char, char> Map = BuildMap();

        static System.Collections.Generic.Dictionary<char, char> BuildMap()
        {
            var map = new System.Collections.Generic.Dictionary<char, char>(Pairs.Length / 2);
            for (int i = 0; i + 1 < Pairs.Length; i += 2)
            {
                char trad = Pairs[i], simp = Pairs[i + 1];
                if (trad != simp) map[trad] = simp;   // 表里有几对是自映射，跳过
            }
            return map;
        }

        /// <summary>
        /// 把文本里的常见繁体字换成简体。没有命中任何字时**返回原字符串本身**
        /// （不新建对象），所以绝大多数调用是零分配的。
        /// </summary>
        public static string ToSimplified(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                if (!Map.TryGetValue(text[i], out char simp)) continue;
                if (sb == null) sb = new StringBuilder(text);   // 第一次命中才开销
                sb[i] = simp;
            }
            return sb != null ? sb.ToString() : text;
        }

        /// <summary>命中了几个繁体字（诊断用，日志里可以提示提示词是不是该加强了）。</summary>
        public static int CountTraditional(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 0;
            foreach (char c in text) if (Map.ContainsKey(c)) n++;
            return n;
        }
    }
}
