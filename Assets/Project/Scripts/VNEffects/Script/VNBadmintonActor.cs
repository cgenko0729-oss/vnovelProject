using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>挥拍种类：高手位 / 低手位 / 空中扣杀</summary>
    public enum VNBadmintonSwing { High, Low, Smash }

    /// <summary>
    /// 羽球角色的**表现层**。不是 MonoBehaviour——由 VNBadmintonModule 构造并每帧 Tick。
    ///
    /// 【这一层的存在意义】
    /// 玩法逻辑只跟它要三样东西：站位、拍面点、能不能挥拍。
    /// 怎么画、怎么动全封在这里——将来把程序化假动画换成序列帧 / 骨骼动画，
    /// 只改这个文件，模块一行不用动。
    ///
    /// 【★ 判定几何与动画是分开的】
    /// RacketPointFor() 返回的是**固定几何**（站位 + 拍型对应的固定高度），
    /// 不随挥拍动画的实际角度走。故意的：判定必须可预测、帧率无关，
    /// 动画只负责让这一拍「看起来打到了」。参考实现用碰撞盒时两者是耦合的，
    /// 那正是它需要连续碰撞检测才不漏球的原因。
    ///
    /// 【程序化假动画】（决策 1）
    /// 底图一张静态侧身立绘（缺席时退回剪影小人）+ 一张独立球拍图，
    /// 靠 DOTween / 逐帧姿态插值做挥拍弧线、蹲起、前倾、落地冲击、扣杀残影，
    /// 不依赖任何帧动画资产。
    /// </summary>
    public class VNBadmintonActor
    {
        // ── 尺寸（field 坐标 px）──
        const float BodyHeight = 340f;
        const float PlaceholderWidth = 96f;
        const float ShoulderY = BodyHeight * 0.62f;
        const float ArmLength = 70f;
        const float RacketLength = 92f;

        /// <summary>球拍点相对角色脚下的水平偏移（朝向前方）——判定用固定值</summary>
        const float RacketOffsetX = 62f;
        const float HighRacketY = 300f;
        const float LowRacketY = 150f;
        const float SmashRacketY = 360f;

        /// <summary>拍面有效的时间窗（占挥拍总时长的比例）</summary>
        const float ActiveFrom = 0.20f;
        const float ActiveTo = 0.65f;

        /// <summary>一种挥拍的姿态定义</summary>
        readonly struct SwingDef
        {
            public readonly float duration;
            public readonly float armWindup, armStrike;   // 手臂角度（度，+ = 抬起）
            public readonly float bodyWindup, bodyStrike; // 身体前倾（度）
            public readonly float squashStrike;           // 击球瞬间的纵向压缩

            public SwingDef(float d, float aw, float a2, float bw, float b2, float sq)
            { duration = d; armWindup = aw; armStrike = a2; bodyWindup = bw; bodyStrike = b2; squashStrike = sq; }
        }

        // 高手位：抡到头顶后方再劈下来 / 低手位：从体侧低点向上撩 / 扣杀：更大更快更狠
        static readonly SwingDef SwingHigh = new SwingDef(0.42f, 125f, -30f, -7f, 9f, 1.00f);
        static readonly SwingDef SwingLow = new SwingDef(0.40f, -95f, 45f, 4f, -6f, 0.93f);
        static readonly SwingDef SwingSmash = new SwingDef(0.46f, 150f, -70f, -12f, 14f, 1.06f);

        const float IdleArmAngle = -35f;

        public RectTransform Root { get; private set; }
        public bool FacingRight { get; private set; }
        public Vector2 Foot => Root != null ? Root.anchoredPosition : Vector2.zero;

        RectTransform _pivot;        // 承载前倾 / 蹲起 / 朝向翻转
        RectTransform _bodyRect, _headRect, _legBack, _legFront;
        RectTransform _armPivot, _racketRect, _shadow;
        Image _bodyImage, _shadowImage;
        Sprite _bodySprite;
        Color _tint;

        RectTransform _talkBox;
        TextMeshProUGUI _talkText;
        Sequence _talkSeq;

        float _swingTime = -1f;
        VNBadmintonSwing _swingKind;
        SwingDef _swing;
        float _idlePhase, _legPhase, _lean, _landSquash;
        float _lastAir;
        GameObject _linkTarget;

        public bool Swinging => _swingTime >= 0f;
        public bool CanSwing => _swingTime < 0f;
        public VNBadmintonSwing CurrentSwing => _swingKind;

        /// <summary>本拍的拍面此刻是否有效（击球判定的时间窗）</summary>
        public bool RacketActive => _swingTime >= 0f &&
            _swingTime >= _swing.duration * ActiveFrom &&
            _swingTime <= _swing.duration * ActiveTo;

        /// <summary>拍面有效窗最早出现在挥拍后多少秒（AI 用它算提前量）</summary>
        public static float ActiveWindowStart(VNBadmintonSwing kind) =>
            DefOf(kind).duration * ActiveFrom;

        static SwingDef DefOf(VNBadmintonSwing kind) => kind switch
        {
            VNBadmintonSwing.Low => SwingLow,
            VNBadmintonSwing.Smash => SwingSmash,
            _ => SwingHigh,
        };

        /// <summary>指定拍型时的拍面中心（field 坐标）。判定专用的固定几何。</summary>
        public Vector2 RacketPointFor(VNBadmintonSwing kind)
        {
            float dir = FacingRight ? 1f : -1f;
            float h = kind switch
            {
                VNBadmintonSwing.Low => LowRacketY,
                VNBadmintonSwing.Smash => SmashRacketY,
                _ => HighRacketY,
            };
            return Foot + new Vector2(RacketOffsetX * dir, h);
        }

        public Vector2 RacketPoint => RacketPointFor(_swingKind);

        // ------------------------------------------------------------------
        // 搭建
        // ------------------------------------------------------------------

        public void Build(RectTransform parent, Sprite body, Sprite racket, Sprite arm,
            Color placeholderTint, bool facingRight, GameObject linkTarget)
        {
            _linkTarget = linkTarget;
            FacingRight = facingRight;
            _bodySprite = body;
            _tint = placeholderTint;

            Root = VNBadmintonUi.CreateNode("Actor", parent);
            VNBadmintonUi.AnchorBottomCenter(Root);
            Root.pivot = new Vector2(0.5f, 0f);
            Root.sizeDelta = Vector2.zero;

            BuildShadow();

            _pivot = VNBadmintonUi.CreateNode("Pivot", Root);
            _pivot.anchorMin = _pivot.anchorMax = new Vector2(0.5f, 0f);
            _pivot.pivot = new Vector2(0.5f, 0f);
            _pivot.anchoredPosition = Vector2.zero;
            _pivot.sizeDelta = Vector2.zero;

            if (body == null) BuildLegs();
            BuildBody(body);
            BuildArm(racket, arm);
            BuildTalkBubble();

            ApplyFacing();
        }

        void BuildShadow()
        {
            _shadow = VNBadmintonUi.CreateImage("Shadow", Root,
                VNProceduralTextures.RadialGlowSprite, new Color(0f, 0f, 0f, 0.32f));
            _shadow.anchorMin = _shadow.anchorMax = new Vector2(0.5f, 0f);
            _shadow.pivot = new Vector2(0.5f, 0.5f);
            _shadow.anchoredPosition = new Vector2(0f, 6f);
            _shadow.sizeDelta = new Vector2(190f, 54f);
            _shadowImage = _shadow.GetComponent<Image>();
        }

        void BuildLegs()
        {
            _legBack = MakeLimb("LegBack", Color.Lerp(_tint, Color.black, 0.28f), 24f, 132f);
            _legBack.anchoredPosition = new Vector2(-16f, 132f);
            _legFront = MakeLimb("LegFront", _tint, 24f, 132f);
            _legFront.anchoredPosition = new Vector2(16f, 132f);
        }

        RectTransform MakeLimb(string name, Color color, float w, float h)
        {
            var rect = VNBadmintonUi.CreateImage(name, _pivot,
                VNProceduralTextures.RoundedRectSprite, color);
            rect.GetComponent<Image>().type = Image.Type.Sliced;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);   // 以髋部为轴摆动
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        void BuildBody(Sprite body)
        {
            _bodyRect = VNBadmintonUi.CreateImage("Body", _pivot,
                body != null ? body : VNProceduralTextures.RoundedRectSprite,
                body != null ? Color.white : _tint);
            _bodyRect.anchorMin = _bodyRect.anchorMax = new Vector2(0.5f, 0f);
            _bodyRect.pivot = new Vector2(0.5f, 0f);
            _bodyImage = _bodyRect.GetComponent<Image>();

            if (body != null)
            {
                // 立绘：脚底贴齐地面线，按高度等比缩放（美术需求清单第八节的约定）
                _bodyImage.preserveAspect = true;
                float aspect = body.rect.width / Mathf.Max(1f, body.rect.height);
                _bodyRect.sizeDelta = new Vector2(BodyHeight * aspect, BodyHeight);
                _bodyRect.anchoredPosition = Vector2.zero;
                return;
            }

            // 占位剪影：躯干（腿已单独画）+ 头
            _bodyImage.type = Image.Type.Sliced;
            _bodyRect.sizeDelta = new Vector2(PlaceholderWidth, BodyHeight * 0.46f);
            _bodyRect.anchoredPosition = new Vector2(0f, BodyHeight * 0.30f);

            _headRect = VNBadmintonUi.CreateImage("Head", _pivot,
                VNProceduralTextures.RoundedRectSprite, Color.Lerp(_tint, Color.white, 0.18f));
            _headRect.GetComponent<Image>().type = Image.Type.Sliced;
            _headRect.anchorMin = _headRect.anchorMax = new Vector2(0.5f, 0f);
            _headRect.pivot = new Vector2(0.5f, 0f);
            _headRect.anchoredPosition = new Vector2(6f, BodyHeight * 0.76f);
            _headRect.sizeDelta = new Vector2(80f, 80f);
        }

        void BuildArm(Sprite racket, Sprite arm)
        {
            // 手臂枢轴放在肩膀：转它 = 球拍绕肩画弧，即使没有手臂图也读得出「挥」
            _armPivot = VNBadmintonUi.CreateNode("ArmPivot", _pivot);
            _armPivot.anchorMin = _armPivot.anchorMax = new Vector2(0.5f, 0f);
            _armPivot.pivot = new Vector2(0.5f, 0.5f);
            _armPivot.anchoredPosition = new Vector2(8f, ShoulderY);
            _armPivot.sizeDelta = Vector2.zero;

            if (arm != null || _bodySprite == null)
            {
                var armRect = VNBadmintonUi.CreateImage("Arm", _armPivot,
                    arm != null ? arm : VNProceduralTextures.RoundedRectSprite,
                    arm != null ? Color.white : Color.Lerp(_tint, Color.white, 0.1f));
                if (arm == null) armRect.GetComponent<Image>().type = Image.Type.Sliced;
                else armRect.GetComponent<Image>().preserveAspect = true;
                armRect.anchorMin = armRect.anchorMax = new Vector2(0.5f, 0.5f);
                armRect.pivot = new Vector2(0f, 0.5f);   // 从肩往外伸
                armRect.anchoredPosition = Vector2.zero;
                armRect.sizeDelta = new Vector2(ArmLength, 20f);
            }

            // 球拍：拍柄末端为旋转轴（美术需求：拍柄画在图片左下角）
            _racketRect = VNBadmintonUi.CreateImage("Racket", _armPivot, racket,
                racket != null ? Color.white : new Color(0.93f, 0.90f, 0.70f, 1f));
            _racketRect.anchorMin = _racketRect.anchorMax = new Vector2(0.5f, 0.5f);
            _racketRect.pivot = new Vector2(0f, 0.5f);
            _racketRect.anchoredPosition = new Vector2(ArmLength - 6f, 0f);

            if (racket != null)
            {
                _racketRect.sizeDelta = new Vector2(RacketLength, RacketLength);
                _racketRect.GetComponent<Image>().preserveAspect = true;
                return;
            }

            // 占位球拍：拍杆 + 空心拍面（RoundedFrameSprite 正好是个空心圆角框）
            _racketRect.sizeDelta = new Vector2(RacketLength * 0.5f, 9f);
            var head = VNBadmintonUi.CreateImage("RacketHead", _racketRect,
                VNProceduralTextures.RoundedFrameSprite, new Color(0.98f, 0.97f, 0.88f, 1f));
            head.GetComponent<Image>().type = Image.Type.Sliced;
            head.anchorMin = head.anchorMax = new Vector2(1f, 0.5f);
            head.pivot = new Vector2(0f, 0.5f);
            head.anchoredPosition = Vector2.zero;
            head.sizeDelta = new Vector2(RacketLength * 0.52f, 46f);
        }

        void BuildTalkBubble()
        {
            _talkBox = VNBadmintonUi.CreateImage("Talk", Root,
                VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.96f));
            _talkBox.GetComponent<Image>().type = Image.Type.Sliced;
            _talkBox.anchorMin = _talkBox.anchorMax = new Vector2(0.5f, 0f);
            _talkBox.pivot = new Vector2(0.5f, 0f);
            _talkBox.anchoredPosition = new Vector2(0f, BodyHeight + 24f);
            _talkBox.sizeDelta = new Vector2(300f, 66f);

            _talkText = VNBadmintonUi.CreateText("TalkTxt", _talkBox, 28,
                new Color(0.15f, 0.15f, 0.2f, 1f), "");
            var tr = VNBadmintonUi.Rect(_talkText);
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(14f, 6f); tr.offsetMax = new Vector2(-14f, -6f);

            _talkBox.localScale = Vector3.zero;
            _talkBox.gameObject.SetActive(false);
        }

        void ApplyFacing() => _pivot.localScale = new Vector3(FacingRight ? 1f : -1f, 1f, 1f);

        // ------------------------------------------------------------------
        // 对外动作
        // ------------------------------------------------------------------

        public void SetPosition(float x, float y)
        {
            if (Root != null) Root.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>移动输入 −1/0/+1：影响前倾与迈步</summary>
        public void SetMoveInput(float dir)
        {
            float clamped = Mathf.Clamp(dir, -1f, 1f);
            _lean = Mathf.Lerp(_lean, -clamped * 9f * (FacingRight ? 1f : -1f), 0.2f);
            _legPhase += Mathf.Abs(clamped) * 0.35f;
        }

        /// <summary>起一拍。挥拍中重复调用无效（防连点刷拍）。</summary>
        public bool PlaySwing(VNBadmintonSwing kind)
        {
            if (!CanSwing) return false;
            _swingKind = kind;
            _swing = DefOf(kind);
            _swingTime = 0f;
            if (kind == VNBadmintonSwing.Smash) SpawnAfterImages();
            return true;
        }

        /// <summary>落地：压一下 + 脚影摊开（参考 VNFootShadow.Impact 的处理）</summary>
        public void Land()
        {
            _landSquash = 1f;
            if (_shadow == null) return;
            _shadow.DOKill();
            _shadow.localScale = new Vector3(1.35f, 0.7f, 1f);
            _shadow.DOScale(1f, 0.25f).SetEase(Ease.OutBack)
                   .SetUpdate(true).SetLink(_linkTarget);
        }

        /// <summary>台词气泡。同侧未消失时不叠加；rate 是触发概率。</summary>
        public bool ShowTalk(string message, float rate, System.Random rng)
        {
            if (string.IsNullOrEmpty(message) || _talkBox == null) return false;
            if (_talkSeq != null && _talkSeq.IsActive() && _talkSeq.IsPlaying()) return false;
            if (rng != null && rng.NextDouble() > rate) return false;

            _talkText.text = message;
            // 气泡宽度跟着台词长度走，否则长句会溢出固定框
            float wanted = _talkText.GetPreferredValues(message).x + 40f;
            _talkBox.sizeDelta = new Vector2(Mathf.Clamp(wanted, 160f, 620f), 66f);
            _talkBox.gameObject.SetActive(true);
            _talkBox.localScale = Vector3.zero;

            _talkSeq = DOTween.Sequence();
            _talkSeq.Append(_talkBox.DOScale(1f, 0.18f).SetEase(Ease.OutBack));
            _talkSeq.AppendInterval(1.4f);
            _talkSeq.Append(_talkBox.DOScale(0f, 0.15f).SetEase(Ease.InBack));
            _talkSeq.AppendCallback(() =>
            {
                if (_talkBox != null) _talkBox.gameObject.SetActive(false);
            });
            _talkSeq.SetUpdate(true).SetLink(_linkTarget);
            return true;
        }

        /// <summary>扣杀残影：三张渐隐的身体副本</summary>
        void SpawnAfterImages()
        {
            for (int i = 0; i < 3; i++)
            {
                float delay = i * 0.05f;
                DOVirtual.DelayedCall(delay, () =>
                {
                    if (Root == null || Root.parent == null) return;

                    var ghost = VNBadmintonUi.CreateImage("Ghost",
                        (RectTransform)Root.parent,
                        _bodySprite != null ? _bodySprite : VNProceduralTextures.RoundedRectSprite,
                        _bodySprite != null ? new Color(1f, 1f, 1f, 0.28f)
                                            : new Color(_tint.r, _tint.g, _tint.b, 0.26f));
                    VNBadmintonUi.AnchorBottomCenter(ghost);
                    ghost.pivot = new Vector2(0.5f, 0f);
                    ghost.anchoredPosition = Foot;
                    ghost.sizeDelta = _bodyRect.sizeDelta;
                    ghost.localScale = new Vector3(FacingRight ? 1f : -1f, 1f, 1f);
                    var img = ghost.GetComponent<Image>();
                    if (_bodySprite != null) img.preserveAspect = true;
                    else img.type = Image.Type.Sliced;
                    ghost.SetAsFirstSibling();   // 压在本体后面

                    img.DOFade(0f, 0.28f).SetUpdate(true).SetLink(_linkTarget)
                       .OnComplete(() => { if (ghost != null) Object.Destroy(ghost.gameObject); });
                }, true).SetLink(_linkTarget);
            }
        }

        // ------------------------------------------------------------------
        // 每帧姿态
        // ------------------------------------------------------------------

        /// <param name="airHeight">离地高度（0 = 站在地上）</param>
        public void Tick(float dt, float airHeight)
        {
            if (_pivot == null) return;

            _idlePhase += dt;
            if (_lastAir > 1f && airHeight <= 1f) Land();
            _lastAir = airHeight;

            UpdateShadow(airHeight);

            float armAngle;
            float bodyRot = _lean;
            float squash = 1f;

            if (_swingTime >= 0f)
            {
                _swingTime += dt;
                float p = Mathf.Clamp01(_swingTime / _swing.duration);

                // 蓄力(0~0.28) → 抽击(0.28~0.52) → 收拍(0.52~1)
                if (p < 0.28f)
                {
                    float k = Ease01(p / 0.28f);
                    armAngle = Mathf.Lerp(IdleArmAngle, _swing.armWindup, k);
                    bodyRot += Mathf.Lerp(0f, _swing.bodyWindup, k);
                    squash = 1f;
                }
                else if (p < 0.52f)
                {
                    float k = (p - 0.28f) / 0.24f;
                    k = k * k;   // 抽击加速，越到后面越快
                    armAngle = Mathf.Lerp(_swing.armWindup, _swing.armStrike, k);
                    bodyRot += Mathf.Lerp(_swing.bodyWindup, _swing.bodyStrike, k);
                    squash = Mathf.Lerp(1f, _swing.squashStrike, k);
                }
                else
                {
                    float k = Ease01((p - 0.52f) / 0.48f);
                    armAngle = Mathf.Lerp(_swing.armStrike, IdleArmAngle, k);
                    bodyRot += Mathf.Lerp(_swing.bodyStrike, 0f, k);
                    squash = Mathf.Lerp(_swing.squashStrike, 1f, k);
                }

                if (_swingTime >= _swing.duration) _swingTime = -1f;
            }
            else if (airHeight > 1f)
            {
                // 空中：举拍待机、身体后仰
                armAngle = 95f;
                bodyRot += -6f;
                squash = 1.04f;
            }
            else
            {
                armAngle = IdleArmAngle + Mathf.Sin(_idlePhase * 2.2f) * 4f;
            }

            // 落地缓冲
            if (_landSquash > 0f)
            {
                _landSquash = Mathf.Max(0f, _landSquash - dt * 5f);
                squash *= 1f - 0.12f * _landSquash;
            }

            // 呼吸
            squash *= 1f + Mathf.Sin(_idlePhase * 2.6f) * 0.012f;

            float flip = FacingRight ? 1f : -1f;
            _pivot.localScale = new Vector3(flip * (2f - squash), squash, 1f);
            _pivot.localRotation = Quaternion.Euler(0f, 0f, bodyRot);
            _armPivot.localRotation = Quaternion.Euler(0f, 0f, armAngle);

            UpdateLegs(airHeight);
            // 气泡挂在 Root 而不是 Pivot 下，所以不会跟着翻转/倾斜/呼吸，不用额外处理
        }

        void UpdateLegs(float airHeight)
        {
            if (_legBack == null) return;
            if (airHeight > 1f)
            {
                _legBack.localRotation = Quaternion.Euler(0f, 0f, 22f);
                _legFront.localRotation = Quaternion.Euler(0f, 0f, -14f);
                return;
            }
            float a = Mathf.Sin(_legPhase * 2f) * 20f;
            _legBack.localRotation = Quaternion.Euler(0f, 0f, a);
            _legFront.localRotation = Quaternion.Euler(0f, 0f, -a);
        }

        void UpdateShadow(float airHeight)
        {
            if (_shadowImage == null) return;
            float k = Mathf.Clamp01(1f - airHeight / 320f);
            _shadow.sizeDelta = new Vector2(190f * Mathf.Lerp(0.55f, 1f, k),
                                            54f * Mathf.Lerp(0.55f, 1f, k));
            var c = _shadowImage.color;
            _shadowImage.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.14f, 0.32f, k));
        }

        static float Ease01(float t) => 1f - (1f - t) * (1f - t);

        public void Dispose()
        {
            _talkSeq?.Kill();
            if (Root != null)
            {
                Root.DOKill();
                Object.Destroy(Root.gameObject);
                Root = null;
            }
        }
    }
}
