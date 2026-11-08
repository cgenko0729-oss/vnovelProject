using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DG.Tweening;
using DG.Tweening.Core.Easing;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects.EditorTools
{
    public partial class VNCamseqEditorWindow
    {
        // ==================================================================
        // 构图辅助线
        // ==================================================================

        void ShowGuidesMenu(Rect rect)
        {
            var menu = new GenericMenu();
            void Toggle(string label, Guides bit) =>
                menu.AddItem(new GUIContent(label), (_guides & bit) != 0,
                    () => SetGuides(_guides ^ bit));

            Toggle("三分线", Guides.Thirds);
            Toggle("中心十字", Guides.Center);
            Toggle("安全区(90%)", Guides.SafeArea);
            Toggle("对话框遮挡区", Guides.DialogueBox);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("全开"), false, () => SetGuides(Guides.All));
            menu.AddItem(new GUIContent("全关"), false, () => SetGuides(Guides.None));
            menu.DropDown(rect);
        }

        void SetGuides(Guides value)
        {
            _guides = value;
            EditorPrefs.SetInt(GuidesPrefKey, (int)value);
            Repaint();
        }

        /// <summary>
        /// 在「一屏画面」上画构图辅助线。
        /// <paramref name="frame"/> = 那一屏在 GUI 里的矩形：整图模式下是选中路径点的
        /// 取景框（辅助线跟着框缩放平移），镜头视角模式下就是整个画布。
        /// <paramref name="clip"/> = 画布矩形，超出的部分裁掉（zoom&lt;1 时取景框比画布还大）。
        ///
        /// 【为什么对话框遮挡区按比例画在 frame 里】对话框是 Canvas 下 ZoomRoot 的**兄弟**，
        /// 不随镜头缩放平移 —— 它在玩家眼里永远压着屏幕底部那一条。所以它要按"占一屏的
        /// 比例"落在取景框内，而不是画布上的某个固定位置。
        /// </summary>
        void DrawCompositionGuides(Rect frame, Rect clip)
        {
            if (_guides == Guides.None || frame.width < 4f || frame.height < 4f) return;

            if ((_guides & Guides.Thirds) != 0)
            {
                var c = new Color(1f, 1f, 1f, 0.3f);
                for (int i = 1; i <= 2; i++)
                {
                    float t = i / 3f;
                    VLine(frame.x + frame.width * t, frame.yMin, frame.yMax, c, clip);
                    HLine(frame.y + frame.height * t, frame.xMin, frame.xMax, c, clip);
                }
            }

            if ((_guides & Guides.Center) != 0)
            {
                var c = new Color(0.45f, 1f, 0.7f, 0.45f);
                VLine(frame.center.x, frame.yMin, frame.yMax, c, clip);
                HLine(frame.center.y, frame.xMin, frame.xMax, c, clip);
            }

            if ((_guides & Guides.SafeArea) != 0)
            {
                float w = frame.width * 0.9f, h = frame.height * 0.9f;
                var safe = new Rect(frame.center.x - w * 0.5f, frame.center.y - h * 0.5f, w, h);
                DrawRectOutlineClipped(safe, new Color(0.4f, 1f, 0.55f, 0.5f), 1f, clip);
            }

            if ((_guides & Guides.DialogueBox) != 0)
            {
                var band = DialogueBandFractions();
                var r = Rect.MinMaxRect(
                    frame.x + band.xMin * frame.width, frame.y + band.yMin * frame.height,
                    frame.x + band.xMax * frame.width, frame.y + band.yMax * frame.height);
                DrawRectClipped(r, new Color(1f, 0.35f, 0.35f, 0.15f), clip);
                DrawRectOutlineClipped(r, new Color(1f, 0.45f, 0.45f, 0.55f), 1f, clip);
            }
        }

        /// <summary>
        /// 对话框在一屏里占的比例（x 自左、y 自上，0~1）。
        /// 优先量场景里真实的对话框（换了皮肤/改了尺寸都跟得上）；
        /// 量不到就退回演示场景生成器的默认布局（1920×1080 下 x 5%~95%、
        /// 底边上方 28px 起、高 230px）。
        /// </summary>
        static Rect DialogueBandFractions()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            var box = stage != null && stage.dialogue != null
                ? stage.dialogue : Object.FindFirstObjectByType<VNDialogueBox>();
            var rt = box != null ? box.transform as RectTransform : null;
            var canvas = rt != null ? rt.GetComponentInParent<Canvas>() : null;
            var root = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;

            if (rt != null && root != null)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);            // 0=左下 1=左上 2=右上 3=右下
                var a = root.InverseTransformPoint(corners[0]);
                var b = root.InverseTransformPoint(corners[2]);
                var cr = root.rect;
                if (cr.width > 1f && cr.height > 1f)
                {
                    float x0 = Mathf.InverseLerp(cr.xMin, cr.xMax, Mathf.Min(a.x, b.x));
                    float x1 = Mathf.InverseLerp(cr.xMin, cr.xMax, Mathf.Max(a.x, b.x));
                    // GUI 的 y 自上而下，画布的 y 自下而上 → 上下对调
                    float y0 = 1f - Mathf.InverseLerp(cr.yMin, cr.yMax, Mathf.Max(a.y, b.y));
                    float y1 = 1f - Mathf.InverseLerp(cr.yMin, cr.yMax, Mathf.Min(a.y, b.y));
                    if (x1 > x0 && y1 > y0)
                        return Rect.MinMaxRect(Mathf.Clamp01(x0), Mathf.Clamp01(y0),
                                               Mathf.Clamp01(x1), Mathf.Clamp01(y1));
                }
            }

            const float h = 230f / 1080f, bottom = 28f / 1080f;
            return Rect.MinMaxRect(0.05f, 1f - bottom - h, 0.95f, 1f - bottom);
        }

        static void DrawRectClipped(Rect r, Color c, Rect clip)
        {
            var i = Rect.MinMaxRect(
                Mathf.Max(r.xMin, clip.xMin), Mathf.Max(r.yMin, clip.yMin),
                Mathf.Min(r.xMax, clip.xMax), Mathf.Min(r.yMax, clip.yMax));
            if (i.width > 0f && i.height > 0f) EditorGUI.DrawRect(i, c);
        }

        static void DrawRectOutlineClipped(Rect r, Color c, float t, Rect clip)
        {
            DrawRectClipped(new Rect(r.x, r.y, r.width, t), c, clip);
            DrawRectClipped(new Rect(r.x, r.yMax - t, r.width, t), c, clip);
            DrawRectClipped(new Rect(r.x, r.y, t, r.height), c, clip);
            DrawRectClipped(new Rect(r.xMax - t, r.y, t, r.height), c, clip);
        }

        static void VLine(float x, float y0, float y1, Color c, Rect clip) =>
            DrawRectClipped(new Rect(x, y0, 1f, y1 - y0), c, clip);

        static void HLine(float y, float x0, float x1, Color c, Rect clip) =>
            DrawRectClipped(new Rect(x0, y, x1 - x0, 1f), c, clip);

        // ==================================================================
        // 时间轴轨道
        // ==================================================================

        /// <summary>
        /// 把 BuildSegments() 的结果画成一排色块：段宽 = 时长，颜色 = 段类型。
        ///
        /// 上层 14px 是刻度条（点/拖 = 移播放头），下层是段块（点 = 选中路径点、
        /// 拖右边界 = 改时长）。两层分开是为了让「移播放头」和「改时长」永不打架 ——
        /// 同一块区域既要 scrub 又要拖边界的话，每一次点击都得猜用户想干嘛。
        /// </summary>
        void DrawTimeline()
        {
            var segs = BuildSegments();
            var area = GUILayoutUtility.GetRect(10f, RulerHeight + TrackHeight + 3f,
                GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.15f));

            if (segs.Count == 0)
            {
                GUI.Label(new Rect(area.x + 6f, area.y + 6f, area.width, 18f),
                    "还没有路径点 —— 在画布上点一下，或用「内置模板 ▾」起个头",
                    EditorStyles.miniLabel);
                return;
            }

            var ruler = new Rect(area.x, area.y, area.width, RulerHeight);
            var track = new Rect(area.x, area.y + RulerHeight + 1f, area.width, TrackHeight);
            var widths = SegmentWidths(segs, track.width);

            float x = track.x;
            for (int i = 0; i < segs.Count && x < track.xMax; i++)
            {
                float w = Mathf.Min(widths[i], track.xMax - x);
                DrawSegmentBlock(new Rect(x, track.y, w, track.height), segs[i], widths[i]);
                x += widths[i];
            }

            DrawTimelineRuler(ruler, segs, widths);

            // 播放头：贯穿两层，顶端一个小三角（细线在色块上容易看丢）
            float headX = Mathf.Clamp(TimeToX(segs, widths, track.x, _scrub),
                area.x, area.xMax - 1f);
            EditorGUI.DrawRect(new Rect(headX, area.y, 1f, area.height), Color.white);
            EditorGUI.DrawRect(new Rect(headX - 3f, area.y, 7f, 3f), Color.white);

            HandleTimelineInput(ruler, track, segs, widths);
        }

        /// <summary>
        /// 每段占的像素宽。**保底 MinSegWidth**：0 秒瞬切段（camseq 起手最常见的写法）
        /// 严格按时长算就是 0 像素，既看不见也点不中。
        /// 拖动中改用按下瞬间冻结的像素/秒，不再按新的总时长重新铺满。
        /// </summary>
        float[] SegmentWidths(List<Segment> segs, float avail)
        {
            int n = segs.Count;
            var widths = new float[n];
            if (n == 0) return widths;

            if (_dragSegIndex >= 0 && _dragPxPerSec > 0.01f)
            {
                // 公式必须与下面的铺满分支**逐项等价**（保底宽 + 时长×像素每秒），
                // 否则按下鼠标的那一帧整条轨道会整体跳一下 —— 冻结比例就是为了不跳
                for (int i = 0; i < n; i++)
                    widths[i] = MinSegWidth + Mathf.Max(0f, segs[i].duration) * _dragPxPerSec;
                return widths;
            }

            float minTotal = n * MinSegWidth;
            if (avail <= minTotal)
            {
                for (int i = 0; i < n; i++) widths[i] = avail / n;
                return widths;
            }

            float total = 0f;
            foreach (var seg in segs) total += Mathf.Max(0f, seg.duration);
            float extra = avail - minTotal;
            for (int i = 0; i < n; i++)
                widths[i] = MinSegWidth + (total > 0.0001f
                    ? Mathf.Max(0f, segs[i].duration) / total * extra
                    : extra / n);
            return widths;
        }

        static Color SegColor(Segment s)
        {
            switch (s.kind)
            {
                case SegKind.Hold: return new Color(0.82f, 0.58f, 0.22f);   // 停留 = 橙
                case SegKind.Xfade: return new Color(0.55f, 0.42f, 0.80f);  // 叠化 = 紫
                case SegKind.StartFade:
                case SegKind.EndFade: return new Color(0.40f, 0.44f, 0.52f); // 开场/收尾 = 灰蓝
                default:
                    return s.duration <= 0.001f
                        ? new Color(0.52f, 0.55f, 0.60f)                     // 瞬切 = 灰
                        : new Color(0.24f, 0.50f, 0.78f);                    // 补间 = 蓝
            }
        }

        void DrawSegmentBlock(Rect r, Segment s, float fullWidth)
        {
            bool selected = s.pointIndex >= 0 && s.pointIndex == _list.index;
            var color = SegColor(s);
            if (selected) color = Color.Lerp(color, Color.white, 0.28f);

            EditorGUI.DrawRect(new Rect(r.x + 1f, r.y, Mathf.Max(1f, r.width - 2f), r.height), color);

            // 左边缘 3px 用画布序号牌的颜色（起绿 / 中蓝 / 终红）：
            // 时间轴和画布是两个视图，靠这条色边才对得上「这一段是哪个框」
            if (s.pointIndex >= 0 && r.width > 4f)
                EditorGUI.DrawRect(new Rect(r.x + 1f, r.y, 3f, r.height), PointColor(s.pointIndex));
            if (selected) DrawRectOutline(r, Color.white, 1f);

            string label = SegLabel(s, fullWidth);
            if (label.Length > 0)
                GUI.Label(new Rect(r.x + 5f, r.y + 6f, Mathf.Max(10f, r.width - 6f), 16f),
                    label, EditorStyles.whiteMiniLabel);
        }

        /// <summary>段块上的字：宽度不够就一层层砍（全文 → 只留头 → 什么都不画）</summary>
        string SegLabel(Segment s, float width)
        {
            string head;
            switch (s.kind)
            {
                case SegKind.StartFade: head = "开场叠化"; break;
                case SegKind.EndFade: head = "收尾叠化"; break;
                case SegKind.Xfade: head = $"{s.pointIndex + 1} 叠"; break;
                case SegKind.Hold:
                    // 停顿 = max(hold, 震动时长)。被震撑起来的那种要标出来，
                    // 否则会以为是自己写的 hold，怎么拖都拖不短
                    var w = s.pointIndex >= 0 && s.pointIndex < _points.Count
                        ? _points[s.pointIndex] : null;
                    bool byShake = w != null && VNShakeSpec.TryParse(w.shake, out VNShakeSpec sp)
                                   && sp.Valid && sp.duration >= w.hold - 0.001f;
                    head = $"{s.pointIndex + 1} {(byShake ? "震" : "停")}";
                    break;
                default:
                    head = s.duration <= 0.001f
                        ? $"{s.pointIndex + 1} ◆" : $"{s.pointIndex + 1}";
                    break;
            }
            if (width > 62f) return $"{head} {s.duration:0.##}s";
            if (width > 26f) return head;
            return "";
        }

        void DrawTimelineRuler(Rect ruler, List<Segment> segs, float[] widths)
        {
            EditorGUI.DrawRect(ruler, new Color(0.18f, 0.18f, 0.21f));

            float total = 0f;
            foreach (var seg in segs) total += Mathf.Max(0f, seg.duration);
            if (total <= 0.001f) return;

            // 段有保底宽度，所以刻度间距天生不均匀 —— 这是对的，它反映的是
            // 「屏幕上这一段占多宽」，与段块严格对齐才不会看串
            float step = total <= 3f ? 0.5f : total <= 10f ? 1f : 2f;
            var tickColor = new Color(1f, 1f, 1f, 0.25f);
            for (float t = 0f; t <= total + 0.0001f; t += step)
            {
                float tx = TimeToX(segs, widths, ruler.x, t);
                if (tx > ruler.xMax) break;
                EditorGUI.DrawRect(new Rect(tx, ruler.y + 7f, 1f, 7f), tickColor);
                GUI.Label(new Rect(tx + 2f, ruler.y - 2f, 40f, 16f),
                    $"{t:0.#}s", EditorStyles.miniLabel);
            }
        }

        /// <summary>时间 → 轨道上的 x（按段逐个换算，与保底宽度一致）</summary>
        static float TimeToX(List<Segment> segs, float[] widths, float trackX, float time)
        {
            float x = trackX, t = Mathf.Max(0f, time);
            for (int i = 0; i < segs.Count; i++)
            {
                float d = Mathf.Max(0f, segs[i].duration);
                if (d > 0.0001f && t < d) return x + widths[i] * (t / d);
                t -= d;
                x += widths[i];
            }
            return x;
        }

        static float XToTime(List<Segment> segs, float[] widths, float trackX, float x)
        {
            float cursor = trackX, t = 0f;
            for (int i = 0; i < segs.Count; i++)
            {
                float d = Mathf.Max(0f, segs[i].duration);
                if (x < cursor + widths[i])
                    return t + (widths[i] > 0.01f ? (x - cursor) / widths[i] * d : 0f);
                t += d;
                cursor += widths[i];
            }
            return t;
        }

        // ==================================================================
        // 时间轴交互（拖时长 / 移播放头 / 右键菜单）
        // ==================================================================

        /// <summary>这一段的时长是不是可以拖的（每种段拖的是不同字段）</summary>
        static bool CanDragSegment(Segment s)
        {
            if (s.kind == SegKind.StartFade || s.kind == SegKind.EndFade) return true;
            return s.pointIndex >= 0;
        }

        float DragValueOf(Segment s)
        {
            switch (s.kind)
            {
                case SegKind.StartFade: return _startFade;
                case SegKind.EndFade: return _endFadeDur;
            }
            if (s.pointIndex < 0 || s.pointIndex >= _points.Count) return 0f;
            var w = _points[s.pointIndex];
            return s.kind == SegKind.Xfade ? w.fade
                : s.kind == SegKind.Hold ? w.hold : w.duration;
        }

        void HandleTimelineInput(Rect ruler, Rect track, List<Segment> segs, float[] widths)
        {
            var e = Event.current;

            // ---- 拖动中：不管鼠标跑到哪都要接着响应，直到松手 ----
            if (_dragSegIndex >= 0 || _draggingRuler)
            {
                if (e.type == EventType.MouseDrag)
                {
                    if (_draggingRuler)
                        _scrub = Mathf.Max(0f, XToTime(segs, widths, track.x, e.mousePosition.x));
                    else
                        ApplyDragValue(_dragStartValue
                            + (e.mousePosition.x - _dragStartMouseX) / _dragPxPerSec);
                    e.Use();
                    Repaint();
                    return;
                }
                if (e.type == EventType.MouseUp || e.type == EventType.Ignore)
                {
                    _dragSegIndex = -1;
                    _draggingRuler = false;
                    e.Use();
                    Repaint();
                    return;
                }
            }

            // ---- 边界处的横向拉伸光标（不用点开就知道这里能拖）----
            float x = track.x;
            for (int i = 0; i < segs.Count; i++)
            {
                x += widths[i];
                if (CanDragSegment(segs[i]))
                    EditorGUIUtility.AddCursorRect(
                        new Rect(x - EdgeGrab, track.y, EdgeGrab * 2f, track.height),
                        MouseCursor.ResizeHorizontal);
            }

            if (e.type != EventType.MouseDown) return;

            // ---- 刻度条：点/拖 = 移播放头 ----
            if (ruler.Contains(e.mousePosition) && e.button == 0)
            {
                _scrub = Mathf.Max(0f, XToTime(segs, widths, track.x, e.mousePosition.x));
                _playing = false;
                _draggingRuler = true;
                e.Use();
                Repaint();
                return;
            }

            if (!track.Contains(e.mousePosition)) return;

            // ---- 段块：先看边界（拖时长），再看块body（选中 / 右键菜单）----
            x = track.x;
            for (int i = 0; i < segs.Count; i++)
            {
                float left = x;
                x += widths[i];

                if (e.button == 0 && CanDragSegment(segs[i]) &&
                    Mathf.Abs(e.mousePosition.x - x) <= EdgeGrab)
                {
                    float total = 0f;
                    foreach (var seg in segs) total += Mathf.Max(0f, seg.duration);
                    // 冻结像素/秒：按「除去保底宽度后剩下的像素」算，与铺满时的比例一致
                    float usable = track.width - segs.Count * MinSegWidth;
                    _dragPxPerSec = total > 0.01f && usable > 20f
                        ? usable / total : 120f;
                    _dragSegIndex = i;
                    _dragSegKind = segs[i].kind;
                    _dragSegPoint = segs[i].pointIndex;
                    _dragStartValue = DragValueOf(segs[i]);
                    _dragStartMouseX = e.mousePosition.x;
                    if (segs[i].pointIndex >= 0) _list.index = segs[i].pointIndex;
                    e.Use();
                    Repaint();
                    return;
                }

                if (e.mousePosition.x < left || e.mousePosition.x >= x) continue;

                if (e.button == 1)
                {
                    ShowSegmentMenu(segs[i]);
                    e.Use();
                    return;
                }
                if (e.button == 0)
                {
                    // 只选中，不动播放头 —— 播放头归刻度条管
                    if (segs[i].pointIndex >= 0) _list.index = segs[i].pointIndex;
                    e.Use();
                    Repaint();
                    return;
                }
            }
        }

        /// <summary>把拖出来的秒数写回对应字段（吸附 0.1 秒，按住 Ctrl 自由）</summary>
        void ApplyDragValue(float value)
        {
            value = Mathf.Max(0f, value);
            if (!Event.current.control) value = Mathf.Round(value / SnapStep) * SnapStep;
            value = Mathf.Round(value * 1000f) / 1000f;

            switch (_dragSegKind)
            {
                case SegKind.StartFade:
                    _startFade = Mathf.Max(0.05f, value);
                    return;
                case SegKind.EndFade:
                    _endFadeDur = Mathf.Max(0.05f, value);
                    return;
            }

            if (_dragSegPoint < 0 || _dragSegPoint >= _points.Count) return;
            var w = _points[_dragSegPoint];
            switch (_dragSegKind)
            {
                // xfade 拖到 0 会让这一段从「叠化」变回「补间」，段的身份在拖动途中
                // 突变很难受，所以留一个 0.05 的地板；要取消叠化走右键菜单
                case SegKind.Xfade: w.fade = Mathf.Max(0.05f, value); break;
                case SegKind.Hold: w.hold = value; break;
                default: w.duration = value; break;
            }
        }

        void ShowSegmentMenu(Segment s)
        {
            var menu = new GenericMenu();

            if (s.kind == SegKind.StartFade)
            {
                menu.AddItem(new GUIContent("关掉开场叠化"), false, () =>
                {
                    _startMode = StartMode.None;
                    Repaint();
                });
                menu.ShowAsContext();
                return;
            }
            if (s.kind == SegKind.EndFade)
            {
                menu.AddItem(new GUIContent("关掉收尾叠化"), false, () =>
                {
                    _endFade = false;
                    Repaint();
                });
                menu.ShowAsContext();
                return;
            }

            int pi = s.pointIndex;
            if (pi < 0 || pi >= _points.Count) return;
            var w = _points[pi];

            foreach (float v in new[] { 0f, 0.3f, 0.5f, 0.8f, 1.2f, 2f })
            {
                float value = v;
                menu.AddItem(new GUIContent($"时长/{(value <= 0f ? "0（瞬切）" : value + " 秒")}"),
                    Mathf.Approximately(w.duration, value), () =>
                    {
                        w.duration = value;
                        Repaint();
                    });
            }
            menu.AddSeparator("");

            if (w.hold > 0.0001f)
                menu.AddItem(new GUIContent("清掉停留 hold"), false, () =>
                {
                    w.hold = 0f;
                    Repaint();
                });
            if (!string.IsNullOrEmpty(w.shake))
                menu.AddItem(new GUIContent("清掉震屏 shake"), false, () =>
                {
                    w.shake = "";
                    Repaint();
                });
            if (w.fade > 0.0001f)
                menu.AddItem(new GUIContent("清掉叠化 xfade"), false, () =>
                {
                    w.fade = 0f;
                    Repaint();
                });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("在后面插入一个同位置的点"), false, () =>
            {
                var copy = CloneWaypoint(w);
                copy.hold = 0f;
                copy.fade = 0f;
                copy.shake = "";          // 副参数不复制：连震两下几乎都是误操作
                _points.Insert(pi + 1, copy);
                _list.index = pi + 1;
                Repaint();
            });
            menu.AddItem(new GUIContent("删除这个路径点"), false, () =>
            {
                _points.RemoveAt(pi);
                _list.index = Mathf.Clamp(_list.index, -1, _points.Count - 1);
                Repaint();
            });

            menu.ShowAsContext();
        }

        static Waypoint CloneWaypoint(Waypoint src) => new Waypoint
        {
            type = src.type,
            anchorIndex = src.anchorIndex,
            charId = src.charId,
            partIndex = src.partIndex,
            slotIndex = src.slotIndex,
            coords = src.coords,
            zoom = src.zoom,
            duration = src.duration,
            easeIndex = src.easeIndex,
            fade = src.fade,
            hold = src.hold,
            shake = src.shake,
        };
    }
}
