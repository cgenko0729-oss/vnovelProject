using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 委托板事件模块：
    ///   event questboard [tag:标签] [max:同时接取上限] [title:板子标题]
    ///   * 接取
    ///   * 离开
    ///
    /// 列出「未接取 + 前置完成 + 出现条件满足 + 不在冷却 + 未达次数上限」的任务
    /// （判定与自动接取共用 VNQuestEngine.CanAccept，不会出现「板上有但接不了」）。
    ///
    /// 模块三铁律：不碰舞台（背景立绘交给事件前后的剧本行）、时间走 VNTime.Delta、
    /// Update 首行让 VNPause 早退。玩家可以连着接好几个，点「离开」才结束。
    /// </summary>
    public class VNQuestBoardModule : VNEventModule
    {
        const string OutcomeAccepted = "接取";
        const string OutcomeLeft = "离开";

        RectTransform _root;
        RectTransform _content;
        TextMeshProUGUI _emptyText;
        string _tag;
        int _maxActive;
        bool _acceptedAny;

        protected override void OnLaunch(VNEventContext ctx)
        {
            _tag = ctx.Kw("tag");
            _maxActive = ctx.KwI("max", 0);   // 0 = 不限
            string title = ctx.Kw("title", VNLocale.T("quest.board.title"));

            _root = transform as RectTransform;
            if (_root == null)
            {
                Debug.LogError("[VNQuestBoard] 模块模板必须带 RectTransform（走场景装机菜单添加）");
                Done(OutcomeLeft);
                return;
            }
            Stretch(_root);

            BuildUi(title);
            RebuildList();
        }

        void Update()
        {
            // 教程讲解等全局暂停期间不吃输入（三铁律②：必须在读输入之前）
            if (VNPause.IsPaused) return;

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Leave();
        }

        void Leave() => Done(_acceptedAny ? OutcomeAccepted : OutcomeLeft);

        // ------------------------------------------------------------------
        // UI
        // ------------------------------------------------------------------

        void BuildUi(string title)
        {
            // 全屏暗幕（委托板是独立场景，可以盖住对话框）
            var dim = new GameObject("Dim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var dimRect = (RectTransform)dim.transform;
            dimRect.SetParent(_root, false);
            Stretch(dimRect);
            dim.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.03f, 0.9f);

            var head = CreateText(_root, 36, TextAlignmentOptions.Center);
            head.text = title;
            head.fontStyle = FontStyles.Bold;
            var headRect = (RectTransform)head.transform;
            headRect.anchorMin = new Vector2(0f, 1f);
            headRect.anchorMax = new Vector2(1f, 1f);
            headRect.pivot = new Vector2(0.5f, 1f);
            headRect.anchoredPosition = new Vector2(0f, -34f);
            headRect.sizeDelta = new Vector2(0f, 54f);

            // 滚动列表
            var scrollGo = new GameObject("Scroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(_root, false);
            scrollRect.anchorMin = new Vector2(0.16f, 0.14f);
            scrollRect.anchorMax = new Vector2(0.84f, 0.88f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;

            var viewportGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            var viewportRect = (RectTransform)viewportGo.transform;
            viewportRect.SetParent(scrollRect, false);
            Stretch(viewportRect);
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _content = (RectTransform)contentGo.transform;
            _content.SetParent(viewportRect, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;   // 同任务面板：不清零会被 RectMask2D 裁掉左右
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewportRect;
            scroll.content = _content;

            // 离开按钮
            var leaveGo = new GameObject("Leave",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var leaveRect = (RectTransform)leaveGo.transform;
            leaveRect.SetParent(_root, false);
            leaveRect.anchorMin = new Vector2(0.5f, 0f);
            leaveRect.anchorMax = new Vector2(0.5f, 0f);
            leaveRect.pivot = new Vector2(0.5f, 0f);
            leaveRect.anchoredPosition = new Vector2(0f, 40f);
            leaveRect.sizeDelta = new Vector2(320f, 58f);
            leaveGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.1f);
            var leaveLabel = CreateText(leaveRect, 26, TextAlignmentOptions.Center);
            Stretch((RectTransform)leaveLabel.transform);
            leaveLabel.text = VNLocale.T("quest.board.leave");
            leaveGo.GetComponent<Button>().onClick.AddListener(Leave);
        }

        void RebuildList()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            var list = VNQuestEngine.BoardList(_tag);
            if (list.Count == 0)
            {
                _emptyText = CreateText(_content, 28, TextAlignmentOptions.Center);
                _emptyText.text = VNLocale.T("quest.board.empty");
                _emptyText.color = new Color(1f, 1f, 1f, 0.55f);
                return;
            }

            var hud = FindFirstObjectByType<VNStatsHud>();
            foreach (var def in list) AddRow(def, hud);
        }

        void AddRow(VNQuestDef def, VNStatsHud hud)
        {
            var box = new GameObject("Request_" + def.id,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            box.transform.SetParent(_content, false);
            box.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            var layout = box.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.padding = new RectOffset(18, 18, 12, 12);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            box.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var text = CreateText(box.transform, 27, TextAlignmentOptions.TopLeft);
            text.richText = true;
            var sb = new System.Text.StringBuilder();
            sb.Append($"<color=#ffd75e><b>{def.Title}</b></color>");
            if (!string.IsNullOrEmpty(def.clientCharacterId))
                sb.Append($"　<size=21><color=#c8c8d2>" +
                          $"{VNLocale.T("quest.board.client", def.clientCharacterId)}</color></size>");
            if (!string.IsNullOrEmpty(def.LocalizedDescription))
                sb.Append($"\n<size=23><color=#c8c8d2>{def.LocalizedDescription}</color></size>");

            string reward = VNQuestReward.Preview(def.RewardsAt(1), hud);
            if (!string.IsNullOrEmpty(reward))
                sb.Append($"\n<size=23><color=#9fd3e0>" +
                          $"{VNLocale.T("quest.board.reward", reward)}</color></size>");
            if (def.deadlineMonths > 0)
                sb.Append($"\n<size=22><color=#9a9aa5>" +
                          $"{VNLocale.T("quest.board.deadline", def.deadlineMonths)}</color></size>");
            text.text = sb.ToString();

            // 接取按钮（超出同时接取上限就灰掉）
            bool full = _maxActive > 0 && ActiveCount() >= _maxActive;
            var btnGo = new GameObject("Accept",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Button), typeof(LayoutElement));
            btnGo.transform.SetParent(box.transform, false);
            btnGo.GetComponent<Image>().color = full
                ? new Color(1f, 1f, 1f, 0.05f)
                : new Color(1f, 0.84f, 0.37f, 0.16f);
            var le = btnGo.GetComponent<LayoutElement>();
            le.preferredHeight = 46f;
            le.minHeight = 46f;

            var label = CreateText(btnGo.transform, 24, TextAlignmentOptions.Center);
            Stretch((RectTransform)label.transform);
            label.richText = true;
            label.text = full
                ? $"<color=#9a9aa5>{VNLocale.T("quest.board.accept")}</color>"
                : $"<color=#ffd75e><b>{VNLocale.T("quest.board.accept")}</b></color>";

            var btn = btnGo.GetComponent<Button>();
            btn.interactable = !full;
            string id = def.id;   // 闭包捕获局部副本
            btn.onClick.AddListener(() =>
            {
                VNQuestEngine.Accept(id);
                _acceptedAny = true;
                RebuildList();    // 接完就从板上消失，同时刷新其他行的「已满」状态
            });
        }

        static int ActiveCount()
        {
            int n = 0;
            foreach (var d in VNQuestEngine.Defs)
                if (VNQuestEngine.IsActive(d.id)) n++;
            return n;
        }

        // ------------------------------------------------------------------

        TextMeshProUGUI CreateText(Transform parent, int size, TextAlignmentOptions anchor)
        {
            var go = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = VNFont.Asset;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = new Color(1f, 1f, 1f, 0.94f);
            t.lineSpacing = 15f;
            t.raycastTarget = false;   // 事件层排在选项面板之上，文字绝不能吃射线
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
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
