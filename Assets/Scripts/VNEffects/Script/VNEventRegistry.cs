using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块注册表：id → 模块模板（预制体或场景内禁用的模板物体）。
    /// 剧本 event 命令用 id 引用；模块实例化到独立 EventLayer
    /// （排序 60，位于 ChoicePanel 45 与 ScreenTransition 100 之间，
    /// 因此进出事件可以用全屏转场包裹）。
    /// </summary>
    public class VNEventRegistry : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            [Header("剧本 event 命令引用的 id（可中文，如 地图 / 史莱姆战）")]
            public string id;
            [Header("模块模板：预制体或场景内禁用状态的物体（运行时 Instantiate）")]
            public VNEventModule template;
        }

        [Header("事件模块库：剧本 event <id> 调起这里登记的模块")]
        public List<Entry> modules = new List<Entry>();

        public const int LayerSortingOrder = 60;

        RectTransform _layer;

        /// <summary>登记的全部 id（编辑器下拉/校验用）</summary>
        public IEnumerable<string> Ids
        {
            get
            {
                foreach (var e in modules)
                    if (e != null && !string.IsNullOrEmpty(e.id)) yield return e.id;
            }
        }

        /// <summary>实例化 id 对应的模块到事件层；找不到返回 null 并告警</summary>
        public VNEventModule Create(string id, Canvas canvas, int line = 0)
        {
            Entry entry = null;
            foreach (var e in modules)
                if (e != null && e.id == id && e.template != null) { entry = e; break; }
            if (entry == null)
            {
                Debug.LogWarning($"[VNEvent] 第 {line} 行：事件模块库里没有「{id}」" +
                                 "（在 VNEventRegistry.modules 登记）");
                return null;
            }
            if (canvas == null)
            {
                Debug.LogError($"[VNEvent] 第 {line} 行：找不到 UI Canvas，无法创建事件层");
                return null;
            }

            var module = Instantiate(entry.template, EnsureLayer(canvas));
            var rect = module.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            module.gameObject.SetActive(true);
            return module;
        }

        RectTransform EnsureLayer(Canvas rootCanvas)
        {
            if (_layer != null) return _layer;

            var go = new GameObject("EventLayer", typeof(RectTransform));
            _layer = (RectTransform)go.transform;
            _layer.SetParent(rootCanvas.transform, false);
            _layer.anchorMin = Vector2.zero;
            _layer.anchorMax = Vector2.one;
            _layer.offsetMin = Vector2.zero;
            _layer.offsetMax = Vector2.zero;

            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = LayerSortingOrder;
            go.AddComponent<GraphicRaycaster>();
            return _layer;
        }
    }
}
