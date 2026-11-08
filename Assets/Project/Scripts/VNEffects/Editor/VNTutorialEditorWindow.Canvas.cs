using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public partial class VNTutorialEditorWindow
    {
        // ------------------------------------------------------------------
        // 中：画布
        // ------------------------------------------------------------------

        struct HoleView
        {
            public bool has;
            public Rect norm;        // 归一化（左下原点），已含 padding
            public bool editable;    // 矩形模式才能拖
            public bool live;        // 来自 Play Mode 的锚点实际位置
            public string note;
        }

        void DrawCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.15f, 1f));
            Rect art = FitRect(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f),
                RefW / RefH);

            if (_backdrop != null) GUI.DrawTexture(art, _backdrop, ScaleMode.StretchToFill, false);
            else
            {
                EditorGUI.DrawRect(art, new Color(0.22f, 0.24f, 0.3f, 1f));
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 13 };
                GUI.Label(art, "没有底图：工具栏「底图 ▾」抓 Game 视图或选一张图\n（没有也能排版，只是看不到参照）", style);
            }

            _lastArt = art;
            var step = Current;
            if (step == null)
            {
                GUI.Label(new Rect(art.x, art.yMax - 20f, art.width, 18f), "左侧选一步来编辑",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var hole = ResolveHole(step);
            float scale = art.width / RefW;

            // ---- 暗幕（四块围出洞口；没洞就整块） ----
            var dim = new Color(0f, 0f, 0.01f, Mathf.Clamp01(_def.dim));
            if (hole.has)
            {
                Rect h = NormToGui(art, hole.norm);
                h = ClampToArt(art, h);
                EditorGUI.DrawRect(Rect.MinMaxRect(art.x, art.y, art.xMax, h.y), dim);
                EditorGUI.DrawRect(Rect.MinMaxRect(art.x, h.yMax, art.xMax, art.yMax), dim);
                EditorGUI.DrawRect(Rect.MinMaxRect(art.x, h.y, h.x, h.yMax), dim);
                EditorGUI.DrawRect(Rect.MinMaxRect(h.xMax, h.y, art.xMax, h.yMax), dim);

                // 描边（HDR 颜色钳到 0~1 显示）
                var edge = _def.edgeColor;
                edge = new Color(Mathf.Clamp01(edge.r), Mathf.Clamp01(edge.g), Mathf.Clamp01(edge.b), 1f);
                float width = Mathf.Max(1.5f, _def.edgeWidth * scale);
                if (step.shape == VNTutorialHole.Ellipse) DrawEllipse(h, edge, width);
                else DrawRoundedRect(h, Mathf.Min(step.corner * scale, Mathf.Min(h.width, h.height) * 0.5f), edge, width);

                if (hole.editable)
                {
                    Handles.color = Color.yellow;
                    Handles.DrawAAPolyLine(1.5f,
                        new Vector3(h.x, h.y), new Vector3(h.xMax, h.y),
                        new Vector3(h.xMax, h.yMax), new Vector3(h.x, h.yMax), new Vector3(h.x, h.y));
                    EditorGUI.DrawRect(SizeHandleRect(h), Color.yellow);
                }
                else if (hole.live)
                {
                    GUI.Label(new Rect(h.x, h.y - 18f, 300f, 16f), "● 锚点实际位置（只读）",
                        MiniLabel(new Color(0.6f, 1f, 0.6f)));
                }
            }
            else EditorGUI.DrawRect(art, dim);

            // ---- 卡片模拟 ----
            if (_showCard) DrawCardMock(art, step, hole, scale);

            if (!string.IsNullOrEmpty(hole.note))
                GUI.Label(new Rect(art.x + 6f, art.yMax - 20f, art.width - 12f, 18f), hole.note,
                    MiniLabel(new Color(1f, 0.9f, 0.6f)));

            HandleCanvasInput(art, step, hole);
        }

        HoleView ResolveHole(VNTutorialStep step)
        {
            var v = new HoleView();
            float padU = step.padding / RefW, padV = step.padding / RefH;

            if (!string.IsNullOrEmpty(step.anchor))
            {
                RectTransform target = EditorApplication.isPlaying ? VNTutorialAnchors.Get(step.anchor) : null;
                if (target != null)
                {
                    v.has = true;
                    v.live = true;
                    v.norm = Pad(VNTutorialPicker.NormalizedRect(target), padU, padV);
                }
                else if (step.HasArea)
                {
                    v.has = true;
                    v.norm = Pad(step.area, padU, padV);
                    v.note = EditorApplication.isPlaying
                        ? $"锚点「{step.anchor}」当前没登记（那个界面还没打开？），显示的是兜底矩形"
                        : $"锚点「{step.anchor}」要进 Play Mode 才看得到实际位置，现在显示兜底矩形";
                }
                else
                {
                    v.note = EditorApplication.isPlaying
                        ? $"锚点「{step.anchor}」当前没登记（先在游戏里打开那个界面）"
                        : $"锚点「{step.anchor}」：进 Play Mode 后显示实际位置";
                }
                return v;
            }

            if (step.HasArea)
            {
                v.has = true;
                v.editable = true;
                v.norm = Pad(step.area, padU, padV);
            }
            else v.note = "整屏压暗：在画布上拖一个框就能变成矩形洞";
            return v;
        }

        static Rect Pad(Rect r, float padU, float padV) =>
            Rect.MinMaxRect(r.xMin - padU, r.yMin - padV, r.xMax + padU, r.yMax + padV);

        void DrawCardMock(Rect art, VNTutorialStep step, HoleView hole, float scale)
        {
            // 落位规则照抄 VNTutorialPlayer.PlaceCard：Auto 躲开洞（洞在上半屏就放下半屏）
            var spot = step.card;
            if (spot == VNTutorialCardSpot.Auto)
                spot = hole.has
                    ? (hole.norm.center.y > 0.5f ? VNTutorialCardSpot.Bottom : VNTutorialCardSpot.Top)
                    : VNTutorialCardSpot.Center;
            float cx = art.center.x, cy = art.center.y;
            if (spot == VNTutorialCardSpot.Top) cy = art.center.y - art.height * 0.28f;
            else if (spot == VNTutorialCardSpot.Bottom) cy = art.center.y + art.height * 0.28f;
            else if (spot == VNTutorialCardSpot.Custom)
            {
                // 与运行时同义：cardPos = 卡片中心、归一化、左下原点
                cx = art.x + step.cardPos.x * art.width;
                cy = art.y + (1f - step.cardPos.y) * art.height;
            }

            // 尺寸：宽度（步骤覆盖 → 整篇默认 → 780）× 整体缩放，与运行时 ApplyCardSize 同规则；
            // 之后所有 px 数值都乘 scale（画布缩放 × 卡片缩放），字号边距配图一起变
            float cardW = _def.ResolveCardWidth(step);
            if (cardW <= 0f) cardW = CardWidth;
            scale *= _def.ResolveCardScale(step);
            float w = cardW * scale;
            float padX = 38f * scale, padTop = 30f * scale, padBottom = 26f * scale, spacing = 14f * scale;
            float inner = w - padX * 2f;

            string title = Pick(step.title, step.titleEn, step.titleJa);
            string body = Pick(step.body, step.bodyEn, step.bodyJa);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = Mathf.Max(8, Mathf.RoundToInt(36f * scale)), wordWrap = true };
            titleStyle.normal.textColor = new Color(1f, 0.93f, 0.72f);
            var bodyStyle = new GUIStyle(EditorStyles.label)
            { fontSize = Mathf.Max(7, Mathf.RoundToInt(27f * scale)), wordWrap = true, richText = true };
            bodyStyle.normal.textColor = new Color(0.92f, 0.93f, 0.97f);
            var footStyle = new GUIStyle(EditorStyles.miniLabel)
            { fontSize = Mathf.Max(6, Mathf.RoundToInt(21f * scale)) };

            float titleH = string.IsNullOrEmpty(title) ? 0f : Mathf.Max(48f * scale, titleStyle.CalcHeight(new GUIContent(title), inner));
            float imageH = step.image != null ? Mathf.Max(40f, step.imageHeight) * scale : 0f;
            float bodyH = string.IsNullOrEmpty(body) ? 20f * scale : bodyStyle.CalcHeight(new GUIContent(body), inner);
            float footH = 34f * scale;
            float h = padTop + padBottom + footH + bodyH +
                      (titleH > 0f ? titleH + spacing : 0f) + (imageH > 0f ? imageH + spacing : 0f) + spacing;

            // 运行时会把卡片钳在屏内（PlaceCard），这里同样钳，所见即所得
            _cardHalfNorm = new Vector2(w * 0.5f / art.width, h * 0.5f / art.height);
            cx = Mathf.Clamp(cx, art.x + w * 0.5f, art.xMax - w * 0.5f);
            cy = Mathf.Clamp(cy, art.y + h * 0.5f, art.yMax - h * 0.5f);
            var card = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
            _cardMock = card;
            EditorGUI.DrawRect(card, new Color(0.07f, 0.08f, 0.13f, 0.96f));
            if (spot == VNTutorialCardSpot.Custom || _drag == DragKind.Card)
            {
                // 自定义位置：描一圈提示「这个能拖」
                Handles.color = new Color(1f, 0.85f, 0.4f, 0.9f);
                Handles.DrawAAPolyLine(1.5f,
                    new Vector3(card.x, card.y), new Vector3(card.xMax, card.y),
                    new Vector3(card.xMax, card.yMax), new Vector3(card.x, card.yMax), new Vector3(card.x, card.y));
            }
            EditorGUIUtility.AddCursorRect(card, MouseCursor.MoveArrow);
            // 右下角手柄：拖 = 改宽度，Shift+拖 = 改整体缩放（都写进这一步的覆盖值）
            var sizeHandle = SizeHandleRect(card);
            EditorGUI.DrawRect(sizeHandle, new Color(1f, 0.85f, 0.4f, 0.95f));
            EditorGUIUtility.AddCursorRect(sizeHandle, MouseCursor.ResizeUpLeft);
            if (_drag == DragKind.CardSize)
                GUI.Label(new Rect(card.x, card.yMax + 4f, 420f, 16f),
                    $"宽 {Mathf.RoundToInt(_def.ResolveCardWidth(step) > 0 ? _def.ResolveCardWidth(step) : CardWidth)}px  " +
                    $"× 缩放 {_def.ResolveCardScale(step):0.00}   （Shift+拖 = 改缩放）",
                    MiniLabel(new Color(1f, 0.9f, 0.6f)));

            float y = card.y + padTop;
            if (titleH > 0f)
            {
                GUI.Label(new Rect(card.x + padX, y, inner, titleH), title, titleStyle);
                y += titleH + spacing;
            }
            if (imageH > 0f)
            {
                var imgRect = new Rect(card.x + padX, y, inner, imageH);
                var tex = step.image.texture;
                if (tex != null)
                {
                    var tr = step.image.textureRect;
                    var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                    var fit = FitRect(imgRect, tr.width / Mathf.Max(1f, tr.height));
                    GUI.DrawTextureWithTexCoords(fit, tex, uv, true);
                }
                y += imageH + spacing;
            }
            GUI.Label(new Rect(card.x + padX, y, inner, bodyH), string.IsNullOrEmpty(body) ? "（正文）" : body, bodyStyle);

            float fy = card.yMax - padBottom - footH;
            int total = Mathf.Max(1, _def.steps.Count);
            footStyle.alignment = TextAnchor.MiddleLeft;
            footStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
            if (_def.allowSkip) GUI.Label(new Rect(card.x + padX, fy, inner, footH), "ESC 跳过教程", footStyle);
            footStyle.alignment = TextAnchor.MiddleCenter;
            footStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(card.x + padX, fy, inner, footH), $"{_selected + 1} / {total}", footStyle);
            footStyle.alignment = TextAnchor.MiddleRight;
            footStyle.normal.textColor = new Color(1f, 0.88f, 0.55f, 0.9f);
            GUI.Label(new Rect(card.x + padX, fy, inner, footH), _selected + 1 >= total ? "✓ 完成" : "▼ 点击继续", footStyle);
        }

        string Pick(string zh, string en, string ja)
        {
            // 与运行时 VNTutorialDef.Pick 同规则：En/Ja 留空回退中文
            switch (_lang)
            {
                case 1: return string.IsNullOrEmpty(en) ? zh : en;
                case 2: return string.IsNullOrEmpty(ja) ? zh : ja;
                default: return zh;
            }
        }

        void HandleCanvasInput(Rect art, VNTutorialStep step, HoleView hole)
        {
            var e = Event.current;
            bool inside = art.Contains(e.mousePosition);
            bool anchorMode = !string.IsNullOrEmpty(step.anchor);

            // ---- 卡片右下角手柄：拖 = 宽度，Shift+拖 = 整体缩放（写进这一步的覆盖值） ----
            if (_showCard && e.type == EventType.MouseDown && e.button == 0 && SizeHandleRect(_cardMock).Contains(e.mousePosition))
            {
                Snapshot();
                _drag = DragKind.CardSize;
                _dragStartMouse = e.mousePosition;
                _dragStartCardW = _def.ResolveCardWidth(step) > 0f ? _def.ResolveCardWidth(step) : CardWidth;
                _dragStartCardS = _def.ResolveCardScale(step);
                GUI.FocusControl(null);
                e.Use();
                return;
            }
            if (_drag == DragKind.CardSize)
            {
                if (e.type == EventType.MouseDrag)
                {
                    Vector2 d = e.mousePosition - _dragStartMouse;
                    float canvasScale = art.width / RefW;   // GUI px → 1920 基准 px
                    if (e.shift)
                    {
                        // 往右下拖 = 放大；每 200 基准 px 改 0.5 倍
                        float ds = (d.x + d.y) * 0.5f / canvasScale / 200f * 0.5f;
                        step.cardScale = Mathf.Clamp(_dragStartCardS + ds, 0.5f, 2.5f);
                    }
                    else
                    {
                        // 手柄在右下角、卡片以中心为锚：鼠标移动 dx 对应宽度变化 2·dx
                        float dw = d.x * 2f / canvasScale / _def.ResolveCardScale(step);
                        step.cardWidth = Mathf.Clamp(_dragStartCardW + dw, 320f, 1800f);
                    }
                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _drag = DragKind.None;
                    Save();
                    e.Use();
                }
                return;
            }

            // ---- 卡片拖动：画在最上层所以命中优先；任何落位模式下拖一下就切成 Custom ----
            if (_showCard && e.type == EventType.MouseDown && e.button == 0 && _cardMock.Contains(e.mousePosition))
            {
                Snapshot();
                _drag = DragKind.Card;
                _dragStartNorm = GuiToNorm(art, e.mousePosition);
                if (step.card != VNTutorialCardSpot.Custom)
                {
                    // 从当前画出来的位置起拖，卡片不会跳
                    step.card = VNTutorialCardSpot.Custom;
                    step.cardPos = GuiToNorm(art, _cardMock.center);
                }
                _dragStartCard = step.cardPos;
                GUI.FocusControl(null);
                e.Use();
                return;
            }
            if (_drag == DragKind.Card)
            {
                if (e.type == EventType.MouseDrag)
                {
                    Vector2 delta = GuiToNorm(art, e.mousePosition) - _dragStartNorm;
                    var p = _dragStartCard + delta;
                    p.x = Mathf.Clamp(p.x, _cardHalfNorm.x, 1f - _cardHalfNorm.x);
                    p.y = Mathf.Clamp(p.y, _cardHalfNorm.y, 1f - _cardHalfNorm.y);
                    step.cardPos = p;
                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _drag = DragKind.None;
                    Save();
                    e.Use();
                }
                return;
            }

            if (anchorMode) return;   // 锚点位置由运行时决定，画布上不可拖（卡片除外，见上）

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && inside:
                {
                    Vector2 norm = GuiToNorm(art, e.mousePosition);
                    if (hole.editable)
                    {
                        Rect h = NormToGui(art, hole.norm);
                        if (SizeHandleRect(h).Contains(e.mousePosition)) BeginDrag(DragKind.Size, norm, step);
                        else if (h.Contains(e.mousePosition)) BeginDrag(DragKind.Move, norm, step);
                        else BeginDrag(DragKind.Draw, norm, step);
                    }
                    else BeginDrag(DragKind.Draw, norm, step);
                    GUI.FocusControl(null);
                    e.Use();
                    break;
                }

                case EventType.MouseDrag when _drag != DragKind.None:
                {
                    Vector2 norm = GuiToNorm(art, e.mousePosition);
                    norm = new Vector2(Mathf.Clamp01(norm.x), Mathf.Clamp01(norm.y));
                    Vector2 delta = norm - _dragStartNorm;
                    switch (_drag)
                    {
                        case DragKind.Draw:
                            step.area = Rect.MinMaxRect(
                                Mathf.Min(_dragStartNorm.x, norm.x), Mathf.Min(_dragStartNorm.y, norm.y),
                                Mathf.Max(_dragStartNorm.x, norm.x), Mathf.Max(_dragStartNorm.y, norm.y));
                            break;
                        case DragKind.Move:
                        {
                            var r = _dragStartArea;
                            r.x = Mathf.Clamp(_dragStartArea.x + delta.x, 0f, 1f - r.width);
                            r.y = Mathf.Clamp(_dragStartArea.y + delta.y, 0f, 1f - r.height);
                            step.area = r;
                            break;
                        }
                        case DragKind.Size:
                        {
                            // 手柄在右下角：宽随 x 增，高随 y 减（y 向上为正）
                            var r = _dragStartArea;
                            float newW = Mathf.Max(0.02f, _dragStartArea.width + delta.x);
                            float newH = Mathf.Max(0.02f, _dragStartArea.height - delta.y);
                            r.y = _dragStartArea.yMax - newH;
                            r.width = newW;
                            r.height = newH;
                            step.area = r;
                            break;
                        }
                    }
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseUp when _drag != DragKind.None:
                    _drag = DragKind.None;
                    // 点一下没拖出面积 → 不算画框，把误产生的零矩形清掉
                    if (step.area.width < 0.005f || step.area.height < 0.005f) step.area = new Rect(0f, 0f, 0f, 0f);
                    Save();
                    e.Use();
                    break;
            }
        }

        void BeginDrag(DragKind kind, Vector2 norm, VNTutorialStep step)
        {
            Snapshot();
            _drag = kind;
            _dragStartNorm = norm;
            _dragStartArea = step.area;
            if (kind == DragKind.Draw) step.area = new Rect(norm.x, norm.y, 0f, 0f);
        }

        // ---- 坐标：归一化左下原点 ⇄ GUI 左上原点 ----

        static Rect NormToGui(Rect art, Rect n) =>
            new Rect(art.x + n.x * art.width,
                     art.y + (1f - n.yMax) * art.height,
                     n.width * art.width, n.height * art.height);

        static Vector2 GuiToNorm(Rect art, Vector2 g) =>
            new Vector2((g.x - art.x) / art.width, 1f - (g.y - art.y) / art.height);

        static Rect ClampToArt(Rect art, Rect r) =>
            Rect.MinMaxRect(Mathf.Clamp(r.xMin, art.xMin, art.xMax), Mathf.Clamp(r.yMin, art.yMin, art.yMax),
                            Mathf.Clamp(r.xMax, art.xMin, art.xMax), Mathf.Clamp(r.yMax, art.yMin, art.yMax));

        static Rect SizeHandleRect(Rect r) =>
            new Rect(r.xMax - HandleSize * 0.5f, r.yMax - HandleSize * 0.5f, HandleSize, HandleSize);

        static Rect FitRect(Rect area, float aspect)
        {
            float w = area.width, h = area.height;
            if (w / h > aspect) w = h * aspect; else h = w / aspect;
            return new Rect(area.center.x - w * 0.5f, area.center.y - h * 0.5f, w, h);
        }

        static void DrawEllipse(Rect r, Color color, float width)
        {
            const int Steps = 48;
            var pts = new Vector3[Steps + 1];
            for (int i = 0; i <= Steps; i++)
            {
                float a = i / (float)Steps * Mathf.PI * 2f;
                pts[i] = new Vector3(r.center.x + Mathf.Cos(a) * r.width * 0.5f,
                                     r.center.y + Mathf.Sin(a) * r.height * 0.5f);
            }
            Handles.color = color;
            Handles.DrawAAPolyLine(width, pts);
        }

        static void DrawRoundedRect(Rect r, float radius, Color color, float width)
        {
            radius = Mathf.Max(0f, radius);
            var pts = new List<Vector3>();
            void Arc(Vector2 c, float from)
            {
                const int N = 6;
                for (int i = 0; i <= N; i++)
                {
                    float a = (from + 90f * i / N) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius));
                }
            }
            // GUI 的 y 向下：从右下角开始顺时针
            Arc(new Vector2(r.xMax - radius, r.yMax - radius), 0f);
            Arc(new Vector2(r.x + radius, r.yMax - radius), 90f);
            Arc(new Vector2(r.x + radius, r.y + radius), 180f);
            Arc(new Vector2(r.xMax - radius, r.y + radius), 270f);
            pts.Add(pts[0]);
            Handles.color = color;
            Handles.DrawAAPolyLine(width, pts.ToArray());
        }

        static GUIStyle MiniLabel(Color color)
        {
            var s = new GUIStyle(EditorStyles.miniLabel);
            s.normal.textColor = color;
            return s;
        }
    }
}
