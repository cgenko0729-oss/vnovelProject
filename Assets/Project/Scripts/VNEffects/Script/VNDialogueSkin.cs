using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 对话框皮肤：挂在皮肤 prefab 根上的"槽位声明"组件。
    /// VNDialogueBox 实例化 prefab 后按这里的引用绑定行为
    /// （打字机 / 名牌显隐 / 头像裁切 / 继续箭头呼吸 / 出入场动画）。
    ///
    /// 【授权自由】prefab 里可以随意添加装饰节点（渐变底图/角花/多层描边……），
    /// 代码只认下面登记的槽位，其余一概不管。
    /// 【全部槽位可选】留空 = 该功能在此皮肤下优雅降级
    /// （如 portraitWindow 留空 = 此皮肤不显示对话头像）。
    /// 【坐标系】皮肤实例会被拉伸铺满整个画布（1920×1080 参考分辨率），
    /// panel 想放屏幕哪儿就锚在哪儿——对话框放顶部就是把 panel 锚到上边。
    ///
    /// 制作起点：Tools → VN Effects → UI Skins → Export Default Skin Prefabs
    /// 会生成一套与程序化默认样式一模一样的 prefab，复制修改即可。
    /// 皮肤在 VNGameConfig 的 dialogueSkins 列表登记 id 后，
    /// 剧本用 `ui dialogue <id>` 切换（`ui dialogue default` 回默认样式）。
    /// </summary>
    public class VNDialogueSkin : MonoBehaviour
    {
        [Header("面板根：出场/退场动画作用于它（留空 = 动画整个皮肤层）")]
        public RectTransform panel;

        [Header("名牌根节点：旁白（无说话人）时整体隐藏")]
        public GameObject nameTag;
        [Header("名字文字")]
        public TMP_Text nameText;

        [Header("正文文字：运行时自动挂 VNTypewriterText 打字机")]
        public TMP_Text bodyText;

        [Header("继续箭头（任意 Graphic）：台词读完后呼吸浮动")]
        public Graphic arrow;

        [Header("头像窗口（建议挂 RectMask2D 裁切）；留空 = 此皮肤不显示头像")]
        public RectTransform portraitWindow;
        [Header("头像图（portraitWindow 的子物体）")]
        public Image portraitImage;
        [Header("头像显示时正文的额外左缩进（0 = 不避让，正文位置固定）")]
        public float portraitBodyInset;
        [Header("头像显示时名牌的额外右移（0 = 不避让）")]
        public float portraitTagShift;

        [Header("流光边框：运行时挂 VNImageEffectController 扫光循环；留空 = 无流光")]
        public Image shineFrame;

        [Header("快捷功能条停靠点（功能条挂到它右上角；留空 = 停靠 panel）")]
        public RectTransform toolbarAnchor;
    }
}
