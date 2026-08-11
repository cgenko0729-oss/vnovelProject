using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 程序化贴纸图形种类。资产没配 sprite 时按这个画（零美术依赖）。
    /// </summary>
    public enum VNPhotoStickerShape
    {
        [InspectorName("爱心")] Heart = 0,
        [InspectorName("星星")] Star = 1,
        [InspectorName("闪光")] Sparkle = 2,
        [InspectorName("蝴蝶结")] Ribbon = 3,
        [InspectorName("对话泡")] SpeechBubble = 4,
        [InspectorName("小花")] Flower = 5,
        [InspectorName("音符")] Note = 6,
        [InspectorName("皇冠")] Crown = 7,
        [InspectorName("猫耳")] CatEars = 8,
        [InspectorName("云朵")] Cloud = 9,
    }

    /// <summary>
    /// 贴纸定义资产：一条 = 一种可以贴到照片上的小图案。
    ///
    /// 参考实现里贴纸是边框图的附属小图（img_photobooth_{n}_{i}），
    /// 这里拆成独立资产，因为同一张贴纸要被多个边框、多个主题的评分表复用。
    ///
    /// 登记进 VNGameConfig 的「照片贴纸」区；边框的自带装饰与主题的加分项都按 stickerId 引用。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Photo Sticker", fileName = "NewPhotoSticker")]
    public class VNPhotoStickerDef : ScriptableObject
    {
        [Header("贴纸 id（可中文，如 爱心 / 星星；主题评分表按它引用）")]
        public string stickerId;

        [Header("列表里显示的名字（留空 = 用 stickerId）")]
        public string displayName;
        [Header("英文/日文（留空回退中文）")]
        public string displayNameEn;
        public string displayNameJa;

        [Header("贴纸图（留空 = 用下面的程序化图形）")]
        public Sprite sprite;

        [Header("【程序化用】图形种类")]
        public VNPhotoStickerShape shape = VNPhotoStickerShape.Heart;

        [Header("【程序化用】颜色（自定义 sprite 也会被乘上这个色，白色 = 原样）")]
        public Color tint = Color.white;

        [Header("贴到照片上的初始边长（像素，1920×1080 基准）")]
        [Range(24f, 400f)]
        public float defaultSize = 96f;

        public string DisplayName
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? displayNameEn
                    : VNLocale.Language == VNLanguage.Japanese ? displayNameJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(displayName) ? stickerId : displayName;
            }
        }

        /// <summary>实际使用的图：自定义优先，留空退回程序化生成</summary>
        public Sprite ResolveSprite() =>
            sprite != null ? sprite : VNPhotoTextures.StickerSprite(shape);
    }
}
