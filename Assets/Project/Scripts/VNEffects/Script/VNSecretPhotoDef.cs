using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 秘密偷拍模式的全部参数（一份全局资产，登记进 VNGameConfig「玩法」页的 secretPhoto）。
    ///
    /// 【它解决什么】
    /// 解锁后右上角出现相机图标 → 点进去自由缩放/平移构图 → 快门把当前画面存进私密相册。
    /// 被拍的角色会「察觉」：镜头对着她越近、停得越久，察觉度涨得越快；满了就被发现，
    /// 扣好感 + 永久累积警惕（下次涨得更快）。胶卷是道具，一张一卷。
    ///
    /// 【为什么全部状态落在 flag 上】
    /// 解锁 / 胶卷 / 警惕度三样都是剧情状态，必须跟着存档回退（读旧档她不该记得未来）。
    /// 走 flag 就一个存档字段都不用加。相册本身是玩家收藏品、全局永久（同 CG 画廊）。
    ///
    /// 【数值档位】默认是「宽松」：中距离约 20 秒被发现、最近约 7 秒，扣 3 好感。
    /// 全在这里改，不用动代码。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Secret Photo Settings", fileName = "秘密相机")]
    public class VNSecretPhotoDef : ScriptableObject
    {
        [Header("—— 解锁与消耗 ——")]
        [Header("解锁 flag：> 0 时右上角出现图标（剧本 flag 秘密相机=1）")]
        public string unlockFlag = "秘密相机";

        [Header("胶卷道具 id（flag = 道具_<id>，与商店/背包同一套；一张照片一卷）")]
        public string filmItemId = "胶卷";

        [Header("没胶卷时能不能进模式看取景（false = 图标变灰、点了提示）")]
        public bool allowEnterWithoutFilm = false;

        [Header("—— 镜头 ——")]
        [Header("缩放范围（背景只有 1920×1080，超过 1.6x 明显糊）")]
        public float zoomMin = 1f;
        public float zoomMax = 1.6f;

        [Header("滚轮一格的缩放量")]
        public float zoomStep = 0.06f;

        [Header("拖动平移的灵敏度（画布像素 / 屏幕像素，1 = 跟手）")]
        public float panSensitivity = 1f;

        [Header("缩放模式：Depth = 立绘比背景多缩 50%（推近更像镜头而不是数码变焦）")]
        public VNCamZoomMode zoomMode = VNCamZoomMode.Depth;

        [Header("进模式时停掉背景 Ken Burns 漂移（它会跟玩家抢构图），退出还原")]
        public bool pauseKenBurns = true;

        [Header("—— 察觉 ——")]
        [Header("基础涨速：取景框正对着她、缩放 1.0x 时每秒涨多少（百分比）")]
        public float detectBaseRate = 5f;

        [Header("缩放到最大时的涨速倍率（1.0x → ×1，zoomMax → ×此值，线性）")]
        public float detectZoomFactorAtMax = 3f;

        [Header("她只擦到画面边缘时的最低权重（居中 = 1）")]
        [Range(0f, 1f)] public float detectEdgeWeight = 0.35f;

        [Header("警惕 flag 前缀（flag = <前缀><角色id>，百分比，永久累积）")]
        public string alertFlagPrefix = "偷拍_警惕_";

        [Header("每次被发现警惕 +N（%）；涨速 ×(1 + 警惕/100)")]
        public int alertGain = 10;

        [Header("—— 被发现 ——")]
        [Header("扣哪个属性、扣多少（走 VNStatsHud.Apply，带钳制与飘字）")]
        public string affectionStat = "好感";
        public int affectionPenalty = 3;

        [Header("被发现时的情绪动作")]
        public VNSecretPhotoEmote caughtEmote = VNSecretPhotoEmote.Surprise;

        [Header("被发现时的漫符")]
        public VNMarkKind caughtMark = VNMarkKind.Anger;

        [Header("被发现时她说的话（随机一句；留空用内置三句）")]
        public List<VNSecretPhotoLine> caughtLines = new List<VNSecretPhotoLine>();

        [Header("按角色覆盖情绪 / 台词")]
        public List<VNSecretPhotoCharacterOverride> characterOverrides =
            new List<VNSecretPhotoCharacterOverride>();

        [Header("—— 其他 ——")]
        [Header("首次进入自动播的教程 id（VNGameConfig 教程库；留空不播）")]
        public string tutorialId = "秘密相机";

        [Header("快门 / 被发现 音效（留空用代码合成的）")]
        public AudioClip shutterClip;
        public AudioClip caughtClip;

        // ------------------------------------------------------------------

        public string FilmFlag => VNShopDef.ItemFlagPrefix + (string.IsNullOrEmpty(filmItemId) ? "胶卷" : filmItemId);

        public string AlertFlagFor(string characterId) =>
            (string.IsNullOrEmpty(alertFlagPrefix) ? "偷拍_警惕_" : alertFlagPrefix) + characterId;

        public VNSecretPhotoCharacterOverride FindOverride(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            foreach (var o in characterOverrides)
                if (o != null && o.characterId == characterId) return o;
            return null;
        }

        /// <summary>被发现时的台词：角色覆盖 → 全局列表 → 内置三句（本地化表）</summary>
        public string PickCaughtLine(string characterId)
        {
            var o = FindOverride(characterId);
            if (o != null && o.caughtLines != null && o.caughtLines.Count > 0)
                return PickFrom(o.caughtLines);
            if (caughtLines != null && caughtLines.Count > 0)
                return PickFrom(caughtLines);
            int n = UnityEngine.Random.Range(1, 4);
            return VNLocale.T("secretphoto.caught." + n);
        }

        public VNSecretPhotoEmote CaughtEmoteFor(string characterId)
        {
            var o = FindOverride(characterId);
            return o != null && o.overrideEmote ? o.caughtEmote : caughtEmote;
        }

        static string PickFrom(List<VNSecretPhotoLine> lines)
        {
            // 跳过整条为空的（Inspector 里多按了一下 + 的那种）
            var valid = new List<VNSecretPhotoLine>();
            foreach (var l in lines)
                if (l != null && !string.IsNullOrEmpty(l.text)) valid.Add(l);
            if (valid.Count == 0) return VNLocale.T("secretphoto.caught.1");
            return valid[UnityEngine.Random.Range(0, valid.Count)].Resolve();
        }

        /// <summary>
        /// 没在 VNGameConfig 登记资产时的运行时默认值（HideFlags.DontSave，不会写进任何文件）。
        /// 这样「只解锁 flag 没建资产」也能玩，同 VNStatsHud 没登记定义也能工作的惯例。
        /// </summary>
        static VNSecretPhotoDef _fallback;
        public static VNSecretPhotoDef Resolve()
        {
            var cfg = VNGameConfig.Active;
            if (cfg != null && cfg.secretPhoto != null) return cfg.secretPhoto;
            if (_fallback == null)
            {
                _fallback = CreateInstance<VNSecretPhotoDef>();
                _fallback.name = "秘密相机(默认)";
                _fallback.hideFlags = HideFlags.DontSave;
            }
            return _fallback;
        }
    }

    public enum VNSecretPhotoEmote { Surprise, Angry, Shy, Dejected, None }

    [Serializable]
    public class VNSecretPhotoLine
    {
        [TextArea(1, 3)] public string text;
        [TextArea(1, 3)] public string textEn;
        [TextArea(1, 3)] public string textJa;

        public string Resolve() => VNTutorialDef.Pick(text, textEn, textJa);
    }

    [Serializable]
    public class VNSecretPhotoCharacterOverride
    {
        [Header("角色 id")]
        public string characterId;

        [Header("覆盖情绪动作")]
        public bool overrideEmote;
        public VNSecretPhotoEmote caughtEmote = VNSecretPhotoEmote.Shy;

        [Header("这个角色专属的被发现台词（留空用全局）")]
        public List<VNSecretPhotoLine> caughtLines = new List<VNSecretPhotoLine>();
    }
}
