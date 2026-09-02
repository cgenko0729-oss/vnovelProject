using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 任务系统的场景侧入口：
    ///   ① 执行剧本 quest 命令（转发给 VNQuestEngine）
    ///   ② 驱动引擎求值（标脏 + 下一帧一次，同属性 HUD 的做法）
    ///   ③ J 键任务日志面板：可领取 / 进行中 / 已完成 / 已失败 四栏，
    ///      可领取那栏置顶并带「领取」按钮——这是玩家唯一需要动手的地方。
    ///
    /// 状态本身全在 VNFlags 里（见 VNQuestEngine 的 flag 表），本组件不持有任何状态。
    /// UI 全程序化构建在独立 Overlay Canvas 上（首次打开时创建），参照 VNBacklog。
    /// </summary>
    public class VNQuestLog : MonoBehaviour
    {
        // 常量转发（老代码与外部引用仍可用 VNQuestLog.StageDone）
        public const int StageDone = VNQuestEngine.StageDone;
        public const int StageFailed = VNQuestEngine.StageFailed;
        public const string FlagPrefix = VNQuestEngine.FlagPrefix;

        [Header("任务定义资产（标题/描述/阶段/条件/奖励）；未登记的任务用 id 当标题照常工作")]
        public List<VNQuestDef> quests = new List<VNQuestDef>();

        [Header("统计声明：把小游戏的「本次成绩」flag 派生成 @最高 / @累计 / @次数")]
        public List<VNTrackerEntry> trackers = new List<VNTrackerEntry>();

        Canvas _canvas;
        GameObject _panel;
        RectTransform _content;
        ScrollRect _scroll;
        bool _open;
        bool _dirty = true;
        int _badgeCount = -1;
        bool _configured;
        VNStatsHud _hud;
        VNStatsHud _configuredHud;

        public bool IsOpen => _open;

        // 面板配色：可领取要压过其他三栏
        const string ColClaim = "#ffd75e";
        const string ColActive = "#9fd3e0";
        const string ColDone = "#8ef5a2";
        const string ColFail = "#9a9aa5";

        void Awake()
        {
            // 定义资产列表优先读 VNGameConfig（场景重建会把它重置成 demo 的那几个）
            var cfg = VNGameConfig.Active;
            if (cfg != null)
            {
                VNGameConfig.ApplyList(cfg.quests, ref quests);
                VNGameConfig.ApplyList(cfg.trackers, ref trackers);
            }

            VNTracker.Configure(trackers);
            VNLocale.LanguageChanged += OnLanguageChanged;
            VNFlags.Changed += MarkDirty;
            VNQuestEngine.Changed += OnEngineChanged;
        }

        void OnDestroy()
        {
            VNLocale.LanguageChanged -= OnLanguageChanged;
            VNFlags.Changed -= MarkDirty;
            VNQuestEngine.Changed -= OnEngineChanged;
        }

        void MarkDirty() => _dirty = true;

        void OnEngineChanged()
        {
            if (_open) RebuildList();
        }

        void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            EnsureConfigured();
            VNQuestEngine.Evaluate();
            UpdateBadge();
        }

        /// <summary>右上角常驻角标：有几个任务在等着领。数字没变就不碰 UI</summary>
        void UpdateBadge()
        {
            int claimable = VNQuestEngine.ClaimableCount;
            if (claimable == _badgeCount) return;
            _badgeCount = claimable;
            VNToast.SetBadge(claimable > 0 ? VNLocale.T("quest.badge", claimable) : null);
        }

        /// <summary>
        /// 属性 HUD 可能比本组件晚一步出现，所以惰性补配置。
        /// 只在真正需要时重建引擎的任务表——这个方法每次 flag 变化的下一帧都会被走到，
        /// 无条件 Configure 等于每写一次 stat 就复制一遍整张任务列表。
        /// </summary>
        void EnsureConfigured()
        {
            if (_hud == null) _hud = FindFirstObjectByType<VNStatsHud>();
            if (_configured && _configuredHud == _hud) return;
            _configured = true;
            _configuredHud = _hud;
            VNQuestEngine.Configure(quests, _hud);
        }

        /// <summary>语言切换：面板惰性构建，销毁缓存让下次打开用新语言重建</summary>
        void OnLanguageChanged()
        {
            if (_open) Close();
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _panel = null;
            _content = null;
            _scroll = null;
        }

        public static string FlagName(string id) => VNQuestEngine.FlagName(id);

        /// <summary>任务当前阶段（0 = 未接取）</summary>
        public static int StageOf(string id) => VNQuestEngine.StageOf(id);

        public VNQuestDef Find(string id)
        {
            foreach (var q in quests)
                if (q != null && q.id == id) return q;
            return null;
        }

        // ------------------------------------------------------------------
        // quest 命令执行
        // ------------------------------------------------------------------

        /// <summary>
        /// 执行 quest 命令。silent = 调试重建时只写状态不弹 Toast。
        ///   quest start|stage|done|fail &lt;id&gt; [阶段]
        ///   quest claim|offer|abandon|reset &lt;id&gt;
        /// </summary>
        public void Apply(string op, string id, int stage, bool silent, int line)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"[VNQuest] 第 {line} 行：quest 需要任务 id" +
                                 "（quest start|stage|done|fail|claim|offer|abandon|reset <id>）");
                return;
            }

            EnsureConfigured();

            switch (op)
            {
                case "start":
                    VNQuestEngine.Accept(id, Mathf.Max(1, stage), silent);
                    break;

                case "stage":
                    if (stage <= 0)
                    {
                        Debug.LogWarning($"[VNQuest] 第 {line} 行：quest stage 需要阶段号" +
                                         $"（quest stage {id} 2）");
                        return;
                    }
                    if (stage > VNQuestEngine.MaxStage)
                    {
                        Debug.LogWarning($"[VNQuest] 第 {line} 行：阶段号 {stage} 超出上限 " +
                                         $"{VNQuestEngine.MaxStage}（100 与 -1 是完成/失败的保留值）");
                        return;
                    }
                    VNQuestEngine.SetStage(id, stage, silent);
                    break;

                case "done":
                    VNQuestEngine.Complete(id, silent);
                    break;

                case "fail":
                    VNQuestEngine.Fail(id, silent);
                    break;

                case "claim":
                    // 剧本内强制领取（「她当场把奖励塞给你」）；没有可领的就什么也不做
                    VNQuestEngine.Claim(id, silent);
                    break;

                case "offer":
                    VNQuestEngine.Offer(id);
                    break;

                case "abandon":
                    VNQuestEngine.Abandon(id, silent);
                    break;

                case "reset":
                    VNQuestEngine.Reset(id);
                    break;

                default:
                    Debug.LogWarning($"[VNQuest] 第 {line} 行：未知 quest 操作「{op}」" +
                                     "（start/stage/done/fail/claim/offer/abandon/reset）");
                    return;
            }

            _dirty = true;
            if (_open) RebuildList();
        }

        // ------------------------------------------------------------------
        // 任务日志面板
        // ------------------------------------------------------------------

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        public void Open()
        {
            if (_open) return;
            EnsureConfigured();
            Build();
            RebuildList();
            _panel.SetActive(true);
            _open = true;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 1f; // 从顶部开始看
        }

        public void Close()
        {
            if (!_open) return;
            _panel.SetActive(false);
            _open = false;
        }

        void Build()
        {
            if (_panel != null) return;

            var canvasGo = new GameObject("VNQuestLogCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 600; // 与回想同层：同一时刻只会开一个
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)_panel.transform;
            panelRect.SetParent(canvasGo.transform, false);
            Stretch(panelRect);

            // 半透明暗底（点击关闭）
            var dimGo = new GameObject("Dim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var dimRect = (RectTransform)dimGo.transform;
            dimRect.SetParent(panelRect, false);
            Stretch(dimRect);
            dimGo.GetComponent<Image>().color = new Color(0f, 0.01f, 0.02f, 0.86f);
            dimGo.GetComponent<Button>().onClick.AddListener(Close);

            var title = CreateText(panelRect, 34, TextAlignmentOptions.Center);
            title.text = VNLocale.T("quest.title");
            title.fontStyle = FontStyles.Bold;
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -26f);
            titleRect.sizeDelta = new Vector2(0f, 50f);

            var scrollGo = new GameObject("Scroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(panelRect, false);
            scrollRect.anchorMin = new Vector2(0.18f, 0.07f);
            scrollRect.anchorMax = new Vector2(0.82f, 0.9f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.scrollSensitivity = 40f;

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
            // sizeDelta 默认 (100,100)，横向拉伸下 = 比视口宽 100px → 左右各溢出 50px
            // 被 RectMask2D 裁掉（任务标题左边缺字），必须显式清零
            _content.sizeDelta = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 18f;
            layout.padding = new RectOffset(14, 14, 8, 8);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = viewportRect;
            _scroll.content = _content;

            _panel.SetActive(false);
        }

        /// <summary>面板一条目所需的全部信息（定义资产可能不存在，所以先摊平）</summary>
        struct JournalEntry
        {
            public string id, title, description, stageText;
            public int stage, pending;
            public VNQuestDef def;
        }

        void RebuildList()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            // 已登记定义的任务按优先级；没有定义资产的活动任务补在后面
            var entries = new List<JournalEntry>();
            var knownIds = new HashSet<string>();
            var ordered = new List<VNQuestDef>();
            foreach (var q in quests)
                if (q != null && !string.IsNullOrEmpty(q.id)) ordered.Add(q);
            ordered.Sort((a, b) => b.priority.CompareTo(a.priority));

            foreach (var q in ordered)
            {
                knownIds.Add(q.id);
                int stage = VNQuestEngine.StageOf(q.id);
                if (stage == 0) continue;                        // 未接取不显示
                if (q.hidden && stage != StageDone) continue;    // 隐藏任务完成前不露面
                entries.Add(new JournalEntry
                {
                    id = q.id,
                    title = q.Title,
                    description = q.LocalizedDescription,
                    stageText = q.StageText(stage),
                    stage = stage,
                    pending = VNQuestEngine.PendingOf(q.id),
                    def = q,
                });
            }
            foreach (var kv in VNFlags.All)
            {
                // 只认主 flag，@待领 / @接取月 这些旁路 flag 要跳过
                if (!kv.Key.StartsWith(FlagPrefix) || kv.Value == 0) continue;
                string id = kv.Key.Substring(FlagPrefix.Length);
                if (id.IndexOf(VNTracker.ReservedChar) >= 0) continue;
                if (knownIds.Contains(id)) continue;
                entries.Add(new JournalEntry
                {
                    id = id, title = id, stage = kv.Value,
                    pending = VNQuestEngine.PendingOf(id),
                });
            }

            if (entries.Count == 0)
            {
                var empty = CreateText(_content, 28, TextAlignmentOptions.Center);
                empty.text = VNLocale.T("quest.empty");
                empty.color = new Color(1f, 1f, 1f, 0.55f);
                return;
            }

            AddSection(entries, VNLocale.T("quest.sectionClaimable"),
                e => e.pending > 0, ColClaim);
            AddSection(entries, VNLocale.T("quest.sectionActive"),
                e => e.pending <= 0 && e.stage > 0 && e.stage != StageDone, ColActive);
            AddSection(entries, VNLocale.T("quest.sectionDone"),
                e => e.stage == StageDone, ColDone);
            AddSection(entries, VNLocale.T("quest.sectionFailed"),
                e => e.stage == StageFailed, ColFail);
        }

        void AddSection(List<JournalEntry> entries, string heading,
            System.Predicate<JournalEntry> match, string colorHex)
        {
            bool any = false;
            foreach (var e in entries)
            {
                if (!match(e)) continue;
                if (!any)
                {
                    any = true;
                    var head = CreateText(_content, 26, TextAlignmentOptions.TopLeft);
                    head.text = $"<color={colorHex}>── {heading} ──</color>";
                }
                AddEntry(e, colorHex);
            }
        }

        /// <summary>一个任务条目：标题 + 描述 + 阶段 + 子目标（含进度条）+ 奖励 + 领取按钮</summary>
        void AddEntry(JournalEntry e, string colorHex)
        {
            var box = new GameObject("Quest_" + e.id,
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            box.transform.SetParent(_content, false);
            var boxLayout = box.GetComponent<VerticalLayoutGroup>();
            boxLayout.spacing = 4f;
            boxLayout.childControlHeight = true;
            boxLayout.childControlWidth = true;
            boxLayout.childForceExpandHeight = false;
            box.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // 标题 + 描述 + 阶段文案
            var head = CreateText(box.transform, 28, TextAlignmentOptions.TopLeft);
            head.richText = true;
            var sb = new System.Text.StringBuilder();
            sb.Append($"<color={colorHex}><b>{e.title}</b></color>");

            string badge = EntryBadge(e);
            if (!string.IsNullOrEmpty(badge))
                sb.Append($"　<size=22><color=#c8c8d2>{badge}</color></size>");
            if (!string.IsNullOrEmpty(e.description))
                sb.Append($"\n<size=24><color=#c8c8d2>{e.description}</color></size>");
            if (!string.IsNullOrEmpty(e.stageText) &&
                e.stage != StageDone && e.stage != StageFailed)
                sb.Append($"\n▶ {e.stageText}");
            head.text = sb.ToString();

            // 子目标（只在进行中/可领取时显示）
            if (e.def != null && e.stage > 0 && e.stage != StageDone && e.stage != StageFailed)
            {
                var stageDef = e.def.StageAt(e.stage);
                if (stageDef != null && stageDef.objectives != null)
                {
                    foreach (var o in stageDef.objectives)
                    {
                        if (o == null || string.IsNullOrEmpty(o.LocalizedText)) continue;
                        bool met = o.IsMet;
                        var line = CreateText(box.transform, 24, TextAlignmentOptions.TopLeft);
                        line.richText = true;
                        string mark = met ? "☑" : "☐";
                        string tint = met ? ColDone : "#c8c8d2";
                        string suffix = o.HasProgressBar
                            ? $"　<color=#9a9aa5>{VNFlags.Get(o.progressFlag)}/{o.progressTarget}</color>"
                            : "";
                        line.text = $"　<color={tint}>{mark} {o.LocalizedText}</color>{suffix}";
                        if (o.HasProgressBar) AddProgressBar(box.transform, o.Progress01, met);
                    }
                }

                // 奖励预览
                string preview = e.def.StageAt(e.stage) != null
                    ? VNQuestReward.Preview(e.def.StageAt(e.stage).rewards, _hud) : "";
                if (!string.IsNullOrEmpty(preview))
                {
                    var rw = CreateText(box.transform, 23, TextAlignmentOptions.TopLeft);
                    rw.richText = true;
                    rw.text = $"　<color=#9a9aa5>{VNLocale.T("quest.rewards")}</color>" +
                              $"　<color={ColClaim}>{preview}</color>";
                }
            }

            // 领取按钮
            if (e.pending > 0) AddClaimButton(box.transform, e.id);
        }

        /// <summary>标题右侧的小字：期限 / 完成次数</summary>
        string EntryBadge(JournalEntry e)
        {
            if (e.def == null) return "";
            if (e.stage == StageDone)
            {
                int times = VNFlags.Get(VNQuestEngine.TimesFlag(e.id));
                return e.def.repeatable && times > 0
                    ? VNLocale.T("quest.repeatCount", times) : "";
            }
            if (e.stage > 0 && e.def.deadlineMonths > 0)
            {
                int left = e.def.deadlineMonths -
                           (VNQuestEngine.Month - VNFlags.Get(VNQuestEngine.AcceptedMonthFlag(e.id)));
                return VNLocale.T("quest.deadline", Mathf.Max(0, left));
            }
            return "";
        }

        void AddProgressBar(Transform parent, float progress01, bool met)
        {
            var barGo = new GameObject("Bar",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            barGo.transform.SetParent(parent, false);
            barGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            barGo.GetComponent<Image>().raycastTarget = false;
            var le = barGo.GetComponent<LayoutElement>();
            le.preferredHeight = 6f;
            le.minHeight = 6f;

            var fillGo = new GameObject("Fill",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var fill = (RectTransform)fillGo.transform;
            fill.SetParent(barGo.transform, false);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress01), 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var img = fillGo.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = met ? new Color(0.56f, 0.96f, 0.64f, 0.9f)
                            : new Color(0.62f, 0.83f, 0.88f, 0.9f);
        }

        void AddClaimButton(Transform parent, string questId)
        {
            var btnGo = new GameObject("Claim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Button), typeof(LayoutElement));
            btnGo.transform.SetParent(parent, false);
            var img = btnGo.GetComponent<Image>();
            img.color = new Color(1f, 0.84f, 0.37f, 0.16f);
            var le = btnGo.GetComponent<LayoutElement>();
            le.preferredHeight = 44f;
            le.minHeight = 44f;

            var label = CreateText(btnGo.transform, 25, TextAlignmentOptions.Center);
            var labelRect = (RectTransform)label.transform;
            Stretch(labelRect);
            label.text = $"<color={ColClaim}><b>{VNLocale.T("quest.claim")}</b></color>";
            label.richText = true;

            var btn = btnGo.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1.35f);
            btn.colors = colors;
            string id = questId;   // 闭包捕获局部副本
            btn.onClick.AddListener(() =>
            {
                // 领取演出走引擎内部：Toast 卡片 + VNStatsHud 的数字滚动与 +N 上飘
                if (!VNQuestEngine.Claim(id)) return;
                RebuildList();
            });
        }

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
            t.lineSpacing = 15f; // TMP 行距为字号百分比，15 ≈ legacy 1.15 倍
            t.raycastTarget = false;
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
