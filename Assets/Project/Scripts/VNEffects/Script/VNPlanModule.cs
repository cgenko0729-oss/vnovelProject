using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static VNEffects.VNUi;

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
    /// 外观走系统主题 VNSystemUiSkinSet.planPrefab（VNPlanSkin 槽位），
    /// 缺失或校验失败时直接抛异常。
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

        static readonly Color SlotEmptyColor = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color SlotFilledColor = new Color(0.35f, 0.55f, 0.95f, 0.25f);

        readonly List<VNPlanDef.ActionDef> _actions = new List<VNPlanDef.ActionDef>();
        int[] _slots;          // 每格的行动编号（0 = 空/休息）
        bool _closing;
        bool _dispatchMode;    // op:next 纯流程派发（不记回想，一周会调 N 次）

        /// <summary>逐格派发只是流程控制，记进回想全是「plan → next」噪音</summary>
        public override bool RecordInBacklog => !_dispatchMode;

        VNPlanSkin _skin;
        RectTransform _panel;
        readonly List<TMP_Text> _slotTexts = new List<TMP_Text>();
        readonly List<Image> _slotImages = new List<Image>();          // 条目可为 null（皮肤格没配背景槽）
        readonly List<RectTransform> _slotRects = new List<RectTransform>();
        Color _slotEmptyColor = SlotEmptyColor;
        Color _slotFilledColor = SlotFilledColor;

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
        // UI 构建：全局主题皮肤（VNSystemUiSkinSet.planPrefab）
        // ------------------------------------------------------------------

        void BuildUi(string titleText)
        {
            _slotTexts.Clear();
            _slotImages.Clear();
            _slotRects.Clear();

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.planPrefab);
            _skin = VNSystemUiSkinUtility.Instantiate<VNPlanSkin>(
                skinPrefab, transform, "VNPlanModule");
            if (_skin == null)
                throw new System.InvalidOperationException("Plan prefab is missing or invalid.");
            BuildFromSkin(titleText);

            RefreshSlots();

            // 面板弹入
            _panel.localScale = Vector3.one * 0.92f;
            _panel.DOScale(1f, 0.28f).SetEase(Ease.OutBack)
                  .SetUpdate(true).SetLink(gameObject);
        }

        /// <summary>皮肤路径：模板行克隆 + 槽位接线，布局外观全由 prefab 决定</summary>
        void BuildFromSkin(string titleText)
        {
            _panel = _skin.panelRoot;
            _skin.titleText.text = titleText;
            if (_skin.resetLabel != null) _skin.resetLabel.text = VNLocale.T("plan.reset");
            if (_skin.confirmLabel != null) _skin.confirmLabel.text = VNLocale.T("plan.confirm");
            _skin.resetButton.onClick.RemoveAllListeners();
            _skin.resetButton.onClick.AddListener(ResetAll);
            _skin.confirmButton.onClick.RemoveAllListeners();
            _skin.confirmButton.onClick.AddListener(Confirm);
            _slotEmptyColor = _skin.slotTemplate.emptyColor;
            _slotFilledColor = _skin.slotTemplate.filledColor;

            _skin.actionTemplate.gameObject.SetActive(false);
            foreach (var action in _actions)
            {
                var captured = action;
                var row = Instantiate(_skin.actionTemplate, _skin.actionContent, false);
                row.gameObject.SetActive(true);
                row.button.onClick.RemoveAllListeners();
                row.button.onClick.AddListener(() => AssignToFirstEmpty(captured));
                if (row.icon != null && action.icon != null)
                {
                    row.icon.sprite = action.icon;
                    row.icon.preserveAspect = true;
                }
                if (row.gainText != null)
                {
                    row.nameText.text = action.DisplayName;
                    row.gainText.text = action.LocalizedGainText ?? "";
                }
                else
                {
                    row.nameText.richText = true;
                    string gain = string.IsNullOrEmpty(action.LocalizedGainText)
                        ? ""
                        : $"\n<size=20><color=#a8d8a8>{action.LocalizedGainText}</color></size>";
                    row.nameText.text = $"{action.DisplayName}{gain}";
                }
            }

            _skin.slotTemplate.gameObject.SetActive(false);
            for (int i = 0; i < _slots.Length; i++)
            {
                int captured = i;
                var row = Instantiate(_skin.slotTemplate, _skin.slotContent, false);
                row.gameObject.SetActive(true);
                row.dayText.text = VNLocale.T("plan.slot", i + 1);
                row.button.onClick.RemoveAllListeners();
                row.button.onClick.AddListener(() => ClearSlot(captured));
                _slotRects.Add((RectTransform)row.transform);
                _slotTexts.Add(row.assignedText);
                _slotImages.Add(row.background);
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
                if (_slotImages[i] != null)
                    _slotImages[i].color = filled ? _slotFilledColor : _slotEmptyColor;
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
            var rect = _slotRects[index];
            rect.DOKill(true);
            rect.DOPunchScale(Vector3.one * 0.05f, 0.2f, 8, 0.6f)
                .SetUpdate(true).SetLink(gameObject);
        }

        // ------------------------------------------------------------------
        // 共用小件（同 VNShopModule 做法）
        // ------------------------------------------------------------------

    }
}
