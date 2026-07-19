using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：日程排程（火山的女儿式"排一周日程"）。两种用法：
    ///
    /// ① 排程面板（op 省略）：左侧候选行动、右侧 N 个日程格，确定后写 flag：
    ///      日程_1..日程_N = 行动编号（0 = 休息/留空）、日程数 = N、当前格 = 0
    ///    event plan slots:7 pool:打工,学习,剑术训练,休息 title:安排这一周
    ///    * confirm                        ← 可选：接住确认结果；不写则顺序继续
    ///
    /// ② 逐格派发（op:next，无 UI 秒回）：当前格 +1，把该格行动编号抄进
    ///    flag「当前行动」；超出日程数返回 end——实现剧本侧"逐一执行"循环：
    ///    label 执行日程
    ///    event plan op:next
    ///    * next
    ///    * end -> 周末结算
    ///    if 当前行动==1 jump 行动_打工
    ///    ...每个行动结尾 jump 执行日程
    ///
    /// 行动详情（图标/收益文案/编号）来自 VNPlanDef 资产（模板 Inspector 登记，
    /// event plan id:xx 选择）；没有资产时 pool: 的行动名按出现顺序编号 1..n。
    /// 遵守事件模块三铁律：不碰舞台演出 / unscaled 计时 + SetUpdate(true) /
    /// 全部 Tween SetLink。
    /// </summary>
    public class VNPlanModule : VNEventModule
    {
        public const string SlotFlagPrefix = "日程_";
        public const string CountFlag = "日程数";
        public const string CursorFlag = "当前格";
        public const string ActionFlag = "当前行动";

        /// <summary>第 slot 格（1 起）的 flag 名</summary>
        public static string SlotFlagName(int slot) => SlotFlagPrefix + slot;

        [Header("本模板登记的日程方案资产（event plan id:xx 按 planId 查找）")]
        public List<VNPlanDef> plans = new List<VNPlanDef>();

        [Header("默认格子数（剧本 slots: 覆盖）")]
        public int defaultSlots = 7;

        static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.13f, 0.97f);
        static readonly Color TitleColor = new Color(1f, 0.92f, 0.7f, 1f);
        static readonly Color ButtonColor = new Color(0.1f, 0.13f, 0.22f, 0.95f);
        static readonly Color AccentColor = new Color(0.92f, 0.61f, 0.18f, 0.98f);
        static readonly Color SlotEmptyColor = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color SlotFilledColor = new Color(0.35f, 0.55f, 0.95f, 0.25f);

        readonly List<VNPlanDef.ActionDef> _actions = new List<VNPlanDef.ActionDef>();
        int[] _slots;          // 每格的行动编号（0 = 空/休息）
        bool _closing;
        bool _dispatchMode;    // op:next 纯流程派发（不记回想，一周会调 N 次）

        /// <summary>逐格派发只是流程控制，记进回想全是「plan → next」噪音</summary>
        public override bool RecordInBacklog => !_dispatchMode;

        RectTransform _panel;
        readonly List<TextMeshProUGUI> _slotTexts = new List<TextMeshProUGUI>();
        readonly List<Image> _slotImages = new List<Image>();

        protected override void OnLaunch(VNEventContext ctx)
        {
            if (ctx.Kw("op") == "next")
            {
                _dispatchMode = true;
                AdvanceCursor();
                return;
            }

            var cfg = VNGameConfig.Active;
            if (cfg != null) VNGameConfig.ApplyList(cfg.plans, ref plans);

            var def = FindDef(ctx.Kw("id"));
            BuildActionList(def, ctx.Kw("pool"), ctx.line);
            if (_actions.Count == 0)
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：event plan 没有任何可用行动" +
                                 "（配 VNPlanDef 资产或 pool: 参数），直接返回");
                Done("");
                return;
            }

            int slots = Mathf.Clamp(ctx.KwI("slots", defaultSlots), 1, 14);
            _slots = new int[slots];

            string title = ctx.Kw("title");
            if (string.IsNullOrEmpty(title) && def != null) title = def.Title;
            if (string.IsNullOrEmpty(title)) title = VNLocale.T("plan.title");

            BuildUi(title);
        }

        // ------------------------------------------------------------------
        // op:next —— 逐格派发（无 UI）
        // ------------------------------------------------------------------

        void AdvanceCursor()
        {
            int count = VNFlags.Get(CountFlag);
            int cursor = VNFlags.Get(CursorFlag) + 1;
            if (count <= 0 || cursor > count)
            {
                Done("end");
                return;
            }
            VNFlags.Set(CursorFlag, cursor);
            VNFlags.Set(ActionFlag, VNFlags.Get(SlotFlagName(cursor)));
            Done("next");
        }

        // ------------------------------------------------------------------
        // 行动清单解析
        // ------------------------------------------------------------------

        VNPlanDef FindDef(string id)
        {
            foreach (var plan in plans)
                if (plan != null && plan.planId == id) return plan;
            if (string.IsNullOrEmpty(id) && plans.Count > 0) return plans[0];
            return null;
        }

        void BuildActionList(VNPlanDef def, string pool, int line)
        {
            _actions.Clear();

            if (def == null)
            {
                // 无资产退化：pool 名字按出现顺序编号 1..n
                if (string.IsNullOrEmpty(pool)) return;
                var names = pool.Split(',');
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i].Trim();
                    if (name.Length == 0) continue;
                    _actions.Add(new VNPlanDef.ActionDef { id = name, number = _actions.Count + 1 });
                }
                return;
            }

            if (string.IsNullOrEmpty(pool))
            {
                foreach (var action in def.actions)
                    if (Available(action)) _actions.Add(action);
                return;
            }

            foreach (var token in pool.Split(','))
            {
                string id = token.Trim();
                if (id.Length == 0) continue;
                VNPlanDef.ActionDef found = null;
                foreach (var action in def.actions)
                    if (action != null && action.id == id) { found = action; break; }
                if (found == null)
                    Debug.LogWarning($"[VNEvent] 第 {line} 行：日程方案「{def.planId}」里" +
                                     $"没有行动「{id}」，已跳过");
                else if (Available(found))
                    _actions.Add(found);
            }
        }

        static bool Available(VNPlanDef.ActionDef action) =>
            action != null && !string.IsNullOrEmpty(action.id) &&
            (string.IsNullOrEmpty(action.condition) || VNFlags.Evaluate(action.condition));

        // ------------------------------------------------------------------
        // 排程交互
        // ------------------------------------------------------------------

        void AssignToFirstEmpty(VNPlanDef.ActionDef action)
        {
            if (_closing) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != 0) continue;
                _slots[i] = action.number;
                RefreshSlots();
                PunchSlot(i);
                return;
            }
            VNToast.Show(VNLocale.T("plan.noSlot"), 1.6f);
        }

        void ClearSlot(int index)
        {
            if (_closing || _slots[index] == 0) return;
            _slots[index] = 0;
            RefreshSlots();
        }

        void ResetAll()
        {
            if (_closing) return;
            for (int i = 0; i < _slots.Length; i++) _slots[i] = 0;
            RefreshSlots();
        }

        void Confirm()
        {
            if (_closing) return;
            _closing = true;

            for (int i = 0; i < _slots.Length; i++)
                VNFlags.Set(SlotFlagName(i + 1), _slots[i]);
            VNFlags.Set(CountFlag, _slots.Length);
            VNFlags.Set(CursorFlag, 0);

            if (_panel != null)
            {
                _panel.DOScale(0.92f, 0.18f).SetEase(Ease.InQuad)
                      .SetUpdate(true).SetLink(gameObject);
                DOVirtual.DelayedCall(0.18f, () => Done("confirm"), true).SetLink(gameObject);
            }
            else Done("confirm");
        }

        // ------------------------------------------------------------------
        // UI 构建（程序化，参照 VNShopModule）
        // ------------------------------------------------------------------

        void BuildUi(string titleText)
        {
            var root = (RectTransform)transform;

            var dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.72f));
            Stretch(dim);

            _panel = CreateImage("Panel", root, VNProceduralTextures.RoundedRectSprite,
                PanelColor);
            _panel.GetComponent<Image>().type = Image.Type.Sliced;
            _panel.anchorMin = new Vector2(0.14f, 0.08f);
            _panel.anchorMax = new Vector2(0.86f, 0.92f);
            _panel.offsetMin = Vector2.zero;
            _panel.offsetMax = Vector2.zero;
            _panel.localScale = Vector3.one * 0.92f;
            _panel.DOScale(1f, 0.28f).SetEase(Ease.OutBack)
                  .SetUpdate(true).SetLink(gameObject);

            var title = CreateText("Title", _panel, 40, TitleColor, titleText);
            title.fontStyle = FontStyles.Bold;
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -26f);
            titleRect.sizeDelta = new Vector2(0f, 52f);

            BuildActionColumn();
            BuildSlotColumn();

            // 底部按钮：重置 / 确定
            var reset = CreateButton(VNLocale.T("plan.reset"), _panel, ResetAll, ButtonColor);
            var resetRect = (RectTransform)reset.transform;
            resetRect.anchorMin = resetRect.anchorMax = new Vector2(0.35f, 0f);
            resetRect.pivot = new Vector2(0.5f, 0f);
            resetRect.anchoredPosition = new Vector2(0f, 22f);
            resetRect.sizeDelta = new Vector2(200f, 54f);

            var confirm = CreateButton(VNLocale.T("plan.confirm"), _panel, Confirm, AccentColor);
            var confirmRect = (RectTransform)confirm.transform;
            confirmRect.anchorMin = confirmRect.anchorMax = new Vector2(0.65f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.anchoredPosition = new Vector2(0f, 22f);
            confirmRect.sizeDelta = new Vector2(200f, 54f);

            RefreshSlots();
        }

        /// <summary>左列：候选行动（图标 + 名称 + 预期收益文案），点击填入下一个空格</summary>
        void BuildActionColumn()
        {
            CreateScrollColumn("Actions",
                new Vector2(0.04f, 0.12f), new Vector2(0.48f, 0.86f), out var content);

            foreach (var action in _actions)
            {
                var captured = action;
                var row = CreateImage("Action_" + action.id, content,
                    VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.06f));
                row.GetComponent<Image>().type = Image.Type.Sliced;
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
                var button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = row.GetComponent<Image>();
                button.onClick.AddListener(() => AssignToFirstEmpty(captured));

                var icon = CreateImage("Icon", row,
                    action.icon != null ? action.icon : VNProceduralTextures.RoundedRectSprite,
                    action.icon != null ? Color.white : new Color(0.5f, 0.65f, 1f, 0.5f));
                icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.anchoredPosition = new Vector2(14f, 0f);
                icon.sizeDelta = new Vector2(56f, 56f);
                icon.GetComponent<Image>().preserveAspect = true;
                icon.GetComponent<Image>().raycastTarget = false;

                var label = CreateText("Name", row, 27, new Color(0.95f, 0.96f, 1f, 1f), "");
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.richText = true;
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = new Vector2(84f, 0f);
                labelRect.offsetMax = new Vector2(-12f, 0f);
                string gain = string.IsNullOrEmpty(action.LocalizedGainText)
                    ? ""
                    : $"\n<size=20><color=#a8d8a8>{action.LocalizedGainText}</color></size>";
                label.text = $"{action.DisplayName}{gain}";
            }
        }

        /// <summary>右列：日程格（第 N 天 + 已排行动），点击已填格清空</summary>
        void BuildSlotColumn()
        {
            CreateScrollColumn("Slots",
                new Vector2(0.52f, 0.12f), new Vector2(0.96f, 0.86f), out var content);

            _slotTexts.Clear();
            _slotImages.Clear();
            for (int i = 0; i < _slots.Length; i++)
            {
                int captured = i;
                var row = CreateImage("Slot_" + (i + 1), content,
                    VNProceduralTextures.RoundedRectSprite, SlotEmptyColor);
                row.GetComponent<Image>().type = Image.Type.Sliced;
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
                var button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = row.GetComponent<Image>();
                button.onClick.AddListener(() => ClearSlot(captured));
                _slotImages.Add(row.GetComponent<Image>());

                var day = CreateText("Day", row, 24, new Color(1f, 0.92f, 0.7f, 0.9f),
                    VNLocale.T("plan.slot", i + 1));
                day.alignment = TextAlignmentOptions.MidlineLeft;
                var dayRect = (RectTransform)day.transform;
                dayRect.anchorMin = new Vector2(0f, 0f);
                dayRect.anchorMax = new Vector2(0.32f, 1f);
                dayRect.offsetMin = new Vector2(16f, 0f);
                dayRect.offsetMax = Vector2.zero;

                var assigned = CreateText("Assigned", row, 26,
                    new Color(0.95f, 0.96f, 1f, 1f), "");
                assigned.alignment = TextAlignmentOptions.MidlineLeft;
                var assignedRect = (RectTransform)assigned.transform;
                assignedRect.anchorMin = new Vector2(0.32f, 0f);
                assignedRect.anchorMax = new Vector2(1f, 1f);
                assignedRect.offsetMin = Vector2.zero;
                assignedRect.offsetMax = new Vector2(-12f, 0f);
                _slotTexts.Add(assigned);
            }
        }

        void RefreshSlots()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                var action = FindAction(_slots[i]);
                bool filled = _slots[i] != 0 && action != null;
                _slotTexts[i].text = filled
                    ? action.DisplayName
                    : $"<color=#8a8fa0>{VNLocale.T("plan.rest")}</color>";
                _slotImages[i].color = filled ? SlotFilledColor : SlotEmptyColor;
            }
        }

        VNPlanDef.ActionDef FindAction(int number)
        {
            foreach (var action in _actions)
                if (action.number == number) return action;
            return null;
        }

        void PunchSlot(int index)
        {
            var rect = (RectTransform)_slotTexts[index].transform.parent;
            rect.DOKill(true);
            rect.DOPunchScale(Vector3.one * 0.05f, 0.2f, 8, 0.6f)
                .SetUpdate(true).SetLink(gameObject);
        }

        // ------------------------------------------------------------------
        // 共用小件（同 VNShopModule 做法）
        // ------------------------------------------------------------------

        RectTransform CreateScrollColumn(string name, Vector2 anchorMin, Vector2 anchorMax,
            out RectTransform content)
        {
            var scrollGo = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(_panel, false);
            scrollRect.anchorMin = anchorMin;
            scrollRect.anchorMax = anchorMax;
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content = (RectTransform)contentGo.transform;
            content.SetParent(scrollRect, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            // 必须显式清零：RectTransform 默认 sizeDelta=(100,100)，横向拉伸锚点下
            // 它是「相对父宽的增量」→ 内容会比视口宽 100px，pivot 0.5 使左右各溢出
            // 50px 被 RectMask2D 裁掉（ContentSizeFitter 只管 y，永远不会修正 x）
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = scrollRect;
            return scrollRect;
        }

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

        GameObject CreateButton(string label, RectTransform parent,
            UnityEngine.Events.UnityAction onClick, Color color)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = CreateText("Label", rect, 26, new Color(0.95f, 0.96f, 1f, 1f), label);
            Stretch((RectTransform)text.transform);
            return go;
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
