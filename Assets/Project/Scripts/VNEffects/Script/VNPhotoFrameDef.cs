using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>程序化边框样式。资产没配 frameSprite 时按这个画。</summary>
    public enum VNPhotoFrameStyle
    {
        [InspectorName("粉格子爱心")] PinkCheck = 0,
        [InspectorName("星空")] StarrySky = 1,
        [InspectorName("胶片")] Film = 2,
        [InspectorName("简约白框")] SimpleWhite = 3,
        [InspectorName("樱花")] Sakura = 4,
    }

    /// <summary>人物区的裁切形状（参考实现是椭圆开窗）。</summary>
    public enum VNPhotoMaskShape
    {
        [InspectorName("不裁切（整块矩形）")] None = 0,
        [InspectorName("椭圆")] Ellipse = 1,
        [InspectorName("圆角矩形")] RoundedRect = 2,
    }

    /// <summary>
    /// 边框样式资产：一条 = 左栏「边框样式」列表里的一项。
    ///
    /// 对应参考实现的 LovePhotoboothCfg（id / name / itemPos 装饰件位置表），
    /// 这里多出：程序化兜底、人物裁切形状、前景层、水印文字。
    ///
    /// 三层结构（照片从后往前）：
    ///   frameSprite  底图（格子花纹 / 星空 …）
    ///   人物层       被 maskShape 裁出的开窗，两个立绘在这里
    ///   frontSprite  前景装饰（压在人物上的文字条、边缘光等，可留空）
    ///   decorations  自带装饰件（玩家可继续拖动，locked 的除外）
    ///
    /// 登记进 VNGameConfig 的「照片边框」区。剧本 frame: 按 frameId 引用。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Photo Frame", fileName = "NewPhotoFrame")]
    public class VNPhotoFrameDef : ScriptableObject
    {
        /// <summary>边框自带的一件装饰（对应参考实现 itemPos 的一行 [图号, x, y]）</summary>
        [System.Serializable]
        public class Decoration
        {
            [Header("贴纸资产")]
            public VNPhotoStickerDef sticker;
            [Header("初始位置（相对取景框中心的像素偏移）")]
            public Vector2 position;
            [Header("初始缩放倍率")]
            [Range(0.2f, 4f)] public float scale = 1f;
            [Header("初始旋转（度）")]
            public float rotation;
            [Header("锁定：玩家不能拖动/删除它（边框自身的一部分）")]
            public bool locked;
        }

        [Header("边框 id（可中文，如 粉格子 / 星空；剧本 frame: 与主题评分表按它引用）")]
        public string frameId;

        [Header("列表里显示的名字（留空 = 用 frameId）")]
        public string displayName;
        [Header("英文/日文（留空回退中文）")]
        public string displayNameEn;
        public string displayNameJa;

        [Header("──────── 图层 ────────")]
        [Header("边框底图（留空 = 用下面的程序化样式生成）")]
        public Sprite frameSprite;

        [Header("【程序化用】样式")]
        public VNPhotoFrameStyle style = VNPhotoFrameStyle.PinkCheck;

        [Header("【程序化用】主色调")]
        public Color mainColor = new Color(1f, 0.62f, 0.78f, 1f);

        [Header("前景装饰图（压在人物上方，可留空）")]
        public Sprite frontSprite;

        [Header("──────── 人物开窗 ────────")]
        [Header("人物区裁切形状")]
        public VNPhotoMaskShape maskShape = VNPhotoMaskShape.Ellipse;

        [Header("开窗大小占取景框的比例（x 宽 / y 高）")]
        public Vector2 maskSize = new Vector2(0.72f, 0.82f);

        [Header("开窗中心相对取景框中心的偏移比例")]
        public Vector2 maskOffset = Vector2.zero;

        [Header("开窗背景色（人物身后那层底色，A=0 则不画）")]
        public Color windowColor = new Color(0.87f, 0.94f, 1f, 1f);

        [Header("开窗描边色（A=0 则不画描边）")]
        public Color maskEdgeColor = new Color(0.95f, 0.4f, 0.62f, 1f);

        [Header("开窗描边粗细（像素）")]
        [Range(0f, 24f)] public float maskEdgeWidth = 8f;

        [Header("──────── 文字与装饰 ────────")]
        [Header("照片下缘的水印文字（留空 = 不画。这是照片的一部分，会被拍进去）")]
        public string watermark;
        public string watermarkEn;
        public string watermarkJa;

        [Header("水印颜色")]
        public Color watermarkColor = new Color(1f, 1f, 1f, 0.92f);

        [Header("边框自带的装饰件（玩家可继续拖动，locked 的除外）")]
        public List<Decoration> decorations = new List<Decoration>();

        public string DisplayName
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? displayNameEn
                    : VNLocale.Language == VNLanguage.Japanese ? displayNameJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(displayName) ? frameId : displayName;
            }
        }

        public string DisplayWatermark
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? watermarkEn
                    : VNLocale.Language == VNLanguage.Japanese ? watermarkJa : null;
                return string.IsNullOrEmpty(localized) ? watermark : localized;
            }
        }

        /// <summary>实际使用的边框底图：自定义优先，留空退回程序化生成</summary>
        public Sprite ResolveFrameSprite() =>
            frameSprite != null ? frameSprite : VNPhotoTextures.FrameSprite(style, mainColor);
    }
}
