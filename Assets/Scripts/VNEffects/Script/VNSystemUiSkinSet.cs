using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 全局系统 UI 主题：每类系统界面只使用一个 prefab。
    /// 任一槽留空或 prefab 校验失败时，对应控制器退回原来的程序化 UI。
    /// 对话框/选项皮肤仍由 VNGameConfig 的独立皮肤库和 ui 命令管理。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/System UI Skin Set", fileName = "VNSystemUiSkinSet")]
    public class VNSystemUiSkinSet : ScriptableObject
    {
        [Header("整页 / 弹窗")]
        public GameObject titleMenuPrefab;
        public GameObject configPanelPrefab;
        public GameObject cgGalleryPrefab;
        public GameObject backlogPrefab;
        public GameObject saveLoadPrefab;

        [Header("常驻 HUD")]
        public GameObject quickToolbarPrefab;
        public GameObject statsHudPrefab;

        [Header("状态完整页")]
        public GameObject statsPanelPrefab;

        [Header("背包（道具 + 装备栏）")]
        public GameObject inventoryPrefab;

        [Header("玩法事件面板（周日程排程 / 结果结算弹窗）")]
        public GameObject planPrefab;
        public GameObject resultPopupPrefab;
    }
}
