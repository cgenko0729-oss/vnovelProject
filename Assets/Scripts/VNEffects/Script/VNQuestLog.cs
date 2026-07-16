using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 任务系统：状态全部落在 VNFlags（flag 名 = 任务_&lt;id&gt;），因此存档、if 分支、
    /// 调试重建全部复用现有设施，本组件只负责两件事：
    ///   ① 执行剧本 quest 命令（写 flag + VNToast 提示）
    ///   ② J 键任务日志面板（从 flags 反查状态渲染，进行中/已完成/已失败分栏）
    /// 阶段约定：0 未接取 / 1..n 进行中 / 100 完成 / -1 失败。
    /// UI 全程序化构建在独立 Overlay Canvas 上（首次打开时创建），参照 VNBacklog。
    /// </summary>
    public class VNQuestLog : MonoBehaviour
    {
        public const int StageDone = 100;
        public const int StageFailed = -1;
        public const string FlagPrefix = "任务_";

        [Tooltip("任务定义资产（标题/描述/阶段文案）；未登记的任务用 id 当标题照常工作")]
        public List<VNQuestDef> quests = new List<VNQuestDef>();

        Canvas _canvas;
        GameObject _panel;
        RectTransform _content;
        ScrollRect _scroll;
        Font _font;
        bool _open;

        public bool IsOpen => _open;

        public static string FlagName(string id) => FlagPrefix + id;

        /// <summary>任务当前阶段（0 = 未接取）</summary>
        public static int StageOf(string id) => VNFlags.Get(FlagName(id));

        public VNQuestDef Find(string id)
        {
            foreach (var q in quests)
                if (q != null && q.id == id) return q;
            return null;
        }

        // ------------------------------------------------------------------
        // quest 命令执行
        // ------------------------------------------------------------------

        /// <summary>执行 quest 命令。silent = 调试重建时只写状态不弹 Toast</summary>
        public void Apply(string op, string id, int stage, bool silent, int line)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"[VNQuest] 第 {line} 行：quest 需要任务 id" +
                                 "（quest start|stage|done|fail <id> [阶段]）");
                return;
            }

            var def = Find(id);
            string title = def != null ? def.Title : id;
            switch (op)
            {
                case "start":
                    VNFlags.Set(FlagName(id), Mathf.Max(1, stage)); // 可 quest start id 2 直接从阶段 2 开始
                    if (!silent) VNToast.Show($"新任务：{title}", 2.2f);
                    break;

                case "stage":
                    if (stage <= 0)
                    {
                        Debug.LogWarning($"[VNQuest] 第 {line} 行：quest stage 需要阶段号" +
                                         $"（quest stage {id} 2）");
                        return;
                    }
                    VNFlags.Set(FlagName(id), stage);
                    if (!silent)
                    {
                        string text = def != null ? def.StageText(stage) : "";
                        VNToast.Show(string.IsNullOrEmpty(text)
                            ? $"任务更新：{title}"
                            : $"任务更新：{title} —— {text}", 2.2f);
                    }
                    break;

                case "done":
                    VNFlags.Set(FlagName(id), StageDone);
                    if (!silent) VNToast.Show($"任务完成：{title}", 2.2f);
                    break;

                case "fail":
                    VNFlags.Set(FlagName(id), StageFailed);
                    if (!silent) VNToast.Show($"任务失败：{title}", 2.2f);
                    break;

                default:
                    Debug.LogWarning($"[VNQuest] 第 {line} 行：未知 quest 操作「{op}」" +
                                     "（start/stage/done/fail）");
                    return;
            }

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

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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

            var title = CreateText(panelRect, 34, TextAnchor.MiddleCenter);
            title.text = "—— 任务日志 ——";
            title.fontStyle = FontStyle.Bold;
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
            scrollRect.anchorMin = new Vector2(0.2f, 0.08f);
            scrollRect.anchorMax = new Vector2(0.8f, 0.9f);
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
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 22f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = viewportRect;
            _scroll.content = _content;

            _panel.SetActive(false);
        }

        struct JournalEntry
        {
            public string title, description, stageText;
            public int stage;
        }

        void RebuildList()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            // 已登记定义的任务按登记顺序；没有定义资产的活动任务补在后面
            var entries = new List<JournalEntry>();
            var knownIds = new HashSet<string>();
            foreach (var q in quests)
            {
                if (q == null || string.IsNullOrEmpty(q.id)) continue;
                knownIds.Add(q.id);
                int stage = StageOf(q.id);
                if (stage == 0) continue; // 未接取不显示
                entries.Add(new JournalEntry
                {
                    title = q.Title,
                    description = q.description,
                    stageText = q.StageText(stage),
                    stage = stage,
                });
            }
            foreach (var kv in VNFlags.All)
            {
                if (!kv.Key.StartsWith(FlagPrefix) || kv.Value == 0) continue;
                string id = kv.Key.Substring(FlagPrefix.Length);
                if (knownIds.Contains(id)) continue;
                entries.Add(new JournalEntry { title = id, stage = kv.Value });
            }

            if (entries.Count == 0)
            {
                var empty = CreateText(_content, 28, TextAnchor.MiddleCenter);
                empty.text = "（还没有接到任何任务）";
                empty.color = new Color(1f, 1f, 1f, 0.55f);
                return;
            }

            AddSection(entries, "进行中", e => e.stage > 0 && e.stage != StageDone, "#ffd27f");
            AddSection(entries, "已完成", e => e.stage == StageDone, "#8ef5a2");
            AddSection(entries, "已失败", e => e.stage == StageFailed, "#9a9aa5");
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
                    var head = CreateText(_content, 26, TextAnchor.UpperLeft);
                    head.text = $"<color={colorHex}>── {heading} ──</color>";
                }

                var t = CreateText(_content, 28, TextAnchor.UpperLeft);
                t.supportRichText = true;
                var sb = new System.Text.StringBuilder();
                sb.Append($"<color={colorHex}><b>{e.title}</b></color>");
                if (!string.IsNullOrEmpty(e.description))
                    sb.Append($"\n<size=24><color=#c8c8d2>{e.description}</color></size>");
                if (!string.IsNullOrEmpty(e.stageText) && e.stage != StageDone &&
                    e.stage != StageFailed)
                    sb.Append($"\n▶ {e.stageText}");
                t.text = sb.ToString();
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        Text CreateText(Transform parent, int size, TextAnchor anchor)
        {
            var go = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = new Color(1f, 1f, 1f, 0.94f);
            t.lineSpacing = 1.15f;
            t.raycastTarget = false;
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
