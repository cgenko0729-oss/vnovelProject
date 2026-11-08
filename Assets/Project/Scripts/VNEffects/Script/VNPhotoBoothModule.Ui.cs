using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    public partial class VNPhotoBoothModule
    {
        // ==================================================================
        // 界面搭建
        // ==================================================================

        void BuildUi()
        {
            var root = (RectTransform)transform;

            // 半透明背板：同时吃掉所有穿透到舞台的点击
            var backdrop = VNPhotoBoothUi.CreateImage("Backdrop", root, null,
                VNPhotoBoothUi.Backdrop, true);
            VNUi.Stretch((RectTransform)backdrop.transform);

            // 机身
            var machine = VNPhotoBoothUi.CreateImage("Machine", root,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.MachineBody, true);
            var machineRect = VNPhotoBoothUi.Center((RectTransform)machine.transform,
                new Vector2(MachineW, MachineH), Vector2.zero);

            var inner = VNPhotoBoothUi.CreateImage("Inner", machineRect,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.MachineInner);
            VNPhotoBoothUi.Center((RectTransform)inner.transform,
                new Vector2(MachineW - 60f, MachineH - 60f), Vector2.zero);

            _machine = machineRect;

            BuildTitleBar(machineRect);
            BuildViewFinder(machineRect);
            BuildLeftPanel(machineRect);
            BuildRightPanel(machineRect);
            BuildBottomBar(machineRect);
            BuildHelpPanel(machineRect);   // 最后建 = 展开时压在所有面板之上

            // 倒数数字：压在取景框中央（拍照那一刻会被藏起来，不会入镜）
            _countdownText = VNPhotoBoothUi.CreateText("Countdown", machineRect, 220,
                VNPhotoBoothUi.CountdownPink, "");
            _countdownText.fontStyle = FontStyles.Bold;
            _countdownText.outlineWidth = 0.22f;      // 白描边，压在任何背景上都看得清
            _countdownText.outlineColor = new Color32(255, 255, 255, 235);
            VNPhotoBoothUi.Center((RectTransform)_countdownText.transform,
                new Vector2(400f, 400f), new Vector2(0f, ViewY));
            _countdownText.gameObject.SetActive(false);

            // 闪光层：铺满整屏，快门瞬间白一下
            _flash = VNPhotoBoothUi.CreateImage("Flash", root, null,
                new Color(1f, 1f, 1f, 0f));
            VNUi.Stretch((RectTransform)_flash.transform);
            _flash.raycastTarget = false;
        }

        void BuildTitleBar(RectTransform parent)
        {
            var title = VNPhotoBoothUi.CreateText("Title", parent, 42,
                VNPhotoBoothUi.AccentSoft, _title, TextAlignmentOptions.Left);
            VNPhotoBoothUi.Center((RectTransform)title.transform, new Vector2(500f, 60f),
                new Vector2(-MachineW * 0.5f + 300f, TitleY));

            string themeLine = _freeMode
                ? VNLocale.T("photo.free")
                : (_theme.hint != null && !_theme.hint.Empty
                    ? _theme.hint.Display
                    : VNLocale.T("photo.theme", _theme.DisplayName));
            var hint = VNPhotoBoothUi.CreateText("ThemeHint", parent, 28,
                Color.white, themeLine, TextAlignmentOptions.Center);
            VNPhotoBoothUi.Center((RectTransform)hint.transform, new Vector2(700f, 50f),
                new Vector2(0f, TitleY));

            // 限时条（不限时就整条不建）。
            // 右上角那块归说明钮，所以限时条整体往左让出 100px。
            if (_timeLimit <= 0f) return;

            var barBg = VNPhotoBoothUi.CreateImage("TimerBg", parent,
                VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.18f));
            var barRect = VNPhotoBoothUi.Center((RectTransform)barBg.transform,
                new Vector2(360f, 20f), new Vector2(MachineW * 0.5f - 300f, TitleY - 6f));

            var fill = VNPhotoBoothUi.CreateImage("TimerFill", barRect,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.AccentSoft);
            _timerFill = (RectTransform)fill.transform;
            _timerFill.anchorMin = new Vector2(0f, 0f);
            _timerFill.anchorMax = new Vector2(0f, 1f);
            _timerFill.pivot = new Vector2(0f, 0.5f);
            _timerFill.offsetMin = Vector2.zero;
            _timerFill.offsetMax = Vector2.zero;
            _timerFill.sizeDelta = new Vector2(360f, 0f);

            // 秒数摆在条的左侧（右侧被说明钮占了），右对齐贴着条的左端
            _timerText = VNPhotoBoothUi.CreateText("TimerText", parent, 26, Color.white,
                Mathf.CeilToInt(_timeLeft).ToString(), TextAlignmentOptions.Right);
            VNPhotoBoothUi.Center((RectTransform)_timerText.transform, new Vector2(90f, 40f),
                new Vector2(MachineW * 0.5f - 537f, TitleY - 4f));
        }

        // ==================================================================
        // 操作说明（右上角「?」，点一下展开卡片）
        // ==================================================================

        /// <summary>
        /// 说明卡片整组（钮 + 卡片）。拍照时要整组藏掉——
        /// 卡片是往左下展开的，会盖住取景框右上角，不藏就会入镜。
        /// </summary>
        GameObject _helpRoot;
        CanvasGroup _helpCard;
        bool _helpOpen;

        void BuildHelpPanel(RectTransform parent)
        {
            var root = VNPhotoBoothUi.CreateNode("Help", parent);
            VNUi.Stretch(root);
            _helpRoot = root.gameObject;

            // ---- 折叠钮 ----
            var button = VNPhotoBoothUi.CreateImage("HelpButton", root,
                VNPhotoTextures.CircleSprite(), VNPhotoBoothUi.Accent, true);
            var buttonRect = VNPhotoBoothUi.Center((RectTransform)button.transform,
                new Vector2(54f, 54f), new Vector2(MachineW * 0.5f - 64f, TitleY));
            var icon = VNPhotoBoothUi.CreateText("HelpIcon", buttonRect, 34, Color.white, "?");
            VNUi.Stretch((RectTransform)icon.transform);

            var toggle = button.gameObject.AddComponent<Button>();
            toggle.targetGraphic = button;
            toggle.onClick.AddListener(ToggleHelp);

            // ---- 展开的卡片 ----
            // 底色必须**不透明**：它会盖在右栏的表情格上，留一点透明度就变成
            // 一层脏兮兮的滤镜，字反而看不清
            var card = VNPhotoBoothUi.CreateImage("HelpCard", root,
                VNProceduralTextures.RoundedRectSprite,
                new Color(0.13f, 0.13f, 0.17f, 1f), true);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(1f, 1f);      // 右上角钉在钮的下方，向左下展开

            var titleText = VNPhotoBoothUi.CreateText("HelpTitle", cardRect, 26,
                VNPhotoBoothUi.AccentSoft, VNLocale.T("photo.help.title"),
                TextAlignmentOptions.TopLeft);
            var titleRect = (RectTransform)titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(24f, 0f);
            titleRect.offsetMax = new Vector2(-24f, -18f);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, 34f);

            var bodyText = VNPhotoBoothUi.CreateText("HelpBody", cardRect, 22,
                new Color(1f, 1f, 1f, 0.85f), VNLocale.T("photo.help.body"),
                TextAlignmentOptions.TopLeft);
            bodyText.lineSpacing = 8f;
            var bodyRect = (RectTransform)bodyText.transform;
            bodyRect.anchorMin = new Vector2(0f, 1f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0.5f, 1f);
            bodyRect.offsetMin = new Vector2(24f, 0f);
            bodyRect.offsetMax = new Vector2(-24f, -60f);

            // 卡片高度按正文实测（TMP + ContentSizeFitter 首帧量不准，手工量更稳）
            const float cardWidth = 440f;
            cardRect.sizeDelta = new Vector2(cardWidth, 400f);
            bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, 400f);
            bodyText.ForceMeshUpdate();
            float bodyHeight = bodyText.preferredHeight;
            bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, bodyHeight);
            cardRect.sizeDelta = new Vector2(cardWidth, bodyHeight + 82f);
            cardRect.anchoredPosition = new Vector2(
                MachineW * 0.5f - 38f, TitleY - 40f);

            _helpCard = card.gameObject.AddComponent<CanvasGroup>();
            _helpCard.alpha = 0f;
            _helpCard.blocksRaycasts = false;
            card.gameObject.SetActive(false);
        }

        /// <summary>点「?」开合。展开时卡片自己吃掉射线，免得点到底下的取景框</summary>
        void ToggleHelp()
        {
            if (_helpCard == null) return;
            _helpOpen = !_helpOpen;

            var rect = (RectTransform)_helpCard.transform;
            _helpCard.gameObject.SetActive(true);
            _helpCard.blocksRaycasts = _helpOpen;
            _helpCard.DOKill();
            rect.DOKill();

            if (_helpOpen)
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, TitleY - 26f);

            _helpCard.DOFade(_helpOpen ? 1f : 0f, 0.16f)
                .SetUpdate(true).SetLink(_helpCard.gameObject)
                .OnComplete(() =>
                {
                    if (!_helpOpen) _helpCard.gameObject.SetActive(false);
                });
            rect.DOAnchorPosY(_helpOpen ? TitleY - 40f : TitleY - 26f, 0.16f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(_helpCard.gameObject);
        }

        void BuildViewFinder(RectTransform parent)
        {
            _viewFinder = VNPhotoBoothUi.CreateNode("ViewFinder", parent);
            VNPhotoBoothUi.Center(_viewFinder, new Vector2(ViewW, ViewH), new Vector2(0f, ViewY));

            _frameBack = VNPhotoBoothUi.CreateImage("FrameBack", _viewFinder, null, Color.white);
            VNUi.Stretch((RectTransform)_frameBack.transform);

            // 人物开窗：Image + Mask，立绘作为它的子节点被裁进形状里
            _windowImage = VNPhotoBoothUi.CreateImage("Window", _viewFinder, null, Color.white);
            _window = (RectTransform)_windowImage.transform;
            var mask = _windowImage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;   // 遮罩图本身就是开窗底色

            // ★ 开窗内的层序（从后往前）：背景 → 拖动板 → 人后贴纸 → 我 → 她
            //   拖动板压在最底下是刻意的：它 raycastTarget=true 会吃掉射线，
            //   放上面的话「人后贴纸」就永远点不到了。人物本身不接收射线，不用担心被挡。
            _backdropImage = VNPhotoBoothUi.CreateImage("Backdrop", _window, null, Color.white);

            var dragPad = VNPhotoBoothUi.CreateImage("DragPad", _window, null,
                new Color(0f, 0f, 0f, 0f), true);
            VNUi.Stretch((RectTransform)dragPad.transform);
            _dragger = dragPad.gameObject.AddComponent<VNPhotoPortraitDragger>();
            _dragger.onChanged = RefreshPortraits;
            _dragger.onDoubleClick = BringPortraitToFront;
            _dragger.onBackdropChanged = () => ApplyBackdrop(_backdrop);

            // 人后贴纸层：尺寸与取景框一致（超出开窗的部分被 Mask 裁掉，正好），
            // 这样它与人前贴纸层坐标系相同，翻层时贴纸不会跳位
            _backStickerLayer = VNPhotoBoothUi.CreateNode("BackStickerLayer", _window);
            VNPhotoBoothUi.Center(_backStickerLayer, new Vector2(ViewW, ViewH), Vector2.zero);

            _meImage = VNPhotoBoothUi.CreateImage("MePortrait", _window, null, Color.white);
            _herImage = VNPhotoBoothUi.CreateImage("HerPortrait", _window, null, Color.white);
            _dragger.me = (RectTransform)_meImage.transform;
            _dragger.her = (RectTransform)_herImage.transform;

            // 描边环必须在 Mask 外面，否则会被自己裁掉
            _windowRing = VNPhotoBoothUi.CreateImage("WindowRing", _viewFinder, null, Color.white);

            _frameFront = VNPhotoBoothUi.CreateImage("FrameFront", _viewFinder, null, Color.white);
            VNUi.Stretch((RectTransform)_frameFront.transform);

            _watermark = VNPhotoBoothUi.CreateText("Watermark", _viewFinder, 34, Color.white, "");
            VNPhotoBoothUi.Center((RectTransform)_watermark.transform,
                new Vector2(ViewW - 60f, 60f), new Vector2(0f, -ViewH * 0.5f + 52f));

            // 贴纸层在最上：贴纸可以压到边框上（参考图里的爱心就在框上）
            _stickerLayer = VNPhotoBoothUi.CreateNode("StickerLayer", _viewFinder);
            VNUi.Stretch(_stickerLayer);

            // 涂鸦层再压一层：笔是画在「洗好的照片」上的，盖住一切
            var doodleRoot = VNPhotoBoothUi.CreateNode("DoodleLayer", _viewFinder);
            VNUi.Stretch(doodleRoot);
            _doodle = new VNPhotoDoodle();
            _doodle.Build(doodleRoot);
        }

        void BuildLeftPanel(RectTransform parent)
        {
            var panel = VNPhotoBoothUi.CreateImage("LeftPanel", parent,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.PanelBg, true);
            var rect = VNPhotoBoothUi.Center((RectTransform)panel.transform,
                new Vector2(PanelW, PanelH), new Vector2(-PanelX, PanelY));
            _leftPanel = panel.gameObject;

            // 顶部四个标签：边框 / 背景 / 贴纸 / 涂鸦
            const float tabW = 76f;
            var tabs = new List<(Button button, TextMeshProUGUI text, GameObject page)>();
            float tabY = PanelH * 0.5f - 36f;
            float[] tabX = { -117f, -39f, 39f, 117f };
            string[] tabKeys =
            {
                "photo.tab.frame", "photo.tab.backdrop",
                "photo.tab.sticker", "photo.tab.doodle",
            };

            var buttons = new Button[4];
            var labels = new TextMeshProUGUI[4];
            for (int i = 0; i < 4; i++)
            {
                buttons[i] = VNPhotoBoothUi.CreateButton($"Tab{i}", rect,
                    new Vector2(tabW, 52f), new Vector2(tabX[i], tabY),
                    VNLocale.T(tabKeys[i]),
                    i == 0 ? VNPhotoBoothUi.Accent : VNPhotoBoothUi.CellBg,
                    i == 0 ? Color.white : VNPhotoBoothUi.TextDark, 22, out labels[i]);
            }

            var frameScroll = VNPhotoBoothUi.CreateScrollList("FrameList", rect,
                new Vector2(ListW, ListH), new Vector2(0f, ListY), 1,
                new Vector2(300f, 176f), 12f, out _frameContent);
            var backdropScroll = VNPhotoBoothUi.CreateScrollList("BackdropList", rect,
                new Vector2(ListW, ListH), new Vector2(0f, ListY), 1,
                new Vector2(300f, 176f), 12f, out _backdropContent);
            var stickerScroll = VNPhotoBoothUi.CreateScrollList("StickerList", rect,
                new Vector2(ListW, ListH), new Vector2(0f, ListY), 2,
                new Vector2(144f, 144f), 12f, out _stickerContent);
            var doodlePage = BuildDoodlePage(rect);
            backdropScroll.gameObject.SetActive(false);
            stickerScroll.gameObject.SetActive(false);
            doodlePage.SetActive(false);

            tabs.Add((buttons[0], labels[0], frameScroll.gameObject));
            tabs.Add((buttons[1], labels[1], backdropScroll.gameObject));
            tabs.Add((buttons[2], labels[2], stickerScroll.gameObject));
            tabs.Add((buttons[3], labels[3], doodlePage));

            for (int i = 0; i < tabs.Count; i++)
            {
                int index = i;
                tabs[i].button.onClick.AddListener(() => SelectTab(tabs, index));
            }

            BuildFrameCells();
            BuildBackdropCells();
            BuildStickerCells();
        }

        void SelectTab(
            List<(Button button, TextMeshProUGUI text, GameObject page)> tabs, int active)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                bool on = i == active;
                tabs[i].page.SetActive(on);
                if (tabs[i].button.targetGraphic is Image image)
                    image.color = on ? VNPhotoBoothUi.Accent : VNPhotoBoothUi.CellBg;
                tabs[i].text.color = on ? Color.white : VNPhotoBoothUi.TextDark;
            }

            // 只有停在「涂鸦」页时画布才吃鼠标，否则挡住人物与贴纸的操作
            _doodle?.SetInteractive(active == 3);
        }

        // ==================================================================
        // 涂鸦工具页
        // ==================================================================

        static readonly Color[] PenColors =
        {
            new Color(1f, 0.35f, 0.55f), new Color(1f, 0.55f, 0.25f),
            new Color(1f, 0.85f, 0.25f), new Color(0.55f, 0.85f, 0.35f),
            new Color(0.3f, 0.78f, 0.72f), new Color(0.35f, 0.6f, 1f),
            new Color(0.65f, 0.45f, 1f), new Color(1f, 0.45f, 0.85f),
            new Color(1f, 1f, 1f), new Color(0.25f, 0.22f, 0.28f),
            new Color(0.75f, 0.55f, 0.4f), new Color(0.6f, 0.85f, 1f),
        };

        GameObject BuildDoodlePage(RectTransform parent)
        {
            var page = VNPhotoBoothUi.CreateNode("DoodlePage", parent);
            VNPhotoBoothUi.Center(page, new Vector2(ListW, ListH), new Vector2(0f, ListY));

            float y = ListH * 0.5f - 40f;

            // ---- 颜色格 4×3 ----
            var colorCells = new List<Image>();
            for (int i = 0; i < PenColors.Length; i++)
            {
                int row = i / 4, col = i % 4;
                var cell = VNPhotoBoothUi.CreateImage($"Pen{i}", page,
                    VNProceduralTextures.RoundedRectSprite, PenColors[i], true);
                VNPhotoBoothUi.Center((RectTransform)cell.transform, new Vector2(76f, 76f),
                    new Vector2(-120f + col * 80f, y - row * 96f));

                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = cell;
                var captured = PenColors[i];
                int index = i;
                button.onClick.AddListener(() =>
                {
                    _doodle.penColor = captured;
                    _doodle.eraser = false;
                    RefreshPenUi(colorCells, index);
                });
                colorCells.Add(cell);
            }
            y -= 2 * 96f + 94f;

            // ---- 笔粗滑块（自由调，不是几个档位）----
            var sizeLabel = VNPhotoBoothUi.CreateText("SizeLabel", page, 24,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.pen.size"),
                TextAlignmentOptions.Left);
            VNPhotoBoothUi.Center((RectTransform)sizeLabel.transform,
                new Vector2(150f, 32f), new Vector2(-68f, y));

            _penPreview = VNPhotoBoothUi.CreateImage("PenPreview", page,
                VNPhotoTextures.CircleSprite(), VNPhotoBoothUi.TextDark);
            VNPhotoBoothUi.Center((RectTransform)_penPreview.transform,
                new Vector2(24f, 24f), new Vector2(120f, y));

            y -= 50f;
            var slider = VNPhotoBoothUi.CreateSlider("PenSize", page,
                new Vector2(292f, 32f), new Vector2(0f, y), 2f, 40f, _doodle.penSize);
            slider.onValueChanged.AddListener(v =>
            {
                _doodle.penSize = v;
                UpdatePenPreview();
            });
            UpdatePenPreview();

            y -= 84f;

            // ---- 荧光笔 / 橡皮 ----
            var glowButton = VNPhotoBoothUi.CreateButton("GlowPen", page,
                new Vector2(144f, 60f), new Vector2(-76f, y),
                VNLocale.T("photo.pen.glow"), VNPhotoBoothUi.CellBg,
                VNPhotoBoothUi.TextDark, 24, out var glowText);
            var eraserButton = VNPhotoBoothUi.CreateButton("Eraser", page,
                new Vector2(144f, 60f), new Vector2(76f, y),
                VNLocale.T("photo.pen.eraser"), VNPhotoBoothUi.CellBg,
                VNPhotoBoothUi.TextDark, 24, out var eraserText);

            glowButton.onClick.AddListener(() =>
            {
                _doodle.glowPen = !_doodle.glowPen;
                _doodle.eraser = false;
                RefreshToolButtons(glowButton, glowText, eraserButton, eraserText);
            });
            eraserButton.onClick.AddListener(() =>
            {
                _doodle.eraser = !_doodle.eraser;
                RefreshToolButtons(glowButton, glowText, eraserButton, eraserText);
            });

            y -= 88f;

            // ---- 撤销 / 清空 ----
            _undoButton = VNPhotoBoothUi.CreateButton("Undo", page,
                new Vector2(144f, 60f), new Vector2(-76f, y),
                VNLocale.T("photo.pen.undo"), VNPhotoBoothUi.CellBg,
                VNPhotoBoothUi.TextDark, 24, out _);
            _undoButton.onClick.AddListener(() => _doodle.Undo());

            VNPhotoBoothUi.CreateButton("ClearDoodle", page,
                new Vector2(144f, 60f), new Vector2(76f, y),
                VNLocale.T("photo.pen.clear"), VNPhotoBoothUi.CellBg,
                VNPhotoBoothUi.TextDark, 24, out _)
                .onClick.AddListener(() => _doodle.Clear());

            // 说明跟在工具区下面（不钉到页底，否则中间空一大块）
            y -= 100f;
            var hint = VNPhotoBoothUi.CreateText("DoodleHint", page, 21,
                new Color(0.45f, 0.4f, 0.45f), VNLocale.T("photo.pen.hint"));
            VNPhotoBoothUi.Center((RectTransform)hint.transform,
                new Vector2(300f, 70f), new Vector2(0f, y));

            RefreshPenUi(colorCells, 0);
            return page.gameObject;
        }

        void UpdatePenPreview()
        {
            if (_penPreview == null) return;
            // 画布 768 宽显示成 1040，所以预览点按同一比例放大才是「所见即所得」
            float shown = Mathf.Clamp(_doodle.penSize * 2f * (ViewW / VNPhotoDoodle.Width),
                6f, 56f);
            ((RectTransform)_penPreview.transform).sizeDelta = new Vector2(shown, shown);
            _penPreview.color = _doodle.eraser ? new Color(0.6f, 0.6f, 0.62f)
                : _doodle.penColor;
        }

        void RefreshPenUi(List<Image> cells, int active)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var rect = (RectTransform)cells[i].transform;
                rect.localScale = Vector3.one * (i == active && !_doodle.eraser ? 1.16f : 1f);
            }
            UpdatePenPreview();
        }

        void RefreshToolButtons(Button glow, TextMeshProUGUI glowText,
            Button eraser, TextMeshProUGUI eraserText)
        {
            if (glow.targetGraphic is Image glowImage)
                glowImage.color = _doodle.glowPen && !_doodle.eraser
                    ? VNPhotoBoothUi.Accent : VNPhotoBoothUi.CellBg;
            glowText.color = _doodle.glowPen && !_doodle.eraser
                ? Color.white : VNPhotoBoothUi.TextDark;

            if (eraser.targetGraphic is Image eraserImage)
                eraserImage.color = _doodle.eraser
                    ? VNPhotoBoothUi.Accent : VNPhotoBoothUi.CellBg;
            eraserText.color = _doodle.eraser ? Color.white : VNPhotoBoothUi.TextDark;

            UpdatePenPreview();
        }

        /// <summary>背景列表：第一项固定是「无背景」（露出边框自己的开窗底色）</summary>
        void BuildBackdropCells()
        {
            _backdropCells.Clear();
            AddBackdropCell(null, VNLocale.T("photo.backdrop.none"));
            foreach (var def in backdrops)
                if (def != null) AddBackdropCell(def, def.DisplayName);
        }

        void AddBackdropCell(VNPhotoBackdropDef def, string label)
        {
            var cell = VNPhotoBoothUi.CreateImage($"BackdropCell_{label}", _backdropContent,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.CellBg, true);
            var rect = (RectTransform)cell.transform;

            if (def != null)
            {
                var preview = VNPhotoBoothUi.CreateImage("Preview", rect,
                    def.ResolveSprite(), Color.white);
                VNPhotoBoothUi.Center((RectTransform)preview.transform,
                    new Vector2(276f, 112f), new Vector2(0f, 20f));
            }

            var text = VNPhotoBoothUi.CreateText("Label", rect, 26,
                VNPhotoBoothUi.TextDark, label);
            VNPhotoBoothUi.Center((RectTransform)text.transform, new Vector2(276f, 40f),
                new Vector2(0f, def != null ? -60f : 0f));

            var button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = cell;
            var captured = def;
            button.onClick.AddListener(() =>
            {
                if (_phase != Phase.Dressing) return;
                // 换了图构图就不一样了，上一张调好的位移/缩放没有意义，重置
                if (_dragger != null)
                {
                    _dragger.backdropOffset = Vector2.zero;
                    _dragger.backdropScale = 1f;
                }
                ApplyBackdrop(captured);
                RefreshBackdropCells();
            });

            _backdropCells.Add(cell);
        }

        void RefreshBackdropCells()
        {
            for (int i = 0; i < _backdropCells.Count; i++)
            {
                if (_backdropCells[i] == null) continue;
                bool selected = i == 0 ? _backdrop == null
                    : i - 1 < backdrops.Count && backdrops[i - 1] == _backdrop;
                _backdropCells[i].color = selected
                    ? VNPhotoBoothUi.CellSelected : VNPhotoBoothUi.CellBg;
            }
        }

        /// <summary>边框列表：第一项固定是「默认」（不加边框），对应参考实现的 0 号</summary>
        void BuildFrameCells()
        {
            _frameCells.Clear();
            AddFrameCell(null, VNLocale.T("photo.frame.none"));
            foreach (var frame in frames)
                if (frame != null) AddFrameCell(frame, frame.DisplayName);
        }

        void AddFrameCell(VNPhotoFrameDef def, string label)
        {
            var cell = VNPhotoBoothUi.CreateImage($"FrameCell_{label}", _frameContent,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.CellBg, true);
            var rect = (RectTransform)cell.transform;

            if (def != null)
            {
                var preview = VNPhotoBoothUi.CreateImage("Preview", rect,
                    def.ResolveFrameSprite(), Color.white);
                VNPhotoBoothUi.Center((RectTransform)preview.transform,
                    new Vector2(276f, 112f), new Vector2(0f, 20f));
            }

            var text = VNPhotoBoothUi.CreateText("Label", rect, 26,
                VNPhotoBoothUi.TextDark, label);
            VNPhotoBoothUi.Center((RectTransform)text.transform, new Vector2(276f, 40f),
                new Vector2(0f, def != null ? -60f : 0f));

            var button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = cell;
            var captured = def;
            button.onClick.AddListener(() =>
            {
                if (_phase != Phase.Dressing) return;
                ApplyFrame(captured);
                RefreshFrameCells();
            });

            _frameCells.Add(cell);
        }

        void BuildStickerCells()
        {
            foreach (var sticker in stickers)
            {
                if (sticker == null) continue;
                var cell = VNPhotoBoothUi.CreateImage($"StickerCell_{sticker.stickerId}",
                    _stickerContent, VNProceduralTextures.RoundedRectSprite,
                    VNPhotoBoothUi.CellBg, true);
                var rect = (RectTransform)cell.transform;

                var icon = VNPhotoBoothUi.CreateImage("Icon", rect,
                    sticker.ResolveSprite(), sticker.tint);
                icon.preserveAspect = true;
                VNPhotoBoothUi.Center((RectTransform)icon.transform,
                    new Vector2(96f, 96f), Vector2.zero);

                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = cell;
                var captured = sticker;
                button.onClick.AddListener(() =>
                {
                    if (_phase != Phase.Dressing) return;
                    SpawnSticker(captured, RandomSpawnPos(), 1f, 0f, false);
                });
            }

            if (stickers.Count != 0) return;
            var empty = VNPhotoBoothUi.CreateText("EmptyHint", _stickerContent, 22,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.sticker.empty"));
            ((RectTransform)empty.transform).sizeDelta = new Vector2(260f, 80f);
        }

        void BuildRightPanel(RectTransform parent)
        {
            var panel = VNPhotoBoothUi.CreateImage("RightPanel", parent,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.PanelBg, true);
            var rect = VNPhotoBoothUi.Center((RectTransform)panel.transform,
                new Vector2(PanelW, PanelH), new Vector2(PanelX, PanelY));
            _rightPanel = panel.gameObject;

            var title = VNPhotoBoothUi.CreateText("RightTitle", rect, 28,
                Color.white, VNLocale.T("photo.expression"));
            var titleBg = VNPhotoBoothUi.CreateImage("RightTitleBg", rect,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.Accent);
            VNPhotoBoothUi.Center((RectTransform)titleBg.transform,
                new Vector2(310f, 52f), new Vector2(0f, PanelH * 0.5f - 36f));
            ((RectTransform)title.transform).SetAsLastSibling();
            VNPhotoBoothUi.Center((RectTransform)title.transform,
                new Vector2(310f, 52f), new Vector2(0f, PanelH * 0.5f - 36f));

            // 列标题：左列是我、右列是她（不然两栏头像分不清谁是谁）
            var meLabel = VNPhotoBoothUi.CreateText("ColMe", rect, 22,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.me"));
            VNPhotoBoothUi.Center((RectTransform)meLabel.transform, new Vector2(140f, 30f),
                new Vector2(-78f, PanelH * 0.5f - 78f));
            var herLabel = VNPhotoBoothUi.CreateText("ColHer", rect, 22,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.her"));
            VNPhotoBoothUi.Center((RectTransform)herLabel.transform, new Vector2(140f, 30f),
                new Vector2(78f, PanelH * 0.5f - 78f));

            var scroll = VNPhotoBoothUi.CreateScrollList("FaceList", rect,
                new Vector2(ListW, PanelH - 120f), new Vector2(0f, -50f), 2,
                new Vector2(FaceCellSize, FaceCellSize), 10f, out var content);

            // 两列：左列是「我」，右列是「她」。行数取两边表情数的较大值，缺的补空格子。
            int meCount = _meDef?.expressions?.Count ?? 0;
            int herCount = _herDef?.expressions?.Count ?? 0;
            int rows = Mathf.Max(meCount, herCount);

            for (int i = 0; i < rows; i++)
            {
                AddFaceCell(content, _meDef, i < meCount ? _meDef.expressions[i].name : null,
                    true, i);
                AddFaceCell(content, _herDef, i < herCount ? _herDef.expressions[i].name : null,
                    false, i);
            }
            scroll.enabled = rows > FaceRowsPerPage;
        }

        void AddFaceCell(RectTransform parent, VNCharacterDef def, string expression,
            bool isMe, int index)
        {
            var cell = VNPhotoBoothUi.CreateImage($"Face_{(isMe ? "me" : "her")}_{index}",
                parent, VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.CellBg,
                expression != null);
            var rect = (RectTransform)cell.transform;

            if (def == null || expression == null)
            {
                cell.color = new Color(1f, 1f, 1f, 0.12f);
                (isMe ? _meCells : _herCells).Add(null);
                return;
            }

            // 裁一个方窗把脸框进去（和取景框同一套 portrait 参数）
            var clip = VNPhotoBoothUi.CreateNode("Clip", rect);
            VNPhotoBoothUi.Center(clip, new Vector2(FaceClipSize, FaceClipSize), Vector2.zero);
            clip.gameObject.AddComponent<RectMask2D>();

            var face = VNPhotoBoothUi.CreateImage("Face", clip, null, Color.white);
            // 格子要看清表情 → 比取景框拉得更近（脸怼满格子）
            VNPhotoBoothUi.ApplyPortrait(face, def, expression, FaceClipSize, FaceCellFit,
                AnchorFor(def), Vector2.zero, false);

            var button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = cell;
            string capturedExpr = expression;
            button.onClick.AddListener(() =>
            {
                if (_phase != Phase.Dressing) return;
                if (isMe) _meExpr = capturedExpr; else _herExpr = capturedExpr;
                RefreshPortraits();
                RefreshExpressionCells();
            });

            (isMe ? _meCells : _herCells).Add(cell);
        }

        void BuildBottomBar(RectTransform parent)
        {
            // 操作说明搬去了右上角的「?」，这条只剩快门
            var bar = VNPhotoBoothUi.CreateNode("BottomBar", parent);
            VNPhotoBoothUi.Center(bar, new Vector2(MachineW, 120f),
                new Vector2(0f, -MachineH * 0.5f + 74f));
            _bottomBar = bar.gameObject;

            var shutter = VNPhotoBoothUi.CreateImage("Shutter", bar,
                VNPhotoTextures.CircleSprite(), VNPhotoBoothUi.Accent, true);
            var shutterRect = VNPhotoBoothUi.Center((RectTransform)shutter.transform,
                new Vector2(112f, 112f), Vector2.zero);

            var icon = VNPhotoBoothUi.CreateText("ShutterIcon", shutterRect, 48,
                Color.white, "◉");
            VNPhotoBoothUi.Center((RectTransform)icon.transform, new Vector2(112f, 112f),
                Vector2.zero);

            _shutterButton = shutter.gameObject.AddComponent<Button>();
            _shutterButton.targetGraphic = shutter;
            _shutterButton.onClick.AddListener(Shoot);
        }
    }
}
