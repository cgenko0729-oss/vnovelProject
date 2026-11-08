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
        // 迷你画布
        // ==================================================================

        /// <summary>
        /// 画布 + 底边分隔条。画布**严格保持 16:9**（取景框、辅助线、立绘比例全靠它），
        /// 高度由分隔条决定、宽度反算，窗口比画布宽时靠左放。
        /// 高度 0 = 自动（按窗口宽度铺满，也就是加分隔条之前的老行为）。
        /// </summary>
        /// <summary>画布这一刻的像素高度（栏很窄时按宽度反过来限高，否则画布顶出所在栏）</summary>
        float CanvasPixelHeight(float availWidth)
        {
            float viewWidth = Mathf.Max(120f, availWidth);
            float height = _canvasHeight > 0f
                ? Mathf.Clamp(_canvasHeight, CanvasMinHeight, CanvasMaxHeight)
                : viewWidth * 9f / 16f;
            return Mathf.Min(height, viewWidth * 9f / 16f);
        }

        void DrawCanvasArea(float availWidth)
        {
            float height = CanvasPixelHeight(availWidth);

            var area = GUILayoutUtility.GetRect(10f, height + SplitterHeight,
                GUILayout.ExpandWidth(true));
            float width = Mathf.Min(area.width, height * 16f / 9f);

            DrawCanvas(new Rect(area.x, area.y, width, height));
            DrawCanvasSplitter(new Rect(area.x, area.y + height, width, SplitterHeight), height);
        }

        void DrawCanvasSplitter(Rect r, float currentHeight)
        {
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.18f));
            EditorGUI.DrawRect(new Rect(r.center.x - 14f, r.y + r.height * 0.5f - 1f, 28f, 2f),
                new Color(1f, 1f, 1f, _draggingCanvasSplit ? 0.75f : 0.32f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical);

            var e = Event.current;
            bool inside = r.Contains(e.mousePosition);

            // 双击复位成自动高度。判断必须排在下面的 MouseDown 之前，
            // 不然第一下按下就把事件 Use 掉了，clickCount 永远到不了 2
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && inside)
            {
                SetCanvasHeight(0f, true);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && inside)
            {
                _draggingCanvasSplit = true;
                _splitStartY = e.mousePosition.y;
                _splitStartHeight = currentHeight;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingCanvasSplit)
            {
                // 拖动中只改内存值：EditorPrefs 写的是注册表，每帧写既慢又没必要
                SetCanvasHeight(_splitStartHeight + (e.mousePosition.y - _splitStartY), false);
                e.Use();
                Repaint();
            }
            else if (_draggingCanvasSplit &&
                     (e.type == EventType.MouseUp || e.type == EventType.Ignore))
            {
                _draggingCanvasSplit = false;
                EditorPrefs.SetFloat(CanvasHeightPrefKey, _canvasHeight);
                e.Use();
                Repaint();
            }
        }

        void SetCanvasHeight(float height, bool persist)
        {
            _canvasHeight = height <= 0f
                ? 0f : Mathf.Clamp(height, CanvasMinHeight, CanvasMaxHeight);
            if (persist) EditorPrefs.SetFloat(CanvasHeightPrefKey, _canvasHeight);
        }

        void DrawCanvas(Rect rect)
        {
            // 底：背景缩略图或深色底
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.12f));

            // 镜头视角模式：整个画面按当前时刻的运镜变换后再画，
            // 画布 = 玩家真正看到的那一帧（拖进度条/▶ 就是运镜动画）
            //
            // 整图模式虽然不跟镜头走（永远显示整块画布），但**立绘的额外倍率要照跟**——
            // mode:depth/bg/char 改的是立绘自己的 localScale，那是画布上的真实内容，
            // 不是取景。不跟的话 mode:char 会整个预览纹丝不动（容器 zoom 恒为 1），
            // 而运行时立绘其实正在放大，两边对不上。
            var previewState = PreviewAtTime(_scrub).state;
            var view = _cameraView
                ? previewState
                : new CamState
                {
                    offset = Vector2.zero, zoom = 1f, charScale = previewState.CharScale,
                };

            // 内容会超出画布（放大后背景/立绘都溢出），统一裁进画布内
            GUI.BeginGroup(rect);
            var local = new Rect(0f, 0f, rect.width, rect.height);
            var bgSprite = CanvasBackdrop();
            if (bgSprite != null)
                DrawSpriteAt(local, ViewPoint(view, Vector2.zero), CanvasHalf * view.zoom, bgSprite);
            DrawStageCharacters(local, view);
            GUI.EndGroup();

            DrawRectOutline(rect, new Color(1f, 1f, 1f, 0.35f), 1f);

            // 镜头视角下画面本身就是取景结果，再叠取景框/路径线只会挡视线
            if (_cameraView)
            {
                // 画布 = 玩家看到的那一屏，辅助线直接铺满
                DrawCompositionGuides(rect, rect);
                GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, 200f, 18f),
                    "镜头视角（拖进度条看运镜）", EditorStyles.whiteMiniLabel);
                return;
            }

            // char 模式镜头压根不动：每个点的取景框都等于整块画布，四个框完全重叠，
            // 序号牌会挤成一坨还挡住立绘。这时候画面里唯一会变的是立绘尺寸（上面已画），
            // 所以取景框整套跳过，改在角上说一句为什么
            if (_zoomMode == VNCamZoomMode.Char)
            {
                GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, 320f, 18f),
                    "mode:char —— 镜头不动，只有立绘在缩放", EditorStyles.whiteMiniLabel);
                return;
            }

            // 各路径点取景框 + 序号牌 + 流向箭头
            DrawWaypointFrames(rect);

            // 预览取景框（沿路径插值）；叠化时再画一个渐隐的橙色残框 = 正在淡出的旧视角
            if (_points.Count > 0)
            {
                var ps = PreviewAtTime(_scrub);
                var center = -ps.state.offset / ps.state.zoom;
                DrawCanvasFrame(rect, center, CanvasHalf / ps.state.zoom, Color.white, 2.5f);
                if (ps.fading)
                {
                    var gc = -ps.fadeFrom.offset / ps.fadeFrom.zoom;
                    DrawCanvasFrame(rect, gc, CanvasHalf / ps.fadeFrom.zoom,
                        new Color(1f, 0.6f, 0.2f, Mathf.Clamp01(ps.ghostAlpha)), 2f);
                }
            }

            // 构图辅助线画在「选中路径点的取景框」里——那一框才是玩家看到的一屏；
            // 没选中点就跟着预览框走
            if (_guides != Guides.None && _points.Count > 0)
            {
                var gs = _list.index >= 0 && _list.index < _points.Count
                    ? TargetState(_list.index)
                    : PreviewAtTime(_scrub).state;
                DrawCompositionGuides(
                    FrameGuiRect(rect, -gs.offset / gs.zoom, CanvasHalf / gs.zoom), rect);
            }

            HandleCanvasInput(rect);
        }

        /// <summary>
        /// 在场立绘：按剧本推算出的站位 + VNStage 的真实高度画，
        /// 取景框套没套住脸能直接看出来。关掉开关 / 没绑定 / 没立绘可用时，
        /// 退回原来那三个灰色站位矩形。
        /// </summary>
        /// <summary>
        /// 画全部路径点：取景框 + 序号牌 + 流向箭头。
        ///
        /// 【为什么分四遍画】所有框长得一样时根本分不清谁是谁，所以：
        /// ① 路径线与箭头垫在最底（被框压住无所谓）；
        /// ② 非选中框压到 alpha 0.16，只当「其他点大概在哪」的参考；
        /// ③ 选中框实线高亮，必须画在淡框之后——同一遍里画会被后面的淡框糊掉边；
        /// ④ 序号牌最后画，永远不透明、永远夹在画布内，它才是认框的唯一可靠线索。
        ///
        /// stay 点与前一个真点的取景框完全重合，同尺寸画只会互相遮住，
        /// 因此按「连续第几个 stay」逐个往里缩一圈并改虚线；序号照常给，
        /// 画布上的编号就不会跳号了（以前 stay 直接跳过，只看得到 1、3）。
        /// </summary>
        void DrawWaypointFrames(Rect rect)
        {
            int count = _points.Count;
            if (count == 0) return;

            var frames = new Rect[count];
            var centers = new Vector2[count];
            var stays = new bool[count];
            int stayRun = 0;

            for (int i = 0; i < count; i++)
            {
                stays[i] = _points[i].type == PointType.Stay;
                stayRun = stays[i] ? stayRun + 1 : 0;

                var st = TargetState(i);             // stay 自动沿用前面最近的真点
                centers[i] = -st.offset / st.zoom;
                frames[i] = FrameGuiRect(rect, centers[i], CanvasHalf / st.zoom);
                if (stays[i]) frames[i] = Inset(frames[i], StayInset * stayRun);
            }

            // ① 路径线（只连真点：stay 原地不动，连出来是个零长度段）
            Vector2? prev = null;
            for (int i = 0; i < count; i++)
            {
                if (stays[i]) continue;
                if (prev.HasValue) DrawFlowLine(rect, prev.Value, centers[i]);
                prev = centers[i];
            }

            // ② 非选中框
            for (int i = 0; i < count; i++)
            {
                if (i == _list.index) continue;
                var c = PointColor(i);
                c.a = FaintAlpha;
                if (stays[i]) DrawDashedRect(frames[i], c, 1f);
                else DrawRectOutline(frames[i], c, 1f);
            }

            // ③ 选中框
            if (_list.index >= 0 && _list.index < count)
            {
                if (stays[_list.index]) DrawDashedRect(frames[_list.index], PointSelectedColor, 2f);
                else DrawRectOutline(frames[_list.index], PointSelectedColor, 2f);
            }

            // ④ 序号牌
            _badgeRects.Clear();
            for (int i = 0; i < count; i++)
                _badgeRects.Add(DrawPointBadge(rect, frames[i], i, stays[i]));
        }

        /// <summary>起点绿 / 终点红 / 中间蓝（只有一个点时按起点算）</summary>
        Color PointColor(int index) =>
            index <= 0 ? PointStartColor
            : index >= _points.Count - 1 ? PointEndColor : PointMidColor;

        static GUIStyle _badgeStyle;

        /// <summary>
        /// 取景框左上角的序号牌。位置**强制夹在画布内**：zoom&lt;1 时取景框比画布还大、
        /// 镜头推到边角时整块框会移出画布，牌子跟着跑出去就等于没有编号可认了。
        /// </summary>
        Rect DrawPointBadge(Rect canvasRect, Rect frame, int index, bool stay)
        {
            if (_badgeStyle == null)
                _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                };

            bool selected = index == _list.index;
            string label = (index + 1).ToString() + (stay ? " 停" : "");
            float w = 12f + label.Length * 7.5f + (stay ? 6f : 0f);
            const float h = 15f;
            var r = new Rect(
                Mathf.Clamp(frame.x + 3f, canvasRect.x + 2f, canvasRect.xMax - w - 2f),
                Mathf.Clamp(frame.y + 3f, canvasRect.y + 2f, canvasRect.yMax - h - 2f),
                w, h);

            // 避让：zoom 相近的点取景框几乎重合，牌子会叠成一摞谁也看不见。
            // 撞上就往下挪一格，挪到画布底就放弃（宁可叠着也不能跑出画布）
            for (int guard = 0; guard < 8; guard++)
            {
                bool hit = false;
                foreach (var taken in _badgeRects)
                    if (taken.Overlaps(r)) { hit = true; break; }
                if (!hit) break;
                if (r.yMax + h + 3f > canvasRect.yMax) break;
                r.y += h + 3f;
            }

            // 投影：亮背景（雪景/白墙）上没有它牌子会糊进去
            EditorGUI.DrawRect(new Rect(r.x + 1f, r.y + 1f, r.width, r.height),
                new Color(0f, 0f, 0f, 0.45f));
            EditorGUI.DrawRect(r, selected ? PointSelectedColor : PointColor(index));
            if (selected) DrawRectOutline(r, Color.white, 1f);

            var prevColor = _badgeStyle.normal.textColor;
            _badgeStyle.normal.textColor = selected ? Color.black : Color.white; // 黄底白字会糊
            GUI.Label(r, label, _badgeStyle);
            _badgeStyle.normal.textColor = prevColor;
            return r;
        }

        /// <summary>两点之间的流向：虚线 + 中点箭头（箭头才是「从哪到哪」的答案）</summary>
        void DrawFlowLine(Rect rect, Vector2 fromCanvas, Vector2 toCanvas)
        {
            DrawDottedLine(rect, fromCanvas, toCanvas, new Color(1f, 1f, 1f, 0.4f));
            if (Event.current.type != EventType.Repaint) return;   // Handles 只在重绘时有效

            var a = CanvasToGui(rect, fromCanvas);
            var b = CanvasToGui(rect, toCanvas);
            var dir = b - a;
            if (dir.sqrMagnitude < 100f) return;   // 两点几乎重合：箭头没有方向可言
            dir.Normalize();
            var mid = (a + b) * 0.5f;
            var side = new Vector2(-dir.y, dir.x);

            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.85f);
            Handles.DrawAAConvexPolygon(
                (Vector3)(mid + dir * 8f),
                (Vector3)(mid - dir * 4f + side * 5f),
                (Vector3)(mid - dir * 4f - side * 5f));
            Handles.EndGUI();
        }

        /// <summary>虚线矩形（stay 点专用：与上一个点重合，实线画出来分不清是谁）</summary>
        static void DrawDashedRect(Rect r, Color c, float t)
        {
            const float dash = 6f, gap = 4f;
            for (float x = r.x; x < r.xMax; x += dash + gap)
            {
                float w = Mathf.Min(dash, r.xMax - x);
                EditorGUI.DrawRect(new Rect(x, r.y, w, t), c);
                EditorGUI.DrawRect(new Rect(x, r.yMax - t, w, t), c);
            }
            for (float y = r.y; y < r.yMax; y += dash + gap)
            {
                float hh = Mathf.Min(dash, r.yMax - y);
                EditorGUI.DrawRect(new Rect(r.x, y, t, hh), c);
                EditorGUI.DrawRect(new Rect(r.xMax - t, y, t, hh), c);
            }
        }

        static Rect Inset(Rect r, float d) => new Rect(
            r.x + d, r.y + d, Mathf.Max(6f, r.width - d * 2f), Mathf.Max(6f, r.height - d * 2f));

        /// <summary>画布坐标 → 运镜变换后的坐标（与运行时 ZoomRoot 的缩放+平移同构）</summary>
        static Vector2 ViewPoint(CamState view, Vector2 p) => p * view.zoom + view.offset;

        void DrawStageCharacters(Rect rect, CamState view)
        {
            var info = RowStage;
            var stage = Object.FindFirstObjectByType<VNStage>();
            bool drew = false;

            if (_showPortraits && info != null && info.characters.Count > 0)
            {
                foreach (var c in info.characters)
                {
                    if (c.sprite == null) continue;

                    var def = FindCharDef(c.id, stage);
                    float height = CharacterHeight(stage, def);
                    Vector2 offset = def != null ? def.positionOffset : Vector2.zero;
                    // 立绘 RectTransform 的锚点位置就是中心（与 VNStage.SlotPosition 一致）
                    var center = new Vector2(SlotX[Mathf.Clamp(c.slot, 0, 2)], -60f) + offset;
                    float aspect = c.sprite.rect.width / Mathf.Max(1f, c.sprite.rect.height);
                    var half = new Vector2(height * aspect * 0.5f, height * 0.5f);
                    // 位置跟容器走（立绘在 ZoomRoot 底下），尺寸再乘 mode 给的额外倍率
                    DrawSpriteAt(rect, ViewPoint(view, center),
                        half * view.zoom * view.CharScale, c.sprite);
                    drew = true;
                }
            }

            if (drew) return;
            for (int s = 0; s < 3; s++)
            {
                var p = CanvasToGui(rect, ViewPoint(view, new Vector2(SlotX[s], -60f)));
                float cs = view.zoom * view.CharScale;
                float hw = 880f * 0.28f * cs * rect.width / 1920f;
                float hh = 880f * 0.5f * cs * rect.height / 1080f;
                EditorGUI.DrawRect(new Rect(p.x - hw * 0.5f, p.y - hh, hw, hh * 2f),
                    new Color(1f, 1f, 1f, 0.05f));
            }
        }

        /// <summary>按画布坐标把 sprite 画到指定矩形（图集 sprite 走 textureRect 取 UV）</summary>
        static void DrawSpriteAt(Rect canvasRect, Vector2 center, Vector2 half, Sprite sprite)
        {
            var tl = CanvasToGui(canvasRect, new Vector2(center.x - half.x, center.y + half.y));
            var br = CanvasToGui(canvasRect, new Vector2(center.x + half.x, center.y - half.y));
            var dst = Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
            // 放大后完全移出画布的（镜头推到别处）直接跳过，省掉无意义的绘制
            if (dst.xMax < canvasRect.x || dst.x > canvasRect.xMax ||
                dst.yMax < canvasRect.y || dst.y > canvasRect.yMax) return;
            DrawSpriteRaw(dst, sprite, true);
        }

        static void DrawSpriteRaw(Rect dst, Sprite sprite, bool alphaBlend)
        {
            var texture = sprite.texture;
            if (texture == null) return;
            // 图集里的 sprite 不能整张 texture 当图用，必须按 textureRect 取 UV
            var source = sprite.textureRect;
            var uv = new Rect(
                source.x / texture.width, source.y / texture.height,
                source.width / texture.width, source.height / texture.height);
            GUI.DrawTextureWithTexCoords(dst, texture, uv, alphaBlend);
        }

        void HandleCanvasInput(Rect rect)
        {
            var e = Event.current;

            // 光标是「现在能不能移动」的唯一提示，所以 Ctrl 一按下就得立刻换形状
            if ((e.type == EventType.KeyDown || e.type == EventType.KeyUp) &&
                (e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl))
                Repaint();

            DrawCanvasCursors(rect, e.control);

            if (e.type == EventType.MouseUp)
            {
                _dragMode = DragMode.None;
                return;
            }
            if (!rect.Contains(e.mousePosition)) return;

            bool hasSelection = _list.index >= 0 && _list.index < _points.Count;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var click = GuiToCanvas(rect, e.mousePosition);

                // 0) 序号牌命中 → 直接选中那个点。排在最前面：框重叠时按中心距离
                //    找最近点会选错人，而牌子是画布上唯一一定不重叠、一定在画布内的把手
                for (int i = 0; i < _badgeRects.Count && i < _points.Count; i++)
                {
                    if (!_badgeRects[i].Contains(e.mousePosition)) continue;
                    _list.index = i;
                    _dragMode = DragMode.None;   // 牌子只用来选，拖动仍走取景框
                    e.Use();
                    Repaint();
                    return;
                }

                // 1) 选中路径点的取景框四角（GUI 12px 内）→ 拖角改 zoom
                if (hasSelection && _points[_list.index].type != PointType.Stay)
                {
                    var st = TargetState(_list.index);
                    var center = -st.offset / st.zoom;
                    var half = CanvasHalf / st.zoom;
                    for (int cx = -1; cx <= 1; cx += 2)
                    for (int cy = -1; cy <= 1; cy += 2)
                    {
                        var cornerGui = CanvasToGui(rect,
                            center + new Vector2(half.x * cx, half.y * cy));
                        if (Vector2.Distance(cornerGui, e.mousePosition) < 12f)
                        {
                            _dragMode = DragMode.Corner;
                            e.Use();
                            return;
                        }
                    }
                }

                // 2) 任意取景中心 60 画布单位内 → 选中该点并可拖动
                int nearest = -1;
                float best = 60f;
                for (int i = 0; i < _points.Count; i++)
                {
                    if (_points[i].type == PointType.Stay) continue;  // 画布上没有它的框
                    var st = TargetState(i);
                    float d = Vector2.Distance(-st.offset / st.zoom, click);
                    if (d < best) { best = d; nearest = i; }
                }
                if (nearest >= 0)
                {
                    _list.index = nearest;
                    // 误触保护：裸拖只选中，**Ctrl + 拖**才移动点位。
                    // 画布上最常做的事是「选中某个点看它的取景」，而按在框上稍微一晃
                    // 就把调好的位置推走了 —— 代价太不对等，所以移动要多按一个键
                    _dragMode = e.control ? DragMode.Center : DragMode.None;
                }
                // 空白处点击不再有任何效果 —— 以前是「点一下就把选中点瞬移过去」，
                // 连拖都不用拖，误伤率最高的一条。移动点位现在只有 Ctrl+拖这一条路
                e.Use();
                Repaint();
            }
            // _dragMode 的判断不能省：拖分隔条时鼠标会扫进画布，这里无条件 Use
            // 就会把分隔条的拖动事件吃掉，分隔条直接卡住不动
            else if (e.type == EventType.MouseDrag && e.button == 0 &&
                     _dragMode != DragMode.None && hasSelection &&
                     _points[_list.index].type != PointType.Stay)
            {
                var w = _points[_list.index];
                if (_dragMode == DragMode.Corner)
                {
                    // 拖角改 zoom：以取景中心为基准，指针到中心的距离 = 新的取景半宽/半高
                    var st = TargetState(w);
                    var center = -st.offset / st.zoom;
                    var mouse = GuiToCanvas(rect, e.mousePosition);
                    var half = new Vector2(
                        Mathf.Max(20f, Mathf.Abs(mouse.x - center.x)),
                        Mathf.Max(12f, Mathf.Abs(mouse.y - center.y)));
                    float zoom = Mathf.Max(CanvasHalf.x / half.x, CanvasHalf.y / half.y);
                    w.zoom = Mathf.Clamp(zoom, 0.5f, 3f);
                }
                else if (_dragMode == DragMode.Center)
                {
                    w.type = PointType.Coords;
                    w.coords = Round(GuiToCanvas(rect, e.mousePosition));
                }
                e.Use();
                Repaint();
            }
        }

        /// <summary>
        /// 画布上的光标提示。移动点位要按住 Ctrl，而这个键唯一的提示就是光标：
        /// 按住 Ctrl 时整块画布变四向移动箭头（松开就恢复，一眼知道当前处于哪种模式）。
        /// 四角的 zoom 热区不挑修饰键，所以一直是斜向拉伸光标，并且**注册在后面**
        /// —— 同一处叠了多个 cursorRect 时，后注册的那个赢。
        /// </summary>
        void DrawCanvasCursors(Rect rect, bool ctrl)
        {
            if (ctrl) EditorGUIUtility.AddCursorRect(rect, MouseCursor.MoveArrow);

            int sel = _list != null ? _list.index : -1;
            if (sel < 0 || sel >= _points.Count || _points[sel].type == PointType.Stay) return;

            var st = TargetState(sel);
            var center = -st.offset / st.zoom;
            var half = CanvasHalf / st.zoom;
            for (int cx = -1; cx <= 1; cx += 2)
            for (int cy = -1; cy <= 1; cy += 2)
            {
                var g = CanvasToGui(rect, center + new Vector2(half.x * cx, half.y * cy));
                var hot = RectIntersect(new Rect(g.x - 10f, g.y - 10f, 20f, 20f), rect);
                if (hot.width < 2f || hot.height < 2f) continue;   // 角在画布外，别乱设光标
                EditorGUIUtility.AddCursorRect(hot,
                    cx * cy > 0 ? MouseCursor.ResizeUpRight : MouseCursor.ResizeUpLeft);
            }
        }

        static Rect RectIntersect(Rect a, Rect b)
        {
            float x1 = Mathf.Max(a.x, b.x), y1 = Mathf.Max(a.y, b.y);
            float x2 = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
            return new Rect(x1, y1, Mathf.Max(0f, x2 - x1), Mathf.Max(0f, y2 - y1));
        }
    }
}
