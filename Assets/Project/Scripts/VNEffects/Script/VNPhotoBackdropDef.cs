using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 程序化背景样式。资产没配 sprite 时按这个画（零美术依赖）。
    /// RadialBurst 是大头贴机最经典的那种放射线底，优先级最高。
    /// </summary>
    public enum VNPhotoBackdropStyle
    {
        [InspectorName("纯色")] SolidColor = 0,
        [InspectorName("竖向渐变")] VerticalGradient = 1,
        [InspectorName("放射线")] RadialBurst = 2,
        [InspectorName("斜条纹")] Stripes = 3,
        [InspectorName("波点")] Dots = 4,
        [InspectorName("星夜")] StarryNight = 5,
        [InspectorName("彩虹")] Rainbow = 6,
        [InspectorName("光斑")] Bokeh = 7,
    }

    /// <summary>
    /// 大头贴的背景资产：一条 = 左栏「背景」列表里的一项，画在两个人身后、
    /// 被边框的开窗形状裁切。
    ///
    /// 与剧情背景（VNStage.backgrounds）分开是刻意的——剧情背景是 16:9 的场景大图，
    /// 塞进 4:3 开窗要裁掉大半；大头贴要的是放射线、波点这种「照相馆背景布」。
    ///
    /// 登记进 VNGameConfig 的「照片背景」区。剧本 `bg:` 按 backdropId 引用，
    /// 主题评分表也按它加分。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Photo Backdrop", fileName = "NewPhotoBackdrop")]
    public class VNPhotoBackdropDef : ScriptableObject
    {
        [Header("背景 id（可中文，如 放射线 / 星夜；剧本 bg: 与主题评分表按它引用）")]
        public string backdropId;

        [Header("列表里显示的名字（留空 = 用 backdropId）")]
        public string displayName;
        [Header("英文/日文（留空回退中文）")]
        public string displayNameEn;
        public string displayNameJa;

        [Header("背景图（留空 = 用下面的程序化样式生成）")]
        public Sprite sprite;

        [Header("【程序化用】样式")]
        public VNPhotoBackdropStyle style = VNPhotoBackdropStyle.RadialBurst;

        [Header("【程序化用】主色")]
        public Color mainColor = new Color(1f, 0.72f, 0.82f, 1f);

        [Header("【程序化用】副色（渐变的另一端 / 放射线与条纹的间隔色）")]
        public Color secondColor = new Color(1f, 0.95f, 0.97f, 1f);

        public string DisplayName
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? displayNameEn
                    : VNLocale.Language == VNLanguage.Japanese ? displayNameJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(displayName) ? backdropId : displayName;
            }
        }

        /// <summary>实际使用的图：自定义优先，留空退回程序化生成</summary>
        public Sprite ResolveSprite() => sprite != null
            ? sprite : VNPhotoTextures.BackdropSprite(style, mainColor, secondColor);
    }
}
