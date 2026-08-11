using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 程序化球场与 HUD。**不是 MonoBehaviour**——由 VNBadmintonModule 构造并持有，
    /// 只负责「画」和「播 HUD 动画」，一条玩法逻辑都不放。
    ///
    /// 坐标系：所有内容挂在 Root 下，Root 锚在画布**底边中心**，
    /// 于是子物体的 anchoredPosition 与《羽毛球小游戏实施计划.md》第四节换算表一一对应。
    ///
    /// 层序（子物体顺序 = 绘制顺序）：
    ///   背景天空 → 广告横幅 → 沙地 → 绿色球场+场地线 → 演员层(影子/角色/球/轨迹点)
    ///   → 球网 → HUD(记分板/提示横幅/操作提示)
    /// 球网画在演员层之上，与参考截图一致（网柱压住球场）。
    /// </summary>
    public class VNBadmintonCourt
    {
        // ── 布局常量（field 坐标；由参考截图逐像素量出并折算到 1920×1080）──
        const float SkyBottomY = 403f;
        const float BannerTopY = 403f;
        const float BannerBottomY = 315f;
        const float SandTopY = 315f;
        const float CourtBottomY = 75f;
        const float CourtTopY = 308f;
        const float CourtBottomHalfW = 940f;
        const float CourtTopHalfW = 720f;
        const float NetBaseY = 61f;
        const float ScoreBoardY = 930f;
        const float TipsY = 620f;

        static readonly Color SkyColor = new Color(0.53f, 0.78f, 0.94f, 1f);
        static readonly Color SkyLowColor = new Color(0.80f, 0.90f, 0.96f, 1f);
        static readonly Color SandColor = new Color(0.86f, 0.78f, 0.60f, 1f);
        static readonly Color CourtColor = new Color(0.16f, 0.45f, 0.38f, 1f);
        static readonly Color LineColor = new Color(0.95f, 0.97f, 0.96f, 0.92f);
        static readonly Color NetColor = new Color(1f, 1f, 1f, 0.75f);
        static readonly Color PoleColor = new Color(0.12f, 0.12f, 0.14f, 1f);
        static readonly Color BannerLeft = new Color(0.20f, 0.42f, 0.82f, 1f);
        static readonly Color BannerRight = new Color(0.90f, 0.26f, 0.36f, 1f);
        static readonly Color BannerPale = new Color(0.97f, 0.97f, 0.98f, 1f);
        static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.14f, 0.92f);
        static readonly Color ScoreColor = new Color(1f, 0.88f, 0.45f, 1f);

        public RectTransform Root { get; private set; }
        /// <summary>影子 / 角色 / 球 / 轨迹虚点都挂这一层</summary>
        public RectTransform ActorLayer { get; private set; }

        RectTransform _scoreBoard, _tipsBox;
        TextMeshProUGUI _scoreText, _leftName, _rightName, _tipsText, _hintText, _goalText;
        Sequence _tipsSeq;
        GameObject _linkTarget;

        /// <param name="parent">模块自身的 RectTransform（已铺满画布）</param>
        /// <param name="backdrop">远景底图；null = 用程序化渐变天空</param>
        public void Build(RectTransform parent, VNBadmintonTuning t, Sprite backdrop,
            GameObject linkTarget)
        {
            _linkTarget = linkTarget;

            Root = VNBadmintonUi.CreateNode("Field", parent);
            Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 0f);
            Root.pivot = new Vector2(0.5f, 0f);
            Root.anchoredPosition = Vector2.zero;
            Root.sizeDelta = Vector2.zero;

            BuildSky(backdrop);
            BuildBanners();
            BuildGround();
            BuildCourt(t);

            ActorLayer = VNBadmintonUi.CreateNode("Actors", Root);
            VNBadmintonUi.AnchorBottomCenter(ActorLayer);
            ActorLayer.anchoredPosition = Vector2.zero;
            ActorLayer.sizeDelta = Vector2.zero;
            ActorLayer.pivot = new Vector2(0.5f, 0f);

            BuildNet(t);
            BuildHud();
        }

        // ------------------------------------------------------------------
        // 场景
        // ------------------------------------------------------------------

        void BuildSky(Sprite backdrop)
        {
            if (backdrop != null)
            {
                var bg = VNBadmintonUi.CreateImage("Backdrop", Root, backdrop, Color.white);
                bg.anchorMin = bg.anchorMax = new Vector2(0.5f, 0f);
                bg.pivot = new Vector2(0.5f, 0f);
                bg.anchoredPosition = new Vector2(0f, SkyBottomY);
                bg.sizeDelta = new Vector2(1920f, 1080f - SkyBottomY);
                bg.GetComponent<Image>().preserveAspect = false;
                return;
            }

            // 程序化渐变天空：上深下浅两块叠出层次
            var sky = VNBadmintonUi.CreateQuad("Sky", Root, SkyColor);
            var skyRect = VNBadmintonUi.Rect(sky);
            VNBadmintonUi.AnchorBottomCenter(skyRect);
            skyRect.anchoredPosition = Vector2.zero;
            sky.SetCorners(new Vector2(-960f, SkyBottomY), new Vector2(960f, SkyBottomY),
                           new Vector2(960f, 1080f), new Vector2(-960f, 1080f));

            var haze = VNBadmintonUi.CreateQuad("SkyHaze", Root,
                new Color(SkyLowColor.r, SkyLowColor.g, SkyLowColor.b, 0.85f));
            var hazeRect = VNBadmintonUi.Rect(haze);
            VNBadmintonUi.AnchorBottomCenter(hazeRect);
            hazeRect.anchoredPosition = Vector2.zero;
            haze.SetCorners(new Vector2(-960f, SkyBottomY), new Vector2(960f, SkyBottomY),
                            new Vector2(960f, SkyBottomY + 220f),
                            new Vector2(-960f, SkyBottomY + 220f));
            // 让雾带下浓上淡
            haze.color = new Color(SkyLowColor.r, SkyLowColor.g, SkyLowColor.b, 0.55f);
        }

        void BuildBanners()
        {
            var band = VNBadmintonUi.CreateQuad("BannerBand", Root, BannerPale);
            var bandRect = VNBadmintonUi.Rect(band);
            VNBadmintonUi.AnchorBottomCenter(bandRect);
            bandRect.anchoredPosition = Vector2.zero;
            band.SetCorners(new Vector2(-960f, BannerBottomY), new Vector2(960f, BannerBottomY),
                            new Vector2(960f, BannerTopY), new Vector2(-960f, BannerTopY));

            // 左蓝 / 右红 两块主横幅
            MakeBanner("BannerL", -430f, BannerLeft, "BADMINTON");
            MakeBanner("BannerR", 430f, BannerRight, "BADMINTON");
            MakeBanner("BannerLL", -800f, BannerPale, "PLAY SPORTS", BannerLeft);
            MakeBanner("BannerRR", 800f, BannerPale, "PLAY SPORTS", BannerRight);
        }

        void MakeBanner(string name, float x, Color color, string label, Color? textColor = null)
        {
            var rect = VNBadmintonUi.CreateImage(name, Root, null, color);
            VNBadmintonUi.AnchorBottomCenter(rect);
            rect.anchoredPosition = new Vector2(x, (BannerBottomY + BannerTopY) * 0.5f);
            rect.sizeDelta = new Vector2(300f, BannerTopY - BannerBottomY - 8f);

            var text = VNBadmintonUi.CreateText(name + "Txt", rect, 34,
                textColor ?? Color.white, label);
            var tr = VNBadmintonUi.Rect(text);
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
            tr.anchoredPosition = Vector2.zero;
            tr.sizeDelta = new Vector2(290f, 50f);
        }

        void BuildGround()
        {
            var sand = VNBadmintonUi.CreateQuad("Sand", Root, SandColor);
            var rect = VNBadmintonUi.Rect(sand);
            VNBadmintonUi.AnchorBottomCenter(rect);
            rect.anchoredPosition = Vector2.zero;
            sand.SetCorners(new Vector2(-960f, 0f), new Vector2(960f, 0f),
                            new Vector2(960f, SandTopY), new Vector2(-960f, SandTopY));
        }

        /// <summary>球场半宽随高度线性收敛，做出透视</summary>
        static float HalfWidthAt(float y)
        {
            float k = Mathf.InverseLerp(CourtBottomY, CourtTopY, y);
            return Mathf.Lerp(CourtBottomHalfW, CourtTopHalfW, k);
        }

        void BuildCourt(VNBadmintonTuning t)
        {
            var court = VNBadmintonUi.CreateQuad("Court", Root, CourtColor);
            var rect = VNBadmintonUi.Rect(court);
            VNBadmintonUi.AnchorBottomCenter(rect);
            rect.anchoredPosition = Vector2.zero;
            court.SetCorners(
                new Vector2(-CourtBottomHalfW, CourtBottomY),
                new Vector2(CourtBottomHalfW, CourtBottomY),
                new Vector2(CourtTopHalfW, CourtTopY),
                new Vector2(-CourtTopHalfW, CourtTopY));

            var lines = VNBadmintonUi.CreateNode("Lines", Root);
            VNBadmintonUi.AnchorBottomCenter(lines);
            lines.anchoredPosition = Vector2.zero;
            lines.sizeDelta = Vector2.zero;

            // 底线 / 顶线
            Line(lines, -CourtBottomHalfW, CourtBottomY, CourtBottomHalfW, CourtBottomY, 5f);
            Line(lines, -CourtTopHalfW, CourtTopY, CourtTopHalfW, CourtTopY, 4f);
            // 双打边线（梯形斜边）
            Line(lines, -CourtBottomHalfW, CourtBottomY, -CourtTopHalfW, CourtTopY, 5f);
            Line(lines, CourtBottomHalfW, CourtBottomY, CourtTopHalfW, CourtTopY, 5f);
            // 单打边线
            Line(lines, -CourtBottomHalfW * 0.92f, CourtBottomY, -CourtTopHalfW * 0.92f, CourtTopY, 4f);
            Line(lines, CourtBottomHalfW * 0.92f, CourtBottomY, CourtTopHalfW * 0.92f, CourtTopY, 4f);
            // 前后发球线
            foreach (float y in new[] { 150f, 250f })
                Line(lines, -HalfWidthAt(y), y, HalfWidthAt(y), y, 4f);
            // 中线（左右两半各一段，避开球网位置）
            Line(lines, -HalfWidthAt(CourtBottomY) * 0.5f, CourtBottomY,
                        -HalfWidthAt(CourtTopY) * 0.5f, CourtTopY, 3f);
            Line(lines, HalfWidthAt(CourtBottomY) * 0.5f, CourtBottomY,
                        HalfWidthAt(CourtTopY) * 0.5f, CourtTopY, 3f);
        }

        void Line(RectTransform parent, float x0, float y0, float x1, float y1, float thickness)
            => VNBadmintonUi.CreateLine("L", parent, new Vector2(x0, y0), new Vector2(x1, y1),
                thickness, LineColor);

        void BuildNet(VNBadmintonTuning t)
        {
            var net = VNBadmintonUi.CreateNode("Net", Root);
            VNBadmintonUi.AnchorBottomCenter(net);
            net.anchoredPosition = Vector2.zero;
            net.sizeDelta = Vector2.zero;

            // 网柱
            var pole = VNBadmintonUi.CreateImage("Pole", net, null, PoleColor);
            VNBadmintonUi.AnchorBottomCenter(pole);
            pole.pivot = new Vector2(0.5f, 0f);
            pole.anchoredPosition = new Vector2(0f, NetBaseY);
            pole.sizeDelta = new Vector2(9f, t.netTopY - NetBaseY);

            // 底座
            var foot = VNBadmintonUi.CreateImage("PoleFoot", net, null, PoleColor);
            VNBadmintonUi.AnchorBottomCenter(foot);
            foot.anchoredPosition = new Vector2(0f, NetBaseY);
            foot.sizeDelta = new Vector2(64f, 14f);

            // 网面：极淡的底 + 几根网线，别做成一块灰盒子（实心半透明会把球场压掉一层）
            float meshW = 112f;
            float meshTop = t.netTopY;
            float meshBottom = NetBaseY + 78f;

            var mesh = VNBadmintonUi.CreateImage("Mesh", net, null,
                new Color(NetColor.r, NetColor.g, NetColor.b, 0.10f));
            VNBadmintonUi.AnchorBottomCenter(mesh);
            mesh.pivot = new Vector2(0.5f, 1f);
            mesh.anchoredPosition = new Vector2(0f, meshTop);
            mesh.sizeDelta = new Vector2(meshW, meshTop - meshBottom);

            var netLines = new Color(1f, 1f, 1f, 0.28f);
            for (int i = 0; i <= 4; i++)   // 竖线
            {
                float x = Mathf.Lerp(-meshW * 0.5f, meshW * 0.5f, i / 4f);
                VNBadmintonUi.CreateLine("NV", net, new Vector2(x, meshBottom),
                    new Vector2(x, meshTop), 2f, netLines);
            }
            for (int i = 0; i <= 6; i++)   // 横线
            {
                float y = Mathf.Lerp(meshBottom, meshTop, i / 6f);
                VNBadmintonUi.CreateLine("NH", net, new Vector2(-meshW * 0.5f, y),
                    new Vector2(meshW * 0.5f, y), 2f, netLines);
            }

            var tape = VNBadmintonUi.CreateImage("Tape", net, null, Color.white);
            VNBadmintonUi.AnchorBottomCenter(tape);
            tape.anchoredPosition = new Vector2(0f, t.netTopY);
            tape.sizeDelta = new Vector2(meshW + 8f, 10f);
        }

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------

        void BuildHud()
        {
            var hud = VNBadmintonUi.CreateNode("Hud", Root);
            VNBadmintonUi.AnchorBottomCenter(hud);
            hud.anchoredPosition = Vector2.zero;
            hud.sizeDelta = Vector2.zero;

            // 记分板
            _scoreBoard = VNBadmintonUi.CreateImage("ScoreBoard", hud,
                VNProceduralTextures.RoundedRectSprite, PanelColor);
            _scoreBoard.GetComponent<Image>().type = Image.Type.Sliced;
            VNBadmintonUi.AnchorBottomCenter(_scoreBoard);
            _scoreBoard.anchoredPosition = new Vector2(0f, ScoreBoardY);
            _scoreBoard.sizeDelta = new Vector2(720f, 148f);

            // 名字与比分同一行（0.62），副标题独占下面一行（0.16），两行不能挤在一起
            _leftName = VNBadmintonUi.CreateText("LeftName", _scoreBoard, 34,
                new Color(0.62f, 0.80f, 1f, 1f), "");
            var ln = VNBadmintonUi.Rect(_leftName);
            ln.anchorMin = ln.anchorMax = new Vector2(0.2f, 0.62f);
            ln.anchoredPosition = Vector2.zero;
            ln.sizeDelta = new Vector2(260f, 60f);

            _rightName = VNBadmintonUi.CreateText("RightName", _scoreBoard, 34,
                new Color(1f, 0.68f, 0.70f, 1f), "");
            var rn = VNBadmintonUi.Rect(_rightName);
            rn.anchorMin = rn.anchorMax = new Vector2(0.8f, 0.62f);
            rn.anchoredPosition = Vector2.zero;
            rn.sizeDelta = new Vector2(260f, 60f);

            _scoreText = VNBadmintonUi.CreateText("Score", _scoreBoard, 62, ScoreColor, "0 - 0");
            var sc = VNBadmintonUi.Rect(_scoreText);
            sc.anchorMin = sc.anchorMax = new Vector2(0.5f, 0.62f);
            sc.anchoredPosition = Vector2.zero;
            sc.sizeDelta = new Vector2(260f, 90f);

            // 赛制副标题：**必须常驻**。开场横幅一闪而过，没有这行的话
            // 「自由练习不会结束」与「正式赛坏了不判胜负」在玩家眼里长得一模一样。
            _goalText = VNBadmintonUi.CreateText("Goal", _scoreBoard, 24,
                new Color(1f, 1f, 1f, 0.62f), "");
            var gr = VNBadmintonUi.Rect(_goalText);
            gr.anchorMin = gr.anchorMax = new Vector2(0.5f, 0.16f);
            gr.anchoredPosition = Vector2.zero;
            gr.sizeDelta = new Vector2(700f, 34f);

            // 提示横幅（得分 / 出界 / 赛点）
            _tipsBox = VNBadmintonUi.CreateImage("Tips", hud,
                VNProceduralTextures.RoundedRectSprite, PanelColor);
            _tipsBox.GetComponent<Image>().type = Image.Type.Sliced;
            VNBadmintonUi.AnchorBottomCenter(_tipsBox);
            _tipsBox.anchoredPosition = new Vector2(0f, TipsY);
            _tipsBox.sizeDelta = new Vector2(0f, 78f);
            _tipsBox.gameObject.SetActive(false);

            _tipsText = VNBadmintonUi.CreateText("TipsTxt", _tipsBox, 42, Color.white, "");
            var tt = VNBadmintonUi.Rect(_tipsText);
            tt.anchorMin = tt.anchorMax = new Vector2(0.5f, 0.5f);
            tt.anchoredPosition = Vector2.zero;
            tt.sizeDelta = new Vector2(680f, 60f);

            // 右下角操作提示（与参考截图同位置）
            _hintText = VNBadmintonUi.CreateText("Hint", hud, 26,
                new Color(1f, 1f, 1f, 0.72f), VNLocale.T("badminton.hint"),
                TextAlignmentOptions.Right);
            var hr = VNBadmintonUi.Rect(_hintText);
            VNBadmintonUi.AnchorBottomCenter(hr);
            hr.pivot = new Vector2(1f, 0f);
            hr.anchoredPosition = new Vector2(930f, 18f);
            hr.sizeDelta = new Vector2(760f, 40f);
        }

        /// <summary>右下角操作提示（正式赛 / 自由练习两套文案）</summary>
        public void SetHint(string text)
        {
            if (_hintText != null) _hintText.text = text;
        }

        /// <summary>记分板副标题：这一局要打到几分，还是根本没有终局</summary>
        public void SetGoal(bool freeMode, int target)
        {
            if (_goalText == null) return;
            _goalText.text = freeMode
                ? VNLocale.T("badminton.goalFree")
                : VNLocale.T("badminton.goal", target);
        }

        public void SetNames(string left, string right)
        {
            if (_leftName != null) _leftName.text = left;
            if (_rightName != null) _rightName.text = right;
        }

        public void SetScore(int mine, int theirs, bool punch)
        {
            if (_scoreText == null) return;
            _scoreText.text = $"{mine} - {theirs}";
            if (!punch) return;
            _scoreBoard.DOKill(true);
            _scoreBoard.DOPunchScale(Vector3.one * 0.08f, 0.35f, 8, 0.7f)
                       .SetUpdate(true).SetLink(_linkTarget);
        }

        /// <summary>记分板滑入/滑出（回合中收起、得分后落下，与参考实现一致）</summary>
        public void ShowScoreBoard(bool show)
        {
            if (_scoreBoard == null) return;
            _scoreBoard.DOKill();
            float y = show ? ScoreBoardY : ScoreBoardY + 220f;  // 收起时整块移出画面上沿
            _scoreBoard.DOAnchorPosY(y, 0.4f)
                       .SetEase(show ? Ease.OutBack : Ease.InBack)
                       .SetUpdate(true).SetLink(_linkTarget);
        }

        /// <summary>中央横幅：宽度 0→700 展开、停留、收回，收完回调</summary>
        public void ShowTips(string message, Action onDone = null)
        {
            if (_tipsBox == null) { onDone?.Invoke(); return; }

            _tipsSeq?.Kill();
            _tipsText.text = message;
            _tipsBox.gameObject.SetActive(true);
            _tipsBox.sizeDelta = new Vector2(0f, 78f);
            _tipsText.alpha = 0f;

            _tipsSeq = DOTween.Sequence();
            _tipsSeq.Append(_tipsBox.DOSizeDelta(new Vector2(700f, 78f), 0.25f));
            _tipsSeq.Append(_tipsText.DOFade(1f, 0.2f));
            _tipsSeq.AppendInterval(0.9f);
            _tipsSeq.Append(_tipsText.DOFade(0f, 0.2f));
            _tipsSeq.Append(_tipsBox.DOSizeDelta(new Vector2(0f, 78f), 0.2f));
            _tipsSeq.AppendCallback(() =>
            {
                _tipsBox.gameObject.SetActive(false);
                onDone?.Invoke();
            });
            _tipsSeq.SetUpdate(true).SetLink(_linkTarget);
        }

        public void Dispose() => _tipsSeq?.Kill();
    }
}
