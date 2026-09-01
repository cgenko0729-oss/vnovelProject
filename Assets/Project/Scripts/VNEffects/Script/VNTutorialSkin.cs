using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 教程说明卡片的皮肤槽位声明（挂 prefab 根；登记在
    /// <see cref="VNSystemUiSkinSet.tutorialPrefab"/>）。
    ///
    /// 只有 panelRoot 与 bodyText 必需，其余全可选降级：缺 titleText 就不显示标题、
    /// 缺 imageRoot 就不显示配图、缺 progressText 就没有「2 / 5」。
    /// 整个槽位没配或校验失败时，<see cref="VNTutorialPlayer"/> 退回程序化卡片。
    ///
    /// 暗幕不在皮肤范围内 —— 它是 shader 挖洞的功能件，不是装饰。
    /// </summary>
    public class VNTutorialSkin : VNSystemUiSkinBehaviour
    {
        // 注意：panelRoot 的锚点/pivot 会被播放器强制改成居中，
        // 位置由步骤的 card 字段（上/中/下/自动避让洞口）驱动。
        [Header("卡片根（弹入动画与定位的目标；必须是 RectTransform）")]
        public RectTransform panelRoot;

        [Header("正文（必需）")]
        public TMP_Text bodyText;

        [Header("可选")]
        public TMP_Text titleText;          // 留空 = 不显示标题行
        public GameObject imageRoot;        // 配图容器（有图时才 SetActive）
        public Image image;                 // 配图本体
        public TMP_Text progressText;       // 「2 / 5」
        public TMP_Text hintText;           // 「▼ 点击继续」
        public TMP_Text skipHintText;       // 「ESC 跳过教程」
        public Button nextButton;           // 留空也能用：点屏幕任意处即可继续

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(panelRoot, "卡片根", errors);
            Require(bodyText, "正文", errors);
        }
    }
}
