using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VNEffects
{
    /// <summary>
    /// 秘密偷拍模式（状态机 + 输入 + 察觉 + 快门 + 被发现），由 VNScriptRunner 启动时创建。
    ///
    /// 【它是什么】不是事件模块——不由剧本 event 启动，而是解锁后玩家随时（台词处）点右上角
    /// 图标进入的**全局面板**，和 G 键画廊 / I 键背包同一层级。但它和那些面板不同：
    /// 它**不盖住舞台，而是直接操控舞台的镜头**（ZoomRoot），所以与 aitalk / interact 一样
    /// 是「刻意破一次模块三铁律」，边界收紧为：只碰镜头容器、UI 可见性、Ken Burns 开关；
    /// 四条退出路径（正常退出 / ESC / 被发现 / 剧本 Stop·被销毁）全部还原。
    ///
    /// 【状态全在 flag】解锁 <see cref="VNSecretPhotoDef.unlockFlag"/>、胶卷 道具_胶卷、
    /// 警惕 偷拍_警惕_&lt;角色&gt;——一个存档字段都不加。模式本身是瞬态，不进存档；
    /// 打开期间 Runner 的 Update 直接 return，所以 F5/F9/Q/L 都按不到。
    ///
    /// 【察觉（简单版）】只升不降的累计量：
    ///   每秒 += 基础 × 她在取景框里的权重 × 缩放倍率 × (1 + 警惕/100)
    /// 她不在框里就不涨（唯一的躲避手段）。满 100 = 被发现：扣好感、警惕永久 +N、
    /// 她做情绪动作 + 漫符 + 说一句、强制退出。**被发现那一下已经按下的快门照样扣胶卷**。
    ///
    /// 【画面保持活的】眨眼 / 呼吸 / 天气 / 表情都不停；只停 Ken Burns
    /// （背景自己漂会跟玩家抢构图）。
    /// </summary>
    public class VNSecretPhotoMode : MonoBehaviour
    {
        public static VNSecretPhotoMode Instance { get; private set; }

        VNScriptRunner _runner;
        VNStage _stage;
        VNSecretPhotoDef _def;
        VNSecretPhotoUi _ui;
        Canvas _canvas;

        bool _open;
        bool _shooting;
        bool _caught;
        Vector2 _look;
        float _zoom = 1f;
        float _alert;              // 0~100
        string _targetId;
        bool _dragging;
        Vector2 _dragLast;
        bool _kenBurnsWasPlaying;
        bool _cursorWasVisible = true;
        bool _wasUnlocked;
        VNPhotoSfx _sfx;
        AudioSource _caughtSource;
        AudioClip _caughtClip;

        public bool IsOpen => _open;

        // ==================================================================
        // 生命周期
        // ==================================================================

        public void Initialize(VNScriptRunner runner, VNStage stage)
        {
            _runner = runner;
            _stage = stage;
            Instance = this;
            _def = VNSecretPhotoDef.Resolve();

            var root = stage != null && stage.characterLayer != null
                ? stage.characterLayer.GetComponentInParent<Canvas>() : null;
            if (root != null) root = root.rootCanvas;
            _canvas = root;
            if (root == null)
            {
                Debug.LogWarning("[VNSecretPhoto] 找不到主 Canvas，偷拍模式不可用");
                return;
            }

            if (_ui == null)
            {
                _ui = gameObject.AddComponent<VNSecretPhotoUi>();
                _ui.Build(root.transform);
                _ui.IconClicked += TryOpen;
                _ui.ShutterClicked += Shoot;
                _ui.ExitClicked += () => Close(false);
            }

            _sfx = new VNPhotoSfx();
            _sfx.Build(gameObject, stage != null ? stage.vnAudio : null);
            if (_def.shutterClip != null) _sfx.Override(VNPhotoSfx.Kind.Shutter, _def.shutterClip);

            VNLocale.LanguageChanged -= OnLanguageChanged;
            VNLocale.LanguageChanged += OnLanguageChanged;
        }

        void OnLanguageChanged()
        {
            // 文案全在 UI 里；重建最省事。打开期间切语言不可能（设置面板开不了）
            if (_ui == null || _open) return;
            Destroy(_ui);
            _ui = null;
            if (_canvas != null)
            {
                _ui = gameObject.AddComponent<VNSecretPhotoUi>();
                _ui.Build(_canvas.transform);
                _ui.IconClicked += TryOpen;
                _ui.ShutterClicked += Shoot;
                _ui.ExitClicked += () => Close(false);
            }
        }

        void OnDestroy()
        {
            VNLocale.LanguageChanged -= OnLanguageChanged;
            if (_open) ForceClose();
            if (Instance == this) Instance = null;
        }

        // ==================================================================
        // 解锁 / 胶卷
        // ==================================================================

        public bool IsUnlocked => _def != null && VNFlags.Get(_def.unlockFlag) > 0;
        public int Film => _def != null ? VNFlags.Get(_def.FilmFlag) : 0;

        /// <summary>画廊「私密」页要不要出现：解锁了，或者相册里已经有照片（换周目也别让照片失联）</summary>
        public static bool AlbumVisible
        {
            get
            {
                var def = VNSecretPhotoDef.Resolve();
                return VNFlags.Get(def.unlockFlag) > 0 || VNSecretAlbum.Count > 0;
            }
        }

        // ==================================================================
        // 开关
        // ==================================================================

        /// <summary>图标点击 / 外部入口：条件不满足时提示原因</summary>
        public void TryOpen()
        {
            if (_open || _runner == null || _stage == null || _ui == null) return;
            if (!IsUnlocked) return;
            if (!_runner.CanOpenSecretPhoto())
            {
                VNToast.Show(VNLocale.T("secretphoto.cannotNow"));
                return;
            }
            if (Film <= 0 && !_def.allowEnterWithoutFilm)
            {
                VNToast.Show(VNLocale.T("secretphoto.noFilm"));
                return;
            }
            Open();
        }

        void Open()
        {
            var cam = _stage.vnCamera;
            if (cam == null || cam.target == null)
            {
                Debug.LogWarning("[VNSecretPhoto] VNStage.vnCamera 未连线，偷拍模式无法缩放");
                return;
            }

            _open = true;
            _shooting = false;
            _caught = false;
            _dragging = false;
            _alert = 0f;
            _targetId = null;
            _zoom = Mathf.Max(0.1f, _def.zoomMin);
            _look = Vector2.zero;

            _runner.SetSkip(false);
            if (_runner.IsAuto) _runner.SetAuto(false);
            _runner.SetSecretPhotoActive(true);

            if (_def.pauseKenBurns && _stage.kenBurns != null)
            {
                _kenBurnsWasPlaying = _stage.kenBurns.IsPlaying;
                if (_kenBurnsWasPlaying) _stage.kenBurns.SetPlaying(false);
            }
            else _kenBurnsWasPlaying = false;

            cam.BeginManual(_def.zoomMode);
            ApplyView();

            _cursorWasVisible = Cursor.visible;
            Cursor.visible = true;

            _ui.SetIcon(false, false, 0);
            _ui.SetHudVisible(true);
            _ui.SetAlert(0f);
            _ui.SetFilm(Film);
            _ui.SetZoom(_zoom);
            _ui.SetTarget(null);

            if (!string.IsNullOrEmpty(_def.tutorialId))
                VNTutorialPlayer.PlayAuto(_def.tutorialId, this);
        }

        /// <summary>正常退出（退出键 / ESC / 被发现之后）</summary>
        public void Close(bool caught)
        {
            if (!_open) return;
            Restore(caught ? 0.5f : 0.35f);
        }

        /// <summary>剧本 Stop / 读档 / 被销毁：瞬间还原，不补间</summary>
        public void ForceClose()
        {
            if (!_open) return;
            Restore(0f);
        }

        void Restore(float duration)
        {
            _open = false;
            _dragging = false;

            var cam = _stage != null ? _stage.vnCamera : null;
            cam?.EndManual(duration);

            if (_kenBurnsWasPlaying && _stage != null && _stage.kenBurns != null)
                _stage.kenBurns.SetPlaying(true);
            _kenBurnsWasPlaying = false;

            Cursor.visible = _cursorWasVisible;

            _ui?.SetHudVisible(false);
            _runner?.SetSecretPhotoActive(false);
        }

        // ==================================================================
        // 每帧
        // ==================================================================

        void Update()
        {
            if (_runner == null || _stage == null || _ui == null) return;

            if (!_open)
            {
                UpdateIcon();
                return;
            }

            if (VNPause.IsPaused) return; // 教程讲解中：不读输入、察觉不涨
            if (_shooting || _caught) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Close(false);
                return;
            }
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
            {
                Shoot();
                return;
            }

            if (mouse != null) ReadMouse(mouse);
            UpdateDetection(VNTime.Delta);
        }

        void UpdateIcon()
        {
            bool unlocked = IsUnlocked;
            bool visible = unlocked && _runner.IsSecretPhotoIconAllowed();
            int film = Film;
            _ui.SetIcon(visible, film > 0 || _def.allowEnterWithoutFilm, film);
            if (unlocked && !_wasUnlocked && visible) _ui.PulseIcon(); // 刚解锁：抖一下
            _wasUnlocked = unlocked;
        }

        void ReadMouse(Mouse mouse)
        {
            Vector2 pos = mouse.position.ReadValue();

            // 滚轮缩放：以指针所指的画布点为中心缩放（该点保持不动）
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                float old = _zoom;
                float next = Mathf.Clamp(_zoom + Mathf.Sign(wheel) * _def.zoomStep, _def.zoomMin, _def.zoomMax);
                if (!Mathf.Approximately(old, next))
                {
                    Vector2 cursorCanvas = _look + ScreenToCanvasOffset(pos) / old;
                    _look += (cursorCanvas - _look) * (1f - old / next);
                    _zoom = next;
                    ApplyView();
                }
            }

            // 左键拖动平移：按下时若压在本层按钮上不算拖动（否则点快门会顺便挪镜头）
            if (mouse.leftButton.wasPressedThisFrame && !_ui.IsPointerOverButton(pos))
            {
                _dragging = true;
                _dragLast = pos;
            }
            if (_dragging)
            {
                if (!mouse.leftButton.isPressed) _dragging = false;
                else
                {
                    Vector2 delta = pos - _dragLast;
                    _dragLast = pos;
                    if (delta.sqrMagnitude > 0f)
                    {
                        float scale = _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;
                        // 内容跟着鼠标走 = 看向点反向移动；缩得越近同样的手势挪得越少
                        _look -= delta / scale / _zoom * _def.panSensitivity;
                        ApplyView();
                    }
                }
            }
        }

        /// <summary>屏幕坐标 → 相对画布中心的偏移（画布像素）</summary>
        Vector2 ScreenToCanvasOffset(Vector2 screenPos)
        {
            float scale = _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;
            return (screenPos - new Vector2(Screen.width, Screen.height) * 0.5f) / scale;
        }

        void ApplyView()
        {
            var cam = _stage.vnCamera;
            _look = VNSecretPhotoRig.ClampLook(_look, _zoom, cam.canvasHalf, EffectiveOverscan(cam));
            cam.SetManualView(_look, _zoom);
            _ui.SetZoom(_zoom);
        }

        /// <summary>
        /// 防露边用的溢出量：按**背景图实际尺寸**算，而不是信 VNCamera.overscan 的 60px。
        /// 场景生成器给背景铺 60px 溢出，但手工搭的场景背景常常正好 1920×1080——
        /// 这时按 60 钳制，推近后往边上一拉就露出一条黑边，而且会被拍进照片里。
        /// </summary>
        Vector2 EffectiveOverscan(VNCamera cam)
        {
            var bg = _stage.backgroundImage != null ? _stage.backgroundImage.rectTransform : null;
            if (bg == null) return cam.overscan;
            var extra = (bg.rect.size - cam.canvasHalf * 2f) * 0.5f;
            extra = Vector2.Max(extra, Vector2.zero);
            return Vector2.Min(extra, cam.overscan);
        }

        // ==================================================================
        // 察觉
        // ==================================================================

        readonly List<Vector2> _centers = new List<Vector2>();
        readonly List<VNStage.ActiveCharacter> _chars = new List<VNStage.ActiveCharacter>();

        void UpdateDetection(float dt)
        {
            var cam = _stage.vnCamera;
            var frame = VNSecretPhotoRig.Frame(_look, _zoom, cam.canvasHalf);

            _chars.Clear();
            _centers.Clear();
            foreach (var c in _stage.ActiveCharacters)
            {
                if (c == null || c.rect == null || c.go == null || !c.go.activeInHierarchy) continue;
                _chars.Add(c);
                _centers.Add(c.rect.anchoredPosition);
            }

            int pick = VNSecretPhotoRig.PickNearest(_look, _centers);
            var target = pick >= 0 ? _chars[pick] : null;
            string id = target != null ? target.def.id : null;
            if (id != _targetId)
            {
                _targetId = id;
                _ui.SetTarget(target != null ? target.def.LocalizedDisplayName : null);
            }
            if (target == null) return; // 风景照：不涨

            var size = Vector2.Scale(target.rect.sizeDelta, (Vector2)target.rect.localScale);
            var charRect = new Rect(target.rect.anchoredPosition - size * 0.5f, size);

            float weight = VNSecretPhotoRig.TargetWeight(frame, charRect, _def.detectEdgeWeight);
            float zf = VNSecretPhotoRig.ZoomFactor(_zoom, _def.zoomMin, _def.zoomMax, _def.detectZoomFactorAtMax);
            int alertFlag = VNFlags.Get(_def.AlertFlagFor(id));
            float rate = VNSecretPhotoRig.DetectionRate(_def.detectBaseRate, weight, zf, alertFlag);

            if (rate <= 0f) return;
            _alert = Mathf.Min(100f, _alert + rate * dt);
            _ui.SetAlert(_alert);
            if (_alert >= 100f) StartCoroutine(CaughtCo(target));
        }

        IEnumerator CaughtCo(VNStage.ActiveCharacter target)
        {
            _caught = true;
            _dragging = false;
            PlayCaughtSound();
            _ui.SetAlert(100f);

            // 她的反应：情绪动作 + 漫符（都是现成组件，一行调用）
            var emote = _def.CaughtEmoteFor(target.def.id);
            // 枚举名 = VNCharacterEmotes 的方法名，执行统一走目录
            if (target.emotes != null && emote != VNSecretPhotoEmote.None)
                VNEmoteCatalog.Invoke(target.emotes, emote.ToString());
            target.marks?.Show(_def.caughtMark, false, null, 1f, 1.6f);

            // 惩罚：好感（走属性系统，带钳制与飘字）+ 警惕永久累积
            string stat = string.IsNullOrEmpty(_def.affectionStat) ? "好感" : _def.affectionStat;
            if (_def.affectionPenalty > 0 && _runner.StatsHud != null)
                _runner.StatsHud.Apply(stat, "-" + _def.affectionPenalty, false, 0);
            VNFlags.Add(_def.AlertFlagFor(target.def.id), Mathf.Max(0, _def.alertGain));

            yield return _ui.CaughtFlashCo();

            string line = _def.PickCaughtLine(target.def.id);
            Close(true);
            // 退出后对话框回来了，把她那句放进去（玩家点一下就继续原剧本）
            _runner.SayOutOfScript(target.def.id, line);
            _caught = false;
        }

        // ==================================================================
        // 快门
        // ==================================================================

        public void Shoot()
        {
            if (!_open || _shooting || _caught || VNPause.IsPaused) return;
            if (Film <= 0)
            {
                VNToast.Show(VNLocale.T("secretphoto.noFilm"));
                return;
            }
            if (VNSecretAlbum.IsFull)
            {
                VNToast.Show(VNLocale.T("secretphoto.albumFull", VNSecretAlbum.Capacity));
                return;
            }
            StartCoroutine(ShootCo());
        }

        IEnumerator ShootCo()
        {
            _shooting = true;
            _dragging = false;

            // 快门按下即扣胶卷（被发现那一下也扣，这是设计）
            VNFlags.Add(_def.FilmFlag, -1);
            _ui.SetFilm(Film);
            _ui.PunchShutter();
            _sfx?.Play(VNPhotoSfx.Kind.Shutter);

            // 拍摄信息（现在只当照片说明；将来做评分/图鉴不用重拍）
            var target = _stage.Get(_targetId);
            string character = target != null ? target.def.id : "";
            string expression = target != null ? target.expression : "";
            string background = _stage.CurrentBackgroundId ?? "";
            int month = VNFlags.Get("月序");
            float zoom = _zoom;

            // 抓屏：HUD 关一帧再抓（VNPhotoCapture 自己处理），带 URP 后处理
            Texture2D shot = null;
            var hide = new List<GameObject> { _ui.HudRoot };
            // 提示卡 / AUTO 角标 / 任务角标那张 Overlay 画布也别入镜；演示场景的底部按键提示同理
            if (VNToast.RootObject != null) hide.Add(VNToast.RootObject);
            var hint = _canvas != null ? _canvas.transform.Find("HintText") : null;
            if (hint != null) hide.Add(hint.gameObject);
            yield return VNPhotoCapture.Capture(_ui.CaptureArea, _canvas, hide, tex => shot = tex);

            StartCoroutine(_ui.FlashCo());

            if (shot == null)
            {
                VNFlags.Add(_def.FilmFlag, 1); // 抓图失败不算，胶卷退回
                _ui.SetFilm(Film);
                VNToast.Show(VNLocale.T("secretphoto.failed"));
                _shooting = false;
                yield break;
            }

            var entry = VNSecretAlbum.Add(shot, character, expression, background, zoom, month);
            if (entry == null)
            {
                VNFlags.Add(_def.FilmFlag, 1);
                _ui.SetFilm(Film);
                VNToast.Show(VNLocale.T("secretphoto.albumFull", VNSecretAlbum.Capacity));
                Destroy(shot);
                _shooting = false;
                yield break;
            }

            _ui.PlayThumbnailFly(shot);
            VNToast.Show(VNLocale.T("secretphoto.saved"));
            _shooting = false;

            // 缩略图飞完再销毁纹理（飞入动画约 1.2 秒）
            yield return new WaitForSecondsRealtime(1.4f);
            if (shot != null) Destroy(shot);
        }

        // ==================================================================
        // 被发现音效（代码合成：两段下行的短促蜂鸣；资产里配了真音效就用真的）
        // ==================================================================

        void PlayCaughtSound()
        {
            if (_caughtSource == null)
            {
                _caughtSource = gameObject.AddComponent<AudioSource>();
                _caughtSource.playOnAwake = false;
                _caughtSource.spatialBlend = 0f;
            }
            if (_caughtClip == null) _caughtClip = _def.caughtClip != null ? _def.caughtClip : BuildCaughtClip();
            float channel = _stage != null && _stage.vnAudio != null ? _stage.vnAudio.seVolume : 1f;
            _caughtSource.PlayOneShot(_caughtClip, 0.8f * channel);
        }

        static AudioClip BuildCaughtClip()
        {
            const int rate = 44100;
            float dur = 0.42f;
            int n = Mathf.RoundToInt(rate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                // 两段：前半 620Hz，后半 440Hz；每段各自快速衰减
                bool second = t >= 0.2f;
                float lt = second ? t - 0.2f : t;
                float freq = second ? 440f : 620f;
                float env = Mathf.Exp(-lt * 14f);
                float s = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t)) * 0.35f   // 方波底
                        + Mathf.Sin(2f * Mathf.PI * freq * t) * 0.65f;
                data[i] = s * env * 0.8f;
            }
            var clip = AudioClip.Create("secretphoto_caught", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
