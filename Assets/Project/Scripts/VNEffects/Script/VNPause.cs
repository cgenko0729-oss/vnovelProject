using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 全局玩法暂停（教程弹窗 / 将来的模块内菜单等一切「先冻住，玩家看完再继续」）。
    ///
    /// 【为什么不能用 Time.timeScale = 0】
    /// 事件模块三铁律②规定所有模块用 <c>Time.unscaledDeltaTime</c> 计时（为了不受
    /// Skip 快进的 DOTween.timeScale 影响），所以 timeScale 归零对羽毛球、问答、
    /// 拍照、互动一律无效——球照飞、倒计时照跑。真正能冻住它们的只有
    /// 「模块自己早退 + dt 归零」这一条路，于是有了这个类和 <see cref="VNTime"/>。
    ///
    /// 【为什么是句柄式而不是裸的 Push/Pop 计数】
    /// 释放路径不止一条：正常结束 / ESC 跳过 / CancelForDebug / 宿主被 Destroy /
    /// 场景切换。裸计数漏掉任何一条，症状是**游戏永久卡死**（比 VNTouchCursor
    /// 漏 Dispose 导致光标消失还严重）。句柄挂在一个 GameObject 上，宿主没了
    /// 句柄自动失效，<see cref="IsPaused"/> 每次读都会顺手清掉这类死句柄。
    ///
    /// 用法：
    ///   _pause = VNPause.Acquire(gameObject, "tutorial");   // 冻住
    ///   VNPause.Release(ref _pause);                        // 解冻（重复调用无害）
    /// </summary>
    public static class VNPause
    {
        /// <summary>一次暂停的持有凭证。宿主（owner）被销毁后自动失效。</summary>
        public class Handle
        {
            internal Object owner;      // 可为 null（无宿主的纯代码持有）
            internal bool ownerBound;   // 有没有指定宿主
            internal string reason;
            internal bool released;

            internal bool Alive => !released && (!ownerBound || owner != null);
        }

        static readonly List<Handle> _handles = new List<Handle>();

        /// <summary>暂停状态变化时触发（true = 刚进入暂停）。光标还原等联动挂这里。</summary>
        public static event System.Action<bool> Changed;

        /// <summary>当前是否处于暂停。读的时候顺手清掉宿主已销毁的死句柄。</summary>
        public static bool IsPaused
        {
            get
            {
                Prune();
                return _handles.Count > 0;
            }
        }

        /// <summary>正在按住暂停的理由（调试用，逗号分隔）</summary>
        public static string Reasons
        {
            get
            {
                Prune();
                if (_handles.Count == 0) return "";
                var names = new List<string>(_handles.Count);
                foreach (var h in _handles) names.Add(h.reason);
                return string.Join(",", names);
            }
        }

        /// <summary>
        /// 取得一个暂停句柄。owner 传持有方的 GameObject/Component：
        /// 它被销毁时句柄自动失效，这是「漏了 Release 也不会永久卡死」的最后一道保险。
        /// </summary>
        public static Handle Acquire(Object owner, string reason)
        {
            bool was = IsPaused;
            var handle = new Handle
            {
                owner = owner,
                ownerBound = owner != null,
                reason = string.IsNullOrEmpty(reason) ? "?" : reason,
            };
            _handles.Add(handle);
            if (!was) Changed?.Invoke(true);
            return handle;
        }

        /// <summary>释放句柄并把引用置空。传 null 或已释放的句柄都无害。</summary>
        public static void Release(ref Handle handle)
        {
            if (handle == null) return;
            handle.released = true;
            handle = null;
            bool was = _handles.Count > 0;
            Prune();
            if (was && _handles.Count == 0) Changed?.Invoke(false);
        }

        /// <summary>
        /// 全部解除。场景切换 / 剧本 Stop 的兜底——正常路径不该用到它，
        /// 用到了说明某处漏了 Release，但至少玩家不会卡死。
        /// </summary>
        public static void ReleaseAll()
        {
            if (_handles.Count == 0) return;
            foreach (var h in _handles) h.released = true;
            _handles.Clear();
            Changed?.Invoke(false);
        }

        static void Prune()
        {
            for (int i = _handles.Count - 1; i >= 0; i--)
                if (!_handles[i].Alive) _handles.RemoveAt(i);
        }

#if UNITY_EDITOR
        // 关闭域重载（Enter Play Mode Options）时静态字段不会自动清空，
        // 上一次 Play 遗留的句柄会让新的一次 Play 一开局就是暂停的。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _handles.Clear();
            Changed = null;
        }
#endif
    }

    /// <summary>
    /// 玩法模块统一的时间源：**受 <see cref="VNPause"/> 影响、不受 Skip 快进影响**。
    ///
    /// 模块里所有 <c>Time.unscaledDeltaTime</c> 换成 <see cref="Delta"/>、
    /// 所有 <c>Time.unscaledTime</c> 换成 <see cref="Time"/> 即可。
    /// 0.05 秒的单帧上限来自羽毛球模块的原注释：切窗口回来时的巨大 dt
    /// 会让球瞬移过整个球场；既然要统一，这个保护就一并收进来。
    ///
    /// 计时由一个隐藏驱动物体每帧推进，**与读取顺序无关**——
    /// 用「读的时候才累加」的懒惰实现会在没人读的帧上丢时间。
    /// </summary>
    public static class VNTime
    {
        /// <summary>单帧 dt 上限（秒）。切窗口回来的巨大 dt 会让弹道/倒计时跳一大截。</summary>
        public const float MaxStep = 0.05f;

        static float _time;

        /// <summary>暂停时为 0 的 dt。</summary>
        public static float Delta =>
            VNPause.IsPaused ? 0f : Mathf.Min(UnityEngine.Time.unscaledDeltaTime, MaxStep);

        /// <summary>暂停时不前进的累计时间轴（只用于「两次读数之差」，绝对值无意义）。</summary>
        public static float Time => _time;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            _time = 0f;
            var go = new GameObject("VNTimeDriver") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Driver>();
        }

        class Driver : MonoBehaviour
        {
            void Update() => _time += Delta;
        }
    }
}
