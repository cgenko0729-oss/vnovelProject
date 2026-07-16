using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>event 命令传给模块的上下文（剧本参数 + 舞台引用）</summary>
    public class VNEventContext
    {
        public string eventId;                       // 剧本里引用的模块 id
        public VNStage stage;                        // 舞台（约定：模块只读，不直接改演出）
        public Dictionary<string, string> kwargs;    // 剧本行的 key:value 参数
        public int line;                             // 源文件行号（报错定位用）

        public string Kw(string key, string def = null) =>
            kwargs != null && kwargs.TryGetValue(key, out var v) ? v : def;

        public float KwF(string key, float def)
        {
            if (kwargs != null && kwargs.TryGetValue(key, out var v) &&
                float.TryParse(v, out float f)) return f;
            return def;
        }

        public int KwI(string key, int def)
        {
            if (kwargs != null && kwargs.TryGetValue(key, out var v) &&
                int.TryParse(v, out int i)) return i;
            return def;
        }
    }

    /// <summary>
    /// 玩法事件模块基类：地图 / 战斗 / 迷你游戏等一切「暂停剧本 → 玩家交互 →
    /// 带结果返回」的玩法都继承它。
    ///
    /// 生命周期：Runner 从 VNEventRegistry 实例化到事件层 → Launch →
    /// 模块自行交互 → 子类调 Done(结果名) → Runner 销毁模块并按结果分支
    /// （结果名匹配 event 命令下的「* 结果行」；整数结果同时写入 flag「事件结果」）。
    ///
    /// 约定：
    ///   - 模块只操作自己的 UI 子树与 VNFlags，不直接改舞台演出（背景/立绘交给事件前后的剧本行）
    ///   - 计时用 unscaledTime、Tween 用 SetUpdate(true)，不受快进 DOTween.timeScale 影响
    ///   - 所有 Tween SetLink(gameObject) 防泄漏（模块随时可能被销毁）
    /// </summary>
    public abstract class VNEventModule : MonoBehaviour
    {
        Action<string> _onDone;
        bool _finished;

        /// <summary>Runner 调用：初始化并开始交互。onDone 只会被回调一次。</summary>
        public void Launch(VNEventContext ctx, Action<string> onDone)
        {
            _onDone = onDone;
            _finished = false;
            OnLaunch(ctx);
        }

        /// <summary>子类实现：搭建 UI 并开始交互</summary>
        protected abstract void OnLaunch(VNEventContext ctx);

        /// <summary>子类交互结束时调用一次；重复调用被忽略</summary>
        protected void Done(string outcome)
        {
            if (_finished) return;
            _finished = true;
            _onDone?.Invoke(outcome ?? "");
        }

        /// <summary>剧本被停止/调试中断时的清理钩子（随后模块被销毁）</summary>
        public virtual void CancelForDebug() { }
    }
}
