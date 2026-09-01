using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 教程高亮目标的注册表：id → RectTransform。
    ///
    /// 【为什么不用「按名字/路径找物体」】
    /// 小游戏的 UI 全是代码程序化生成的（VNBadmintonCourt / VNPhotoBoothUi /
    /// VNQuizModule…），层级和物体名随手就会改。用路径寻址的话，改一次布局
    /// 教程就全废，而且**没有任何报错**——只是洞挖到了空气上。
    /// 改成显式登记后，id 是一份稳定契约，布局怎么改都不影响。
    ///
    /// 【怎么用】
    /// 模块建完 UI 顺手登记一行，销毁时反注册：
    ///     VNTutorialAnchors.Register("badminton.scoreboard", scoreRect);
    ///     VNTutorialAnchors.Unregister("badminton.scoreboard", scoreRect);
    /// prefab 上的控件更省事：直接挂 <see cref="VNTutorialAnchor"/> 组件填 id。
    ///
    /// 取用时会顺手清掉已销毁的条目，所以模块忘了反注册也不会留下野指针
    /// （Unity 的伪 null 在这里是好事：== null 对已销毁对象成立）。
    /// </summary>
    public static class VNTutorialAnchors
    {
        static readonly Dictionary<string, RectTransform> _map =
            new Dictionary<string, RectTransform>();

        /// <summary>登记一个高亮目标（同 id 重复登记 = 后来者覆盖）</summary>
        public static void Register(string id, RectTransform rect)
        {
            if (string.IsNullOrEmpty(id) || rect == null) return;
            _map[id] = rect;
        }

        /// <summary>
        /// 反注册。传了 rect 时只在当前登记的就是它时才删——
        /// 否则两个实例先后登记同一个 id，先销毁的那个会把后来者的登记抹掉。
        /// </summary>
        public static void Unregister(string id, RectTransform rect = null)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (rect != null && (!_map.TryGetValue(id, out var cur) || cur != rect)) return;
            _map.Remove(id);
        }

        /// <summary>取高亮目标；没登记或已销毁返回 null</summary>
        public static RectTransform Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!_map.TryGetValue(id, out var rect)) return null;
            if (rect == null) { _map.Remove(id); return null; } // 已销毁：顺手清掉
            return rect;
        }

        /// <summary>当前登记的全部 id（调试 / 编辑器下拉用）</summary>
        public static IEnumerable<string> Ids
        {
            get
            {
                var dead = new List<string>();
                foreach (var kv in _map)
                    if (kv.Value == null) dead.Add(kv.Key);
                foreach (var id in dead) _map.Remove(id);
                return new List<string>(_map.Keys);
            }
        }

        public static void Clear() => _map.Clear();

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _map.Clear();
#endif
    }

    /// <summary>
    /// 挂在 prefab 里的 UI 控件上，自动把它登记成教程高亮目标。
    /// 程序化生成的 UI 直接调 <see cref="VNTutorialAnchors.Register"/> 更省事，
    /// 这个组件是给皮肤 prefab 里的按钮用的。
    /// </summary>
    public class VNTutorialAnchor : MonoBehaviour
    {
        [Header("教程资产里 anchor 字段填的名字（如 hud.stats / toolbar.save）")]
        public string id;

        RectTransform _rect;

        void OnEnable()
        {
            _rect = transform as RectTransform;
            VNTutorialAnchors.Register(id, _rect);
        }

        void OnDisable() => VNTutorialAnchors.Unregister(id, _rect);
    }
}
