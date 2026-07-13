using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 轻量屏幕提示：
    ///   VNToast.Show("已保存")        —— 底部居中气泡，1.6 秒后淡出
    ///   VNToast.SetMode("AUTO")       —— 右上角常驻模式标签（传 null 清除）
    /// 自建独立 Overlay Canvas（渲染在一切之上），首次调用时自动创建。
    /// </summary>
    public static class VNToast
    {
        static Canvas _canvas;
        static Text _toast;
        static Text _mode;
        static Tween _toastTween;

        static void EnsureCanvas()
        {
            if (_canvas != null) return;

            var go = new GameObject("VNToastCanvas",
                typeof(Canvas), typeof(CanvasScaler));
            Object.DontDestroyOnLoad(go);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 999;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _toast = CreateText(go.transform, font, 30, TextAnchor.MiddleCenter);
            var tr = (RectTransform)_toast.transform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f);
            tr.pivot = new Vector2(0.5f, 0f);
            tr.anchoredPosition = new Vector2(0f, 300f);
            tr.sizeDelta = new Vector2(900f, 56f);
            SetAlpha(_toast, 0f);

            _mode = CreateText(go.transform, font, 30, TextAnchor.UpperRight);
            _mode.fontStyle = FontStyle.Bold;
            var mr = (RectTransform)_mode.transform;
            mr.anchorMin = mr.anchorMax = new Vector2(1f, 1f);
            mr.pivot = new Vector2(1f, 1f);
            mr.anchoredPosition = new Vector2(-36f, -24f);
            mr.sizeDelta = new Vector2(300f, 44f);
            _mode.color = new Color(1f, 0.85f, 0.4f, 0.9f);
            _mode.text = "";
        }

        static Text CreateText(Transform parent, Font font, int size, TextAnchor anchor)
        {
            var go = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = new Color(1f, 1f, 1f, 0.95f);
            t.raycastTarget = false;
            return t;
        }

        static void SetAlpha(Text t, float a)
        {
            var c = t.color;
            c.a = a;
            t.color = c;
        }

        /// <summary>底部提示气泡，短暂显示后淡出</summary>
        public static void Show(string message, float holdSeconds = 1.6f)
        {
            EnsureCanvas();
            _toastTween?.Kill();
            _toast.text = message;
            SetAlpha(_toast, 1f);
            _toastTween = DOTween.Sequence()
                .AppendInterval(holdSeconds)
                .Append(_toast.DOFade(0f, 0.5f));
        }

        /// <summary>右上角常驻模式标签（AUTO/SKIP），传 null 或空清除</summary>
        public static void SetMode(string label)
        {
            EnsureCanvas();
            _mode.text = string.IsNullOrEmpty(label) ? "" : label;
        }
    }
}
