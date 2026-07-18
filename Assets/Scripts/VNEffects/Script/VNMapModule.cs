using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：地图地点选择（自由行动）。全屏地图底图 + 可点击地点标记，
    /// 点击地点 = 返回该地点名作为结果，由剧本「* 结果行」接分支。
    ///
    /// 地点在模块模板的 Inspector 里配置（归一化坐标 + 可选显示条件）；
    /// 剧本「* 结果行」进一步控制本次开放哪些地点（没写结果行 = 全部开放）。
    /// 选中地点自动 flag「去过_&lt;地点&gt;」+1（可关），已去过的标记显示 ✓。
    ///
    /// 剧本用法（结果名 = 地点名）：
    ///   event map title:去哪里走走？ [bg:背景id]
    ///   * 教室 -> 教室剧情
    ///   * 天台 -> 天台剧情        ← 模板里天台配了条件 好感度>=2 时，不满足则隐藏
    /// </summary>
    public class VNMapModule : VNEventModule
    {
        [System.Serializable]
        public class Location
        {
            [Header("地点名 = 返回给剧本的结果名（对应「* 结果行」，永远不翻译）")]
            public string name;
            [Header("英文显示名（本地化；留空 = 显示地点名）")]
            public string displayNameEn;
            [Header("日文显示名（本地化；留空 = 显示地点名）")]
            public string displayNameJa;
            [Header("在地图上的归一化坐标（0,0 左下 ～ 1,1 右上）")]
            public Vector2 position = new Vector2(0.5f, 0.5f);
            [Header("显示条件（VNFlags 表达式，如 好感度>=2；留空 = 总是显示）")]
            public string condition;
            [Header("可选自定义图标；留空 = 程序化光点")]
            public Sprite icon;

            /// <summary>当前语言的显示名；逻辑（结果匹配、去过_xx flag）永远用 name</summary>
            public string DisplayName
            {
                get
                {
                    switch (VNLocale.Language)
                    {
                        case VNLanguage.English:
                            return string.IsNullOrEmpty(displayNameEn) ? name : displayNameEn;
                        case VNLanguage.Japanese:
                            return string.IsNullOrEmpty(displayNameJa) ? name : displayNameJa;
                        default: return name;
                    }
                }
            }
        }

        [Header("地图底图；剧本 bg:<背景id> 参数可临时换用舞台背景库里的图")]
        public Sprite mapSprite;
        [Header("地点列表（结果名/坐标/条件）")]
        public List<Location> locations = new List<Location>();
        [Header("选中地点后自动 flag「去过_<地点>」+1")]
        public bool markVisited = true;

        static readonly Color MarkerColor = new Color(0.5f, 0.9f, 1f, 1f);
        static readonly Color VisitedColor = new Color(0.65f, 0.95f, 0.75f, 1f);

        bool _chosen;

        protected override void OnLaunch(VNEventContext ctx)
        {
            // 可用地点 = 通过条件 且 被本次「* 结果行」接住
            var visible = new List<Location>();
            foreach (var loc in locations)
            {
                if (loc == null || string.IsNullOrEmpty(loc.name)) continue;
                if (!string.IsNullOrEmpty(loc.condition) &&
                    !VNFlags.Evaluate(loc.condition, ctx.line)) continue;
                if (!ctx.AcceptsOutcome(loc.name)) continue;
                visible.Add(loc);
            }
            if (visible.Count == 0)
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：地图「{ctx.eventId}」" +
                                 "没有任何可用地点（条件全不满足或结果行没接住），直接返回");
                Done("");
                return;
            }

            BuildUi(ctx, visible);
        }

        void BuildUi(VNEventContext ctx, List<Location> visible)
        {
            // 暗幕
            var dim = CreateImage("Dim", (RectTransform)transform, null,
                new Color(0f, 0f, 0f, 0.72f));
            Stretch(dim);

            // 地图底图（不保比例铺满区域，保证归一化坐标与画面对应）
            Sprite sprite = ResolveMapSprite(ctx);
            var map = CreateImage("Map", (RectTransform)transform, sprite,
                sprite != null ? Color.white : new Color(0.1f, 0.12f, 0.2f, 0.95f));
            map.anchorMin = new Vector2(0.08f, 0.06f);
            map.anchorMax = new Vector2(0.92f, 0.86f);
            map.offsetMin = Vector2.zero;
            map.offsetMax = Vector2.zero;
            if (sprite == null)
            {
                var img = map.GetComponent<Image>();
                img.sprite = VNProceduralTextures.RoundedRectSprite;
                img.type = Image.Type.Sliced;
            }

            // 标题
            var title = CreateText("Title", (RectTransform)transform, 44,
                new Color(1f, 1f, 1f, 0.95f), ctx.Kw("title", VNLocale.T("map.title")));
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.93f);
            titleRect.sizeDelta = new Vector2(1200f, 60f);

            // 地点标记（错开弹入）
            for (int i = 0; i < visible.Count; i++)
                CreateMarker(map, visible[i], 0.08f * i);
        }

        Sprite ResolveMapSprite(VNEventContext ctx)
        {
            string bgId = ctx.Kw("bg");
            if (!string.IsNullOrEmpty(bgId) && ctx.stage != null)
            {
                foreach (var b in ctx.stage.backgrounds)
                    if (b != null && b.id == bgId && b.sprite != null) return b.sprite;
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：地图找不到背景「{bgId}」，" +
                                 "使用模板底图");
            }
            return mapSprite;
        }

        void CreateMarker(RectTransform map, Location loc, float delay)
        {
            bool visited = markVisited && VNFlags.Get("去过_" + loc.name) > 0;
            Color color = visited ? VisitedColor : MarkerColor;

            var go = new GameObject($"Marker_{loc.name}", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(map, false);
            rect.anchorMin = rect.anchorMax = loc.position;
            rect.sizeDelta = new Vector2(96f, 96f);

            var image = go.GetComponent<Image>();
            image.sprite = loc.icon != null ? loc.icon
                : VNProceduralTextures.RadialGlowSprite;
            image.color = color;

            // 中心亮点（自定义图标时省略）
            RectTransform core = null;
            if (loc.icon == null)
            {
                core = CreateImage("Core", rect,
                    VNProceduralTextures.RadialGlowSprite, Color.white);
                core.anchorMin = core.anchorMax = new Vector2(0.5f, 0.5f);
                core.sizeDelta = new Vector2(34f, 34f);
                core.GetComponent<Image>().raycastTarget = false;
            }

            // 地点名（标记下方，显示当前语言译名）
            var label = CreateText("Label", rect, 30, Color.white,
                visited ? loc.DisplayName + " ✓" : loc.DisplayName);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, -26f);
            labelRect.sizeDelta = new Vector2(300f, 40f);
            // TMP 不走 uGUI Outline 组件，用 SDF 材质自带描边（更锐利）
            label.outlineWidth = 0.22f;
            label.outlineColor = new Color32(0, 0, 0, 217);

            go.GetComponent<Button>().onClick.AddListener(() => Choose(loc, rect));
            go.AddComponent<MarkerHover>();

            // 弹入 + 中心亮点常驻呼吸脉动
            rect.localScale = Vector3.zero;
            var seq = DOTween.Sequence().SetUpdate(true).SetLink(go)
                .AppendInterval(delay)
                .Append(rect.DOScale(1f, 0.35f).SetEase(Ease.OutBack));
            if (core != null)
            {
                var coreRect = core;
                seq.OnComplete(() =>
                    coreRect.DOScale(1.25f, 0.9f)
                            .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine)
                            .SetUpdate(true).SetLink(coreRect.gameObject));
            }
        }

        void Choose(Location loc, RectTransform marker)
        {
            if (_chosen) return;
            _chosen = true;
            if (markVisited) VNFlags.Add("去过_" + loc.name, 1);

            marker.DOKill();
            marker.DOPunchScale(Vector3.one * 0.3f, 0.25f, 6, 0.7f)
                  .SetUpdate(true).SetLink(gameObject);
            DOVirtual.DelayedCall(0.3f, () => Done(loc.name), true).SetLink(gameObject);
        }

        /// <summary>标记悬停放大（uGUI 指针事件，独立小组件）</summary>
        class MarkerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public void OnPointerEnter(PointerEventData e) => Scale(1.18f);
            public void OnPointerExit(PointerEventData e) => Scale(1f);

            void Scale(float target)
            {
                transform.DOKill();
                transform.DOScale(target, 0.18f).SetEase(Ease.OutQuad)
                         .SetUpdate(true).SetLink(gameObject);
            }
        }

        // ------------------------------------------------------------------

        static RectTransform CreateImage(string name, RectTransform parent,
            Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return rect;
        }

        TextMeshProUGUI CreateText(string name, RectTransform parent, int size,
            Color color, string content)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
