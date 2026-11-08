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
        // 限时 / 键盘
        // ==================================================================

        void Update()
        {
            if (VNPause.IsPaused) return;   // 教程讲解中：限时与全部快捷键一起冻住
            if (_phase == Phase.Dressing) TickTimer();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_phase == Phase.Dressing) ShowConfirm(true);
                else if (_phase == Phase.Confirm) ShowConfirm(false);
                else if (_phase == Phase.Result) Finish();   // 照片已经拍好了，ESC = 收下
            }

            // Ctrl+Z 撤销涂鸦（装扮阶段才有意义）
            if (_phase == Phase.Dressing && _doodle != null &&
                keyboard.zKey.wasPressedThisFrame &&
                (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
                _doodle.Undo();

            if (_undoButton != null && _doodle != null)
                _undoButton.interactable = _doodle.CanUndo;
        }

        void TickTimer()
        {
            if (_timeLimit <= 0f) return;

            _timeLeft -= VNTime.Delta;   // 三铁律：不受快进 timeScale 影响；受 VNPause 冻结
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                UpdateTimerUi();
                Shoot();                            // 超时自动快门
                return;
            }
            UpdateTimerUi();
        }

        void UpdateTimerUi()
        {
            if (_timerFill != null)
                _timerFill.sizeDelta = new Vector2(
                    360f * Mathf.Clamp01(_timeLeft / Mathf.Max(0.01f, _timeLimit)), 0f);

            if (_timerText == null) return;
            _timerText.text = Mathf.CeilToInt(_timeLeft).ToString();

            bool urgent = _timeLeft <= UrgentSeconds;
            _timerText.color = urgent ? VNPhotoBoothUi.Urgent : Color.white;
            if (_timerFill != null)
            {
                var fillImage = _timerFill.GetComponent<Image>();
                if (fillImage != null)
                    fillImage.color = urgent ? VNPhotoBoothUi.Urgent : VNPhotoBoothUi.AccentSoft;
            }
        }

        // ==================================================================
        // 放弃确认
        // ==================================================================

        void ShowConfirm(bool show)
        {
            if (show && _confirmLayer == null) BuildConfirmLayer();
            if (_confirmLayer == null) return;

            _confirmLayer.SetActive(show);
            _phase = show ? Phase.Confirm : Phase.Dressing;
        }

        void BuildConfirmLayer()
        {
            var root = (RectTransform)transform;
            var layer = VNPhotoBoothUi.CreateImage("ConfirmLayer", root, null,
                new Color(0f, 0f, 0f, 0.6f), true);
            VNUi.Stretch((RectTransform)layer.transform);
            _confirmLayer = layer.gameObject;

            var box = VNPhotoBoothUi.CreateImage("Box", (RectTransform)layer.transform,
                VNProceduralTextures.RoundedRectSprite, new Color(0.16f, 0.15f, 0.19f, 0.98f), true);
            var boxRect = VNPhotoBoothUi.Center((RectTransform)box.transform,
                new Vector2(660f, 260f), Vector2.zero);

            var text = VNPhotoBoothUi.CreateText("Text", boxRect, 30, Color.white,
                VNLocale.T("photo.quit.confirm"));
            VNPhotoBoothUi.Center((RectTransform)text.transform, new Vector2(600f, 100f),
                new Vector2(0f, 40f));

            VNPhotoBoothUi.CreateButton("Cancel", boxRect, new Vector2(220f, 66f),
                new Vector2(-130f, -62f), VNLocale.T("photo.quit.cancel"),
                VNPhotoBoothUi.Accent, Color.white, 28, out _)
                .onClick.AddListener(() => ShowConfirm(false));

            VNPhotoBoothUi.CreateButton("Quit", boxRect, new Vector2(220f, 66f),
                new Vector2(130f, -62f), VNLocale.T("photo.quit.ok"),
                new Color(0.75f, 0.28f, 0.32f, 1f), Color.white, 28, out _)
                .onClick.AddListener(QuitWithoutPhoto);
        }

        void QuitWithoutPhoto()
        {
            if (_phase == Phase.Ending) return;
            _phase = Phase.Ending;

            // 放弃 = 没有照片：自由拍照按「完成」收场，评分模式按 0 分失败
            if (_freeMode)
            {
                Done(VNPhotoScore.OutcomeFree);
                return;
            }
            WriteFlags(0, 0);
            Done(VNPhotoScore.OutcomeFail);
        }

        // ==================================================================
        // 快门
        // ==================================================================

        void Shoot()
        {
            if (_phase != Phase.Dressing) return;
            _phase = Phase.Shooting;
            if (_shutterButton != null) _shutterButton.interactable = false;
            StartCoroutine(ShootRoutine());
        }

        IEnumerator ShootRoutine()
        {
            // ---- 3・2・1 倒数 ----
            _countdownText.gameObject.SetActive(true);
            for (int n = 3; n >= 1; n--)
            {
                _countdownText.text = n.ToString();
                _sfx.Play(VNPhotoSfx.Kind.Tick, 1f + (3 - n) * 0.12f);   // 越数越急

                var rect = (RectTransform)_countdownText.transform;
                rect.localScale = Vector3.one * 1.6f;
                _countdownText.alpha = 1f;
                rect.DOScale(1f, 0.32f).SetEase(Ease.OutBack)
                    .SetUpdate(true).SetLink(gameObject);
                _countdownText.DOFade(0.25f, 0.85f)
                    .SetUpdate(true).SetLink(gameObject);

                yield return WaitUnscaled(1f);
            }
            _countdownText.gameObject.SetActive(false);

            // ---- 快门：闪白 + 咔嚓 + 机身一颤 ----
            _sfx.Play(VNPhotoSfx.Kind.Shutter);
            _flash.color = new Color(1f, 1f, 1f, 0.92f);
            _flash.DOFade(0f, 0.45f).SetEase(Ease.OutQuad)
                .SetUpdate(true).SetLink(gameObject);
            if (_machine != null)
                _machine.DOShakeAnchorPos(0.3f, 14f, 18, 90f, false, true)
                    .SetUpdate(true).SetLink(gameObject);

            // ---- 抓图（协程内部会把这些藏一帧，所以取景框上不会有杂物）----
            // ★ _flash 必须进这个列表：闪白要 0.45 秒才淡完，而抓图只等一帧，
            //   不藏它拍下来的就是一张白纱。闪光是"拍照瞬间"的表现，不该进照片。
            //   _helpRoot 同理：说明卡片是往左下展开的，会压住取景框右上角。
            var hide = new List<GameObject>
            {
                _leftPanel, _rightPanel, _bottomBar, _flash.gameObject, _helpRoot,
            };
            Texture2D shot = null;
            yield return VNPhotoCapture.Capture(_viewFinder, _canvas, hide, tex => shot = tex);

            _lastResult = VNPhotoScore.Evaluate(BuildDressing(), _theme);

            // 先存进相册；玩家点「重拍」再把这张删掉（只有留下的才算数）
            if (shot != null)
            {
                _shotEntry = VNPhotoAlbum.Add(shot, _herDef != null ? _herDef.id : "",
                    _meDef != null ? _meDef.id : "",
                    _theme != null ? _theme.themeId : "",
                    _lastResult.total, _freeMode ? -1 : _lastResult.grade);
                _shotTexture = shot;
                _shotSprite = Sprite.Create(shot, new Rect(0, 0, shot.width, shot.height),
                    new Vector2(0.5f, 0.5f), 100f);
                _shotSprite.hideFlags = HideFlags.DontSave;
            }

            _phase = Phase.Result;
            yield return StartCoroutine(ShowResult());
        }

        static IEnumerator WaitUnscaled(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += VNTime.Delta;
                yield return null;
            }
        }

        // ==================================================================
        // 结算：相纸飞入 → 冲分 → 档位大字
        // ==================================================================

        IEnumerator ShowResult()
        {
            // 快门按钮会从半透明结算层后面透出来，正好卡在两个按钮中间——藏掉。
            // 左右两栏同理：分数栏与右栏的表情格横向重叠，加分项飘在头像上根本读不清。
            // 机身与取景框留着，相纸才有"从这儿飞出来"的连续感。
            if (_bottomBar != null) _bottomBar.SetActive(false);
            if (_leftPanel != null) _leftPanel.SetActive(false);
            if (_rightPanel != null) _rightPanel.SetActive(false);

            BuildResultLayer(out var paper, out var scoreText, out var barFill,
                out var hitList, out var gradeText, out var commentText);

            // ---- 相纸从取景框位置飞出来 ----
            paper.anchoredPosition = new Vector2(0f, ViewY);
            paper.localScale = Vector3.one * 0.42f;
            paper.localRotation = Quaternion.Euler(0f, 0f, -14f);
            paper.DOAnchorPos(new Vector2(_freeMode ? 0f : -430f, 20f), 0.55f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
            paper.DOScale(1f, 0.55f).SetEase(Ease.OutBack)
                .SetUpdate(true).SetLink(gameObject);
            paper.DOLocalRotate(new Vector3(0f, 0f, -2.5f), 0.55f)
                .SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
            _sfx.Play(VNPhotoSfx.Kind.Place);

            yield return WaitUnscaled(0.65f);

            // 相纸落定后取景框就没用了：留着的话它那张亮底会从背板后面顶出来，
            // 跟分数、评语叠在一起。飞入过程要留着，所以只能等动画完再藏
            if (_viewFinder != null) _viewFinder.gameObject.SetActive(false);

            // 自由拍照：不评分，看一眼就完事
            if (_freeMode) yield break;

            // ---- 逐条弹出命中项，分数一路冲上去 ----
            int shown = 0;
            int max = Mathf.Max(_theme.perfectLine, _lastResult.total);
            int listed = 0;

            foreach (var hit in _lastResult.hits)
            {
                if (listed >= 6) break;      // 再多就刷屏了，剩下的合进总分
                listed++;
                shown += hit.score;

                SpawnHitRow(hitList, hit, listed);
                _sfx.Play(VNPhotoSfx.Kind.Count, 1f + listed * 0.06f);

                int from = shown - hit.score;
                int to = shown;
                DOTween.To(() => from, v => { from = v; scoreText.text = v.ToString(); },
                        to, 0.22f)
                    .SetUpdate(true).SetLink(gameObject);
                barFill.DOSizeDelta(
                        new Vector2(BarWidth * Mathf.Clamp01((float)to / max), 0f), 0.22f)
                    .SetEase(Ease.OutQuad).SetUpdate(true).SetLink(gameObject);

                yield return WaitUnscaled(0.26f);
            }

            scoreText.text = _lastResult.total.ToString();
            barFill.sizeDelta = new Vector2(
                BarWidth * Mathf.Clamp01((float)_lastResult.total / max), 0f);

            yield return WaitUnscaled(0.25f);

            // ---- 档位大字 ----
            bool good = _lastResult.grade >= 1;
            gradeText.text = GradeLabel(_lastResult.grade);
            gradeText.color = _lastResult.grade >= 2 ? new Color(1f, 0.85f, 0.35f)
                : _lastResult.grade == 1 ? new Color(0.62f, 0.88f, 1f)
                : new Color(0.85f, 0.6f, 0.65f);
            gradeText.gameObject.SetActive(true);

            var gradeRect = (RectTransform)gradeText.transform;
            gradeRect.localScale = Vector3.one * 2.2f;
            gradeRect.DOScale(1f, 0.4f).SetEase(Ease.OutBack)
                .SetUpdate(true).SetLink(gameObject);
            _sfx.Play(VNPhotoSfx.Kind.Fanfare, good ? 1f : 0.72f);

            if (_lastResult.grade >= 2) SparkleBurst(gradeRect);

            // ---- 评语：优先说命中项的细评，没有就用分档总评 ----
            string comment = !string.IsNullOrEmpty(_lastResult.bestComment)
                ? _lastResult.bestComment : _lastResult.gradeComment;
            if (!string.IsNullOrEmpty(comment))
            {
                yield return WaitUnscaled(0.3f);
                commentText.text = comment;
                commentText.alpha = 0f;
                commentText.DOFade(1f, 0.4f).SetUpdate(true).SetLink(gameObject);
            }
        }

        const float BarWidth = 520f;
        /// <summary>结算相纸（照片 780×585 + 上下白边 + 主题标题）</summary>
        const float PaperW = 860f;
        const float PaperH = 770f;

        string GradeLabel(int grade) => VNLocale.T(
            grade >= 2 ? "photo.grade.perfect"
            : grade == 1 ? "photo.grade.normal" : "photo.grade.fail");

        void SpawnHitRow(RectTransform list, VNPhotoScore.Hit hit, int index)
        {
            var row = VNPhotoBoothUi.CreateNode($"Hit{index}", list);
            VNPhotoBoothUi.Center(row, new Vector2(BarWidth, 38f),
                new Vector2(0f, -index * 42f + 90f));

            var label = VNPhotoBoothUi.CreateText("Label", row, 24,
                new Color(1f, 1f, 1f, 0.9f), hit.label, TextAlignmentOptions.Left);
            VNPhotoBoothUi.Center((RectTransform)label.transform,
                new Vector2(BarWidth - 110f, 38f), new Vector2(-55f, 0f));

            var value = VNPhotoBoothUi.CreateText("Value", row, 26,
                hit.score >= 0 ? new Color(1f, 0.82f, 0.4f) : new Color(1f, 0.5f, 0.5f),
                (hit.score >= 0 ? "+" : "") + hit.score, TextAlignmentOptions.Right);
            VNPhotoBoothUi.Center((RectTransform)value.transform,
                new Vector2(100f, 38f), new Vector2(BarWidth * 0.5f - 50f, 0f));

            row.anchoredPosition += new Vector2(40f, 0f);
            row.DOAnchorPosX(row.anchoredPosition.x - 40f, 0.25f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
        }

        /// <summary>完美时的星光爆发。UI 层自绘，不去碰舞台粒子（事件模块三铁律）。</summary>
        void SparkleBurst(RectTransform center)
        {
            for (int i = 0; i < 8; i++)
            {
                var star = VNPhotoBoothUi.CreateImage($"Sparkle{i}", center,
                    VNProceduralTextures.SparkleSprite, new Color(1f, 0.92f, 0.6f, 1f));
                var rect = VNPhotoBoothUi.Center((RectTransform)star.transform,
                    Vector2.one * 48f, Vector2.zero);

                float angle = i * 45f + Random.Range(-12f, 12f);
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad),
                                      Mathf.Sin(angle * Mathf.Deg2Rad));
                rect.DOAnchorPos(dir * Random.Range(130f, 200f), 0.6f)
                    .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
                rect.DOScale(0.2f, 0.6f).SetUpdate(true).SetLink(gameObject);
                star.DOFade(0f, 0.6f).SetUpdate(true).SetLink(gameObject)
                    .OnComplete(() => { if (star != null) Destroy(star.gameObject); });
            }
        }

        void BuildResultLayer(out RectTransform paper, out TextMeshProUGUI scoreText,
            out RectTransform barFill, out RectTransform hitList,
            out TextMeshProUGUI gradeText, out TextMeshProUGUI commentText)
        {
            var root = (RectTransform)transform;
            // 背板要压得够黑：相纸放大后与分数栏几乎铺满整屏，底下的机身、
            // 右栏表情格再透出来就会跟分数、评语搅在一起看不清
            var layer = VNPhotoBoothUi.CreateImage("ResultLayer", root, null,
                new Color(0.03f, 0.03f, 0.06f, 0.9f), true);
            VNUi.Stretch((RectTransform)layer.transform);
            _resultLayer = layer.gameObject;
            var layerRect = (RectTransform)layer.transform;

            // ---- 相纸 ----
            var paperImage = VNPhotoBoothUi.CreateImage("Paper", layerRect,
                VNPhotoTextures.PaperSprite(), Color.white);
            // 相纸左移到与分数栏对半分屏；自由拍照没有分数栏，居中放
            paper = VNPhotoBoothUi.Center((RectTransform)paperImage.transform,
                new Vector2(PaperW, PaperH), new Vector2(-430f, 20f));

            var photo = VNPhotoBoothUi.CreateImage("Photo", paper, _shotSprite, Color.white);
            photo.preserveAspect = true;
            VNPhotoBoothUi.Center((RectTransform)photo.transform,
                new Vector2(780f, 585f), new Vector2(0f, 65f));
            if (_shotSprite == null) photo.color = new Color(0.85f, 0.85f, 0.88f, 1f);

            string caption = _freeMode
                ? VNLocale.T("photo.free")
                : VNLocale.T("photo.theme", _theme.DisplayName);
            var captionText = VNPhotoBoothUi.CreateText("Caption", paper, 30,
                new Color(0.35f, 0.3f, 0.35f), caption);
            VNPhotoBoothUi.Center((RectTransform)captionText.transform,
                new Vector2(780f, 56f), new Vector2(0f, -302f));

            // ---- 分数区（自由拍照没有）----
            scoreText = null; barFill = null; hitList = null;
            gradeText = null; commentText = null;

            if (!_freeMode)
            {
                var panel = VNPhotoBoothUi.CreateNode("ScorePanel", layerRect);
                VNPhotoBoothUi.Center(panel, new Vector2(640f, 620f), new Vector2(380f, 20f));

                scoreText = VNPhotoBoothUi.CreateText("Score", panel, 92,
                    Color.white, "0");
                VNPhotoBoothUi.Center((RectTransform)scoreText.transform,
                    new Vector2(BarWidth, 110f), new Vector2(0f, 250f));

                var barBg = VNPhotoBoothUi.CreateImage("BarBg", panel,
                    VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.16f));
                var barBgRect = VNPhotoBoothUi.Center((RectTransform)barBg.transform,
                    new Vector2(BarWidth, 20f), new Vector2(0f, 186f));

                var fill = VNPhotoBoothUi.CreateImage("BarFill", barBgRect,
                    VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.AccentSoft);
                barFill = (RectTransform)fill.transform;
                barFill.anchorMin = new Vector2(0f, 0f);
                barFill.anchorMax = new Vector2(0f, 1f);
                barFill.pivot = new Vector2(0f, 0.5f);
                barFill.offsetMin = Vector2.zero;
                barFill.offsetMax = Vector2.zero;
                barFill.sizeDelta = new Vector2(0f, 0f);

                hitList = VNPhotoBoothUi.CreateNode("HitList", panel);
                VNPhotoBoothUi.Center(hitList, new Vector2(BarWidth, 300f),
                    new Vector2(0f, 20f));

                gradeText = VNPhotoBoothUi.CreateText("Grade", panel, 80, Color.white, "");
                VNPhotoBoothUi.Center((RectTransform)gradeText.transform,
                    new Vector2(BarWidth, 110f), new Vector2(0f, -190f));
                gradeText.gameObject.SetActive(false);

                commentText = VNPhotoBoothUi.CreateText("Comment", panel, 28,
                    new Color(1f, 1f, 1f, 0.9f), "");
                VNPhotoBoothUi.Center((RectTransform)commentText.transform,
                    new Vector2(620f, 90f), new Vector2(0f, -272f));
            }

            // ---- 重拍 / 完成（相纸变高了，按钮跟着往下让）----
            VNPhotoBoothUi.CreateButton("Retake", layerRect, new Vector2(240f, 72f),
                new Vector2(-160f, -450f), VNLocale.T("photo.retake"),
                new Color(0.28f, 0.3f, 0.4f, 1f), Color.white, 30, out _)
                .onClick.AddListener(Retake);

            VNPhotoBoothUi.CreateButton("Finish", layerRect, new Vector2(240f, 72f),
                new Vector2(160f, -450f), VNLocale.T("photo.finish"),
                VNPhotoBoothUi.Accent, Color.white, 30, out _)
                .onClick.AddListener(Finish);
        }

        /// <summary>重拍：把刚存的那张从相册删掉，回到装扮阶段（装扮内容全保留）</summary>
        void Retake()
        {
            if (_phase != Phase.Result) return;

            if (_shotEntry != null) VNPhotoAlbum.Delete(_shotEntry.file);
            _shotEntry = null;
            ReleaseShot();

            if (_resultLayer != null) { Destroy(_resultLayer); _resultLayer = null; }
            if (_bottomBar != null) _bottomBar.SetActive(true);
            if (_leftPanel != null) _leftPanel.SetActive(true);
            if (_rightPanel != null) _rightPanel.SetActive(true);
            if (_viewFinder != null) _viewFinder.gameObject.SetActive(true);

            _timeLeft = _timeLimit;
            UpdateTimerUi();
            if (_shutterButton != null) _shutterButton.interactable = true;
            _phase = Phase.Dressing;
        }

        /// <summary>完成：留下照片，写成绩，按结果分支离开</summary>
        void Finish()
        {
            if (_phase != Phase.Result) return;
            _phase = Phase.Ending;

            if (_freeMode)
            {
                VNFlags.Add(_flagPrefix + FlagCountSuffix, 1);
                Done(VNPhotoScore.OutcomeFree);
                return;
            }

            WriteFlags(_lastResult.total, _lastResult.grade);
            ApplyStatReward(_lastResult.total);
            Done(_lastResult.Outcome);
        }

        void ReleaseShot()
        {
            if (_shotSprite != null) { Destroy(_shotSprite); _shotSprite = null; }
            if (_shotTexture != null) { Destroy(_shotTexture); _shotTexture = null; }
        }

        void OnDestroy()
        {
            ReleaseShot();
            _doodle?.Destroy();   // 两张画布纹理 + 加法材质
        }

        VNPhotoDressing BuildDressing()
        {
            var dressing = new VNPhotoDressing
            {
                meExpression = _meExpr,
                herExpression = _herExpr,
                frameId = _frame != null ? _frame.frameId : null,
                backdropId = _backdrop != null ? _backdrop.backdropId : null,
            };
            foreach (var item in _playerStickers)
                if (item != null) dressing.stickerIds.Add(item.stickerId);
            return dressing;
        }

        void WriteFlags(int score, int grade)
        {
            VNFlags.Set(_flagPrefix + FlagScoreSuffix, score);
            VNFlags.Set(_flagPrefix + FlagGradeSuffix, grade);
            VNFlags.Add(_flagPrefix + FlagCountSuffix, 1);
        }

        void ApplyStatReward(int score)
        {
            if (string.IsNullOrEmpty(_statId) || score <= 0) return;

            int amount = Mathf.RoundToInt(score * _statRate);
            if (amount == 0) return;

            if (_statsHud != null)
                _statsHud.Apply(_statId, (amount >= 0 ? "+" : "") + amount, false, 0);
            else
                VNFlags.Add(_statId, amount);
        }
    }
}
