using DG.Tweening;
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
    /// 玩法逻辑只跟它要三样东西：站位 x、球拍点、能不能挥拍。
    /// 怎么画、怎么动全封在这里——将来把程序化假动画换成序列帧 / 骨骼动画，
    /// 只改这个文件，模块一行不用动。
    ///
    /// 程序化假动画（P2 决策）：底图一张静态侧身立绘 + 一张独立球拍图，
    /// 靠 DOTween 做旋转 / 蹲起 / 前倾 / 呼吸，不依赖任何帧动画资产。
    /// 立绘缺席时退回剪影色块，玩法完全可玩。
    /// </summary>
    public class VNBadmintonActor
    {
        // ── 表现参数（field 坐标 px）──
        const float BodyHeight = 340f;
        const float PlaceholderWidth = 108f;
        const float RacketLength = 96f;
        /// <summary>球拍点相对角色脚下的水平偏移（朝向前方）</summary>
        const float RacketOffsetX = 62f;
        /// <summary>三种挥拍的拍面高度（相对脚底）</summary>
        const float HighRacketY = 300f;
        const float LowRacketY = 150f;
        const float SmashRacketY = 360f;
        /// <summary>挥拍总时长与「拍面有效」的时间窗</summary>
        const float SwingDuration = 0.42f;
        const float SwingActiveFrom = 0.08f;
        const float SwingActiveTo = 0.28f;

        public RectTransform Root { get; private set; }
        /// <summary>朝向：true = 面向右（左侧角色）</summary>
        public bool FacingRight { get; private set; }
        /// <summary>脚下位置（field 坐标）</summary>
        public Vector2 Foot => Root != null ? Root.anchoredPosition : Vector2.zero;

        RectTransform _pivot;      // 承载倾斜/蹲起的中间层
        RectTransform _bodyRect;
        RectTransform _racketRect;
        RectTransform _shadow;
        Image _bodyImage;

        float _swingTime = -1f;    // <0 = 未挥拍
        VNBadmintonSwing _swingKind;
        float _idlePhase;
        float _lean;               // 当前前倾角（度）
        GameObject _linkTarget;

        /// <summary>正在挥拍中（拍面可能有效）</summary>
        public bool Swinging => _swingTime >= 0f;
        /// <summary>能不能起新的一拍</summary>
        public bool CanSwing => _swingTime < 0f;
        /// <summary>本拍的拍面此刻是否有效（击球判定的时间窗）</summary>
        public bool RacketActive =>
            _swingTime >= SwingActiveFrom && _swingTime <= SwingActiveTo;
        public VNBadmintonSwing CurrentSwing => _swingKind;

        /// <summary>指定拍型时的拍面中心（field 坐标）。空中扣杀取更高的位置。</summary>
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

        /// <summary>当前这一拍的拍面中心</summary>
        public Vector2 RacketPoint => RacketPointFor(_swingKind);

        public void Build(RectTransform parent, Sprite body, Sprite racket,
            Color placeholderTint, bool facingRight, GameObject linkTarget)
        {
            _linkTarget = linkTarget;
            FacingRight = facingRight;

            Root = VNBadmintonUi.CreateNode("Actor", parent);
            VNBadmintonUi.AnchorBottomCenter(Root);
            Root.pivot = new Vector2(0.5f, 0f);
            Root.sizeDelta = Vector2.zero;

            // 脚影（先画，压在角色下面）
            _shadow = VNBadmintonUi.CreateImage("Shadow", Root,
                VNProceduralTextures.RadialGlowSprite, new Color(0f, 0f, 0f, 0.32f));
            _shadow.anchorMin = _shadow.anchorMax = new Vector2(0.5f, 0f);
            _shadow.pivot = new Vector2(0.5f, 0.5f);
            _shadow.anchoredPosition = new Vector2(0f, 6f);
            _shadow.sizeDelta = new Vector2(190f, 54f);

            _pivot = VNBadmintonUi.CreateNode("Pivot", Root);
            _pivot.anchorMin = _pivot.anchorMax = new Vector2(0.5f, 0f);
            _pivot.pivot = new Vector2(0.5f, 0f);
            _pivot.anchoredPosition = Vector2.zero;
            _pivot.sizeDelta = Vector2.zero;

            BuildBody(body, placeholderTint);
            BuildRacket(racket, placeholderTint);
            ApplyFacing();
        }

        void BuildBody(Sprite body, Color tint)
        {
            _bodyRect = VNBadmintonUi.CreateImage("Body", _pivot,
                body != null ? body : VNProceduralTextures.RoundedRectSprite,
                body != null ? Color.white : tint);
            _bodyRect.anchorMin = _bodyRect.anchorMax = new Vector2(0.5f, 0f);
            _bodyRect.pivot = new Vector2(0.5f, 0f);
            _bodyRect.anchoredPosition = Vector2.zero;
            _bodyImage = _bodyRect.GetComponent<Image>();

            if (body != null)
            {
                _bodyImage.preserveAspect = true;
                float aspect = body.rect.width / Mathf.Max(1f, body.rect.height);
                _bodyRect.sizeDelta = new Vector2(BodyHeight * aspect, BodyHeight);
            }
            else
            {
                _bodyImage.type = Image.Type.Sliced;
                _bodyRect.sizeDelta = new Vector2(PlaceholderWidth, BodyHeight * 0.78f);

                var head = VNBadmintonUi.CreateImage("Head", _pivot,
                    VNProceduralTextures.RoundedRectSprite, tint);
                head.GetComponent<Image>().type = Image.Type.Sliced;
                head.anchorMin = head.anchorMax = new Vector2(0.5f, 0f);
                head.pivot = new Vector2(0.5f, 0f);
                head.anchoredPosition = new Vector2(0f, BodyHeight * 0.76f);
                head.sizeDelta = new Vector2(86f, 86f);
            }
        }

        void BuildRacket(Sprite racket, Color tint)
        {
            // 球拍以「拍柄末端」为旋转轴（美术需求：拍柄画在图片左下角）
            _racketRect = VNBadmintonUi.CreateImage("Racket", _pivot,
                racket, racket != null ? Color.white
                    : new Color(0.92f, 0.90f, 0.72f, 1f));
            _racketRect.anchorMin = _racketRect.anchorMax = new Vector2(0.5f, 0f);
            _racketRect.pivot = new Vector2(0f, 0.5f);
            _racketRect.anchoredPosition = new Vector2(18f, BodyHeight * 0.55f);
            _racketRect.sizeDelta = racket != null
                ? new Vector2(RacketLength, RacketLength)
                : new Vector2(RacketLength, 12f);
            if (racket != null) _racketRect.GetComponent<Image>().preserveAspect = true;
            _racketRect.localRotation = Quaternion.Euler(0f, 0f, -35f);
        }

        void ApplyFacing()
        {
            float sx = FacingRight ? 1f : -1f;
            _pivot.localScale = new Vector3(sx, 1f, 1f);
        }

        public void SetPosition(float x, float y)
        {
            if (Root != null) Root.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>移动输入 −1/0/+1，只影响前倾表现</summary>
        public void SetMoveInput(float dir)
        {
            float target = -Mathf.Clamp(dir, -1f, 1f) * 9f * (FacingRight ? 1f : -1f);
            _lean = Mathf.Lerp(_lean, target, 0.2f);
        }

        /// <summary>起一拍。挥拍中重复调用无效（防连点刷拍）。</summary>
        public bool PlaySwing(VNBadmintonSwing kind)
        {
            if (!CanSwing) return false;
            _swingKind = kind;
            _swingTime = 0f;
            return true;
        }

        /// <summary>脚影随高度收缩（跳起时变小变淡）</summary>
        void UpdateShadow(float airHeight)
        {
            if (_shadow == null) return;
            float k = Mathf.Clamp01(1f - airHeight / 320f);
            _shadow.sizeDelta = new Vector2(190f * Mathf.Lerp(0.55f, 1f, k),
                                            54f * Mathf.Lerp(0.55f, 1f, k));
            var img = _shadow.GetComponent<Image>();
            var c = img.color;
            img.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.14f, 0.32f, k));
        }

        /// <param name="airHeight">离地高度（0 = 站在地上）</param>
        public void Tick(float dt, float airHeight)
        {
            if (_pivot == null) return;

            _idlePhase += dt;
            UpdateShadow(airHeight);

            // 挥拍进度
            float swingAngle = 0f;
            float crouch = 0f;
            if (_swingTime >= 0f)
            {
                _swingTime += dt;
                float p = Mathf.Clamp01(_swingTime / SwingDuration);
                // 蓄力(0~0.25) → 抽击(0.25~0.55) → 收拍(0.55~1)
                float baseA = _swingKind switch
                {
                    VNBadmintonSwing.Low => -110f,
                    VNBadmintonSwing.Smash => 150f,
                    _ => 120f,
                };
                if (p < 0.25f) swingAngle = Mathf.Lerp(0f, -baseA * 0.35f, p / 0.25f);
                else if (p < 0.55f) swingAngle = Mathf.Lerp(-baseA * 0.35f, baseA, (p - 0.25f) / 0.3f);
                else swingAngle = Mathf.Lerp(baseA, 0f, (p - 0.55f) / 0.45f);

                if (_swingTime >= SwingDuration) _swingTime = -1f;
            }
            else
            {
                // 待机：球拍轻微浮动
                swingAngle = Mathf.Sin(_idlePhase * 2.2f) * 4f;
            }

            // 落地缓冲：刚落地时压一下
            if (airHeight <= 0.5f) crouch = Mathf.Sin(_idlePhase * 2.6f) * 0.012f;

            float breathe = 1f + Mathf.Sin(_idlePhase * 2.6f) * 0.012f + crouch;
            _pivot.localScale = new Vector3((FacingRight ? 1f : -1f) * (2f - breathe),
                                            breathe, 1f);
            _pivot.localRotation = Quaternion.Euler(0f, 0f, _lean);
            _racketRect.localRotation = Quaternion.Euler(0f, 0f, -35f + swingAngle);
        }

        public void Dispose()
        {
            if (Root != null)
            {
                Root.DOKill();
                Object.Destroy(Root.gameObject);
                Root = null;
            }
        }
    }
}
