using System;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 「素材库改了」的编辑器内广播。
    ///
    /// 【解决什么】
    /// 素材浏览器 / VNGameConfig Inspector 登记了新素材之后，
    /// 剧本编辑器的 bg / cg / bgm 下拉是**缓存**的（`VNScenarioEditorWindow.RefreshSources()`
    /// 一次性构建 `_ctx.backgroundIds` 等数组），不重建就搜不到新素材。
    /// 让写方发一个信号、读方自己重建，比让写方去认识读方干净。
    ///
    /// 【订阅方必须退订】
    /// 这是 static 事件，订阅者是 EditorWindow 实例。窗口关闭 / 域重载会重建窗口，
    /// 不在 `OnDisable` 退订的话，事件会一直攥着已销毁窗口的引用，
    /// 下次广播时对着"假 null"的窗口调方法 —— 典型的编辑器内存泄漏 + 幽灵异常。
    /// 一律 `OnEnable` 订阅、`OnDisable` 退订。
    /// </summary>
    public static class VNAssetLibraryEvents
    {
        /// <summary>素材库（背景 / CG / 音频 / 角色…）发生了增删改。</summary>
        public static event Action Changed;

        /// <summary>写方在改完并 ApplyModifiedProperties 之后调用。</summary>
        public static void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}
