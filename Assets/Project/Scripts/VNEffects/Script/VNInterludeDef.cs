using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>过场层的进出方式</summary>
    public enum VNInterludeEnter
    {
        /// <summary>过场层自己淡入淡出（默认，最不容易和别的演出打架）</summary>
        Fade,
        /// <summary>复用 VNScreenTransition 的全屏转场（噪声溶解 / 水墨 / 眨眼 …）</summary>
        Transition,
    }

    /// <summary>
    /// 一次「过场」（章节标题卡）的全部内容与参数。
    ///
    /// 【它解决什么】
    /// 切章节 / 切场景时想插一屏：一张转场图铺满 + 章节标题 + 转 1.5 秒的 loading 图标
    /// + 一句与这个标题相关的语音。剧本里只写 `interlude 第二章`，其余全在这份资产里配。
    ///
    /// 【为什么做成资产而不是命令参数】
    /// 标题要三语（进不了 .vn.txt，剧本只写中文 id）；语音是一「池」不是一条；
    /// 同一个过场往往在多处复用（读档回来、番外线绕回主线）。写在行里三处都要改。
    ///
    /// 【怎么用】
    ///   1. 右键 Create → VN → Interlude Def 建资产，填 id（剧本引用名，可中文）
    ///   2. 登记进 VNGameConfig 的「过场库」
    ///   3. 剧本：interlude 第二章  /  interlude 第二章 time:3 cg:某张图
    ///
    /// 【转场图从哪来】
    /// images 留空 = 从 VNGameConfig.interludeImages 这个**全局池**里随机抽一张。
    /// 这是常态：转场图是一批通用氛围图，不是某一章专属的。
    /// 只有想让某个过场固定用某几张时才填这里的 images。
    /// 转场图**不进 CG 鉴赏画廊**——它是演出素材，不是收集品。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Interlude Def", fileName = "VNInterludeDef")]
    public class VNInterludeDef : ScriptableObject
    {
        [Header("剧本 interlude 命令引用的 id（可中文，如 第二章 / 夏日祭）")]
        public string id;

        // ------------------------------------------------------------------
        // 文字（三语；En/Ja 留空回退中文，与 VNGameConfig.gameTitle 同套语义）
        // ------------------------------------------------------------------

        [Header("标题（大字，居中）")]
        public string title;
        public string titleEn;
        public string titleJa;

        [Header("副标题（小字，排在标题下方；留空则不显示）")]
        public string subtitle;
        public string subtitleEn;
        public string subtitleJa;

        // ------------------------------------------------------------------
        // 语音 / 图
        // ------------------------------------------------------------------

        [Header("语音池：与这个标题相关的语音 id（须在 Voice 库登记）\n" +
                "每次随机播一条；留空 = 不放语音")]
        public List<string> voices = new List<string>();

        [Header("专属转场图池（留空 = 从 VNGameConfig 的全局转场图池随机抽）")]
        public List<Sprite> images = new List<Sprite>();

        // ------------------------------------------------------------------
        // 节奏
        // ------------------------------------------------------------------

        [Header("loading 图标转多久（秒）。转完自动继续，玩家点击不能提前跳过")]
        public float loadingDuration = 1.5f;

        [Header("进出方式：淡入淡出 / 复用全屏转场")]
        public VNInterludeEnter enter = VNInterludeEnter.Fade;

        [Header("enter = Transition 时用哪一种全屏转场")]
        public VNTransition transition = VNTransition.NoiseDissolve;

        [Header("淡入 / 淡出时长（enter = Fade 时才用）")]
        public float fadeIn = 0.45f;
        public float fadeOut = 0.45f;

        // ------------------------------------------------------------------
        // 外观
        // ------------------------------------------------------------------

        [Header("转场图上压的暗幕浓度（0 = 不压；图亮的时候文字会看不清）")]
        [Range(0f, 1f)] public float dimStrength = 0.45f;

        [Header("没有任何转场图可用时的纯色底")]
        public Color fallbackColor = new Color(0.05f, 0.05f, 0.08f, 1f);

        [Header("标题字号 / 颜色")]
        public float titleFontSize = 96f;
        public Color titleColor = Color.white;

        [Header("副标题字号 / 颜色")]
        public float subtitleFontSize = 34f;
        public Color subtitleColor = new Color(0.85f, 0.86f, 0.92f, 1f);

        [Header("loading 图标颜色与直径（像素，1920×1080 基准）")]
        public Color loadingColor = new Color(1f, 0.93f, 0.72f, 1f);
        public float loadingSize = 56f;

        // ------------------------------------------------------------------
        // 取值（三语回退 + 随机抽取）
        // ------------------------------------------------------------------

        /// <summary>当前语言的标题（En/Ja 留空回退中文）</summary>
        public string ResolveTitle() => Pick(title, titleEn, titleJa);

        /// <summary>当前语言的副标题</summary>
        public string ResolveSubtitle() => Pick(subtitle, subtitleEn, subtitleJa);

        static string Pick(string zh, string en, string ja)
        {
            switch (VNLocale.Language)
            {
                case VNLanguage.English: return string.IsNullOrEmpty(en) ? zh : en;
                case VNLanguage.Japanese: return string.IsNullOrEmpty(ja) ? zh : ja;
                default: return zh;
            }
        }

        /// <summary>随机取一条语音 id；池为空返回 null</summary>
        public string PickVoice() => PickRandom(voices);

        /// <summary>
        /// 随机取一张转场图：自己的池优先，为空则退到全局池。
        /// 两个池都空 → null（此时过场层用 fallbackColor 纯色底）。
        /// </summary>
        public Sprite PickImage()
        {
            var own = PickRandom(images);
            if (own != null) return own;
            var cfg = VNGameConfig.Active;
            return cfg != null ? PickRandom(cfg.interludeImages) : null;
        }

        static T PickRandom<T>(List<T> list) where T : class
        {
            if (list == null || list.Count == 0) return null;
            // 先剔掉空槽再抽，否则 Inspector 里留的空行会抽出"什么都没有"
            int valid = 0;
            for (int i = 0; i < list.Count; i++)
                if (!IsEmpty(list[i])) valid++;
            if (valid == 0) return null;
            int pick = Random.Range(0, valid);
            for (int i = 0; i < list.Count; i++)
            {
                if (IsEmpty(list[i])) continue;
                if (pick-- == 0) return list[i];
            }
            return null;
        }

        static bool IsEmpty<T>(T item) where T : class
        {
            if (item == null) return true;
            if (item is string s) return string.IsNullOrWhiteSpace(s);
            if (item is Object o) return o == null; // Unity 的伪 null
            return false;
        }
    }
}
