using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>所有系统 UI 槽位组件的共同验证入口。</summary>
    public abstract class VNSystemUiSkinBehaviour : MonoBehaviour
    {
        /// <summary>把缺失的必需槽位名称加入 errors；可选装饰槽位不应报错。</summary>
        public abstract void CollectValidationErrors(List<string> errors);

        public bool IsValid(out string error)
        {
            var errors = new List<string>();
            CollectValidationErrors(errors);
            error = string.Join("、", errors);
            return errors.Count == 0;
        }

        protected static void Require(Object value, string displayName, List<string> errors)
        {
            if (value == null) errors.Add(displayName);
        }
    }

    /// <summary>系统 UI prefab 的统一安全实例化。失败时返回 null，让调用方走默认构建。</summary>
    public static class VNSystemUiSkinUtility
    {
        public static T Instantiate<T>(GameObject prefab, Transform parent, string owner)
            where T : VNSystemUiSkinBehaviour
        {
            if (prefab == null) return null;

            var source = prefab.GetComponent<T>();
            if (source == null)
            {
                Debug.LogError($"[{owner}] 系统 UI prefab {prefab.name} 缺少 {typeof(T).Name}，已退回默认 UI");
                return null;
            }
            if (!source.IsValid(out string sourceError))
            {
                Debug.LogError($"[{owner}] 系统 UI prefab {prefab.name} 缺少必需槽位：{sourceError}，已退回默认 UI");
                return null;
            }

            var go = Object.Instantiate(prefab, parent, false);
            go.name = "Skin_" + prefab.name;
            var skin = go.GetComponent<T>();
            if (skin != null && skin.IsValid(out _)) return skin;

            Object.Destroy(go);
            Debug.LogError($"[{owner}] 系统 UI prefab {prefab.name} 实例化后校验失败，已退回默认 UI");
            return null;
        }

        public static GameObject Prefab(System.Func<VNSystemUiSkinSet, GameObject> selector)
        {
            var config = VNGameConfig.Active;
            return config != null && config.systemUiSkin != null
                ? selector(config.systemUiSkin)
                : null;
        }
    }
}
