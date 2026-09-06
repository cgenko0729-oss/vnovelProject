namespace VNEffects
{
    /// <summary>
    /// 主界面（非小游戏）UI 的教程锚点 id 一览。
    ///
    /// 小游戏模块各自把 <c>AnchorXxx</c> 常量放在模块类上（VNBadmintonModule /
    /// VNSecretPhotoUi），主界面的控件分散在十几个类里，集中放一处才好查：
    /// 教程编辑器的锚点目录靠反射扫所有 <c>public const string Anchor*</c>，
    /// 这里的常量与模块上的一视同仁。
    ///
    /// 带参数的 id（每个按钮 / 每条属性 / 每个装备格）用下面的格式化方法生成，
    /// 运行时登记与编辑器目录必须走同一个方法，否则两边拼出来的字符串对不上、
    /// 洞就挖到空气上。
    ///
    /// 【覆盖层的坑】
    /// 属性 HUD / 日历 / 背包 / 任务面板都是 Screen Space - Overlay 画布
    /// （排序 578~600），比挂在主 Canvas 下的教程层（92）高，正常挖不到。
    /// VNTutorialPlayer 在一步的目标落在 Overlay 画布上时会把自己整层临时搬到
    /// 一块更高的 Overlay 画布（<see cref="VNTutorialPlayer.overlaySortingOrder"/>），
    /// 代价是那一步洞口描边吃不到 Bloom。
    /// </summary>
    public static class VNUiAnchors
    {
        public const string AnchorDialogue = "ui.dialogue";          // 对话框面板
        public const string AnchorNameplate = "ui.dialogue.name";    // 名牌
        public const string AnchorToolbar = "ui.toolbar";            // 快捷功能条整体
        public const string AnchorStats = "ui.stats";                // 顶部属性 HUD 整条
        public const string AnchorCalendar = "ui.calendar";          // 右下日历
        public const string AnchorInventory = "ui.inventory";        // 背包面板
        public const string AnchorInventoryItems = "ui.inventory.items";   // 背包左侧道具列表
        public const string AnchorInventoryDetail = "ui.inventory.detail"; // 背包介绍区
        public const string AnchorQuest = "ui.quest";                // 任务日志列表
        public const string AnchorChoice = "ui.choice";              // 选项面板

        /// <summary>快捷条单个按钮：ui.toolbar.save / ui.toolbar.auto …</summary>
        public static string Toolbar(VNToolbarAction action) =>
            "ui.toolbar." + action.ToString().ToLowerInvariant();

        /// <summary>顶部 HUD 的单条属性：ui.stats.&lt;属性id&gt;</summary>
        public static string Stat(string statId) => "ui.stats." + statId;

        /// <summary>背包装备格：ui.inventory.slot.head / …face / …special</summary>
        public static string InventorySlot(VNEquipSlot slot) =>
            "ui.inventory.slot." + slot.ToString().ToLowerInvariant();
    }
}
