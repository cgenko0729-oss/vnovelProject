using TMPro;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 选项面板皮肤：挂在皮肤 prefab 根上的"槽位声明"组件。
    /// VNChoicePanel 按 buttonTemplate 克隆出每个选项按钮，
    /// 飞入 / 悬停扫光 / 选中闪光 / 落选溶解等演出照常作用于克隆体。
    ///
    /// 【排版规则】
    ///   - container 挂了 LayoutGroup（如 VerticalLayoutGroup）→ 排版交给它，
    ///     入场动画改用淡入+缩放（位置归 Layout 管，不做位移飞入）。
    ///   - 没挂 Layout → 克隆体以 buttonTemplate 的锚点位置为首项，
    ///     依次向下堆叠（间距 = spacing）。想把选项挪到屏幕右侧，
    ///     把 template 锚到右边即可。
    /// 【模板要求】buttonTemplate 在 prefab 里保持禁用状态（不勾选 Active），
    ///   根上要有 Image 当按钮底图；文字/花费小字槽在下面登记。
    ///
    /// 制作起点与注册方式同 VNDialogueSkin（ui choice &lt;id&gt; 切换）。
    /// </summary>
    public class VNChoiceSkin : MonoBehaviour
    {
        [Header("选项容器：按钮克隆到它下面（挂 LayoutGroup 则由其排版）")]
        public RectTransform container;

        [Header("按钮模板（禁用状态子物体，根上须有 Image 底图）")]
        public RectTransform buttonTemplate;

        [Header("模板内的选项文字槽")]
        public TMP_Text buttonLabel;

        [Header("模板内的花费小字槽（可选，choice cost: 显示用）")]
        public TMP_Text buttonCost;

        [Header("无 LayoutGroup 时的纵向堆叠间距（像素）")]
        public float spacing = 26f;
    }
}
