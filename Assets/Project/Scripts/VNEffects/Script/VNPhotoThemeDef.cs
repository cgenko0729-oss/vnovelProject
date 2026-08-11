using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>一句三语文本（评语用）。空文本 = 不显示。</summary>
    [System.Serializable]
    public class VNPhotoLine
    {
        [TextArea(1, 2)] public string text;
        [Header("英文/日文（留空回退中文）")]
        [TextArea(1, 2)] public string textEn;
        [TextArea(1, 2)] public string textJa;

        public bool Empty => string.IsNullOrEmpty(text) &&
                             string.IsNullOrEmpty(textEn) && string.IsNullOrEmpty(textJa);

        public string Display
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? textEn
                    : VNLocale.Language == VNLanguage.Japanese ? textJa : null;
                return string.IsNullOrEmpty(localized) ? text : localized;
            }
        }
    }

    /// <summary>表情加分项针对谁</summary>
    public enum VNPhotoSlot
    {
        [InspectorName("任意一方")] Any = 0,
        [InspectorName("我")] Me = 1,
        [InspectorName("对方")] Her = 2,
    }

    /// <summary>
    /// 拍照主题资产：主题名 + 这个主题下的加分清单 + 分档评语。
    ///
    /// 采用「清单制」——每个主题直接列出它想要什么，一眼看懂、加新主题只需新建一个资产：
    ///   甜蜜：表情 害羞+20 / 微笑+15，边框 粉格子+20，贴纸 爱心+10（最多计 3 个）
    ///
    /// 得分 = 基础分 + 命中项之和，再按 perfectLine / passLine 分成 完美 / 普通 / 失败 三档。
    /// 计算逻辑在 VNPhotoScore（纯静态，可单测），本资产只存数据。
    ///
    /// 登记进 VNGameConfig 的「照片主题」区。剧本 theme: 按 themeId 引用；不写 theme: 即自由拍照。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Photo Theme", fileName = "NewPhotoTheme")]
    public class VNPhotoThemeDef : ScriptableObject
    {
        /// <summary>表情加分项</summary>
        [System.Serializable]
        public class ExpressionRule
        {
            [Header("针对谁的表情")]
            public VNPhotoSlot slot = VNPhotoSlot.Any;
            [Header("表情名（VNCharacterDef.expressions 里的 name）")]
            public string expression;
            [Header("分数（可为负，用来惩罚跑题的表情）")]
            public int score = 10;
            [Header("命中时的细评（留空 = 不单独讲评）")]
            public VNPhotoLine comment = new VNPhotoLine();
        }

        /// <summary>边框加分项</summary>
        [System.Serializable]
        public class FrameRule
        {
            [Header("边框 id（VNPhotoFrameDef.frameId）")]
            public string frameId;
            public int score = 10;
            public VNPhotoLine comment = new VNPhotoLine();
        }

        /// <summary>贴纸加分项</summary>
        [System.Serializable]
        public class StickerRule
        {
            [Header("贴纸 id（VNPhotoStickerDef.stickerId）")]
            public string stickerId;
            [Header("每贴一个的分数")]
            public int score = 5;
            [Header("最多计几个（防止满屏刷同一种贴纸刷分）")]
            [Min(1)] public int maxCount = 3;
            public VNPhotoLine comment = new VNPhotoLine();
        }

        [Header("主题 id（可中文，如 甜蜜 / 搞怪；剧本 theme: 按它引用）")]
        public string themeId;

        [Header("面板上显示的主题名（留空 = 用 themeId）")]
        public string displayName;
        public string displayNameEn;
        public string displayNameJa;

        [Header("主题提示语（面板顶部那行「拍一张甜甜的合照吧」）")]
        public VNPhotoLine hint = new VNPhotoLine();

        [Header("──────── 赛制 ────────")]
        [Header("装扮限时（秒）。0 = 不限时；剧本 time: 可覆盖")]
        [Min(0f)] public float timeLimit = 60f;

        [Header("基础分（什么都不选也有的底分）")]
        public int baseScore = 20;

        [Header("完美线：总分 ≥ 这个值 = 完美")]
        public int perfectLine = 70;

        [Header("及格线：总分 ≥ 这个值 = 普通，低于则失败")]
        public int passLine = 40;

        [Header("贴纸总数上限（超出的贴纸一律不计分，防刷分）")]
        [Min(0)] public int stickerScoreCap = 6;

        [Header("──────── 加分清单 ────────")]
        public List<ExpressionRule> expressionRules = new List<ExpressionRule>();
        public List<FrameRule> frameRules = new List<FrameRule>();
        public List<StickerRule> stickerRules = new List<StickerRule>();

        [Header("──────── 分档总评 ────────")]
        public VNPhotoLine perfectComment = new VNPhotoLine();
        public VNPhotoLine normalComment = new VNPhotoLine();
        public VNPhotoLine failComment = new VNPhotoLine();

        public string DisplayName
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? displayNameEn
                    : VNLocale.Language == VNLanguage.Japanese ? displayNameJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(displayName) ? themeId : displayName;
            }
        }

        /// <summary>档位对应的总评（2 完美 / 1 普通 / 0 失败）</summary>
        public VNPhotoLine CommentForGrade(int grade) =>
            grade >= 2 ? perfectComment : grade == 1 ? normalComment : failComment;
    }
}
