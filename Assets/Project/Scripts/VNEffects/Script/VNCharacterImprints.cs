using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 立绘痕迹层：在立绘上的指定位置印一枚痕迹图（掌印 / 口红印 / 绳痕…），
    /// 随时间褪色并自行消失。
    ///
    /// 它是三样现成东西的拼接：
    ///   · 定位与挂载 —— 抄 <see cref="VNCharacterMarks"/>：挂在立绘下、归一化坐标、
    ///     共用立绘的材质实例（出场溶解/调色/震动自动带上痕迹）
    ///   · 生命周期   —— 抄 VNWetScreen 的水渍：印上→保持褪色→淡出，对象池 + 数量上限
    ///   · 强度语义   —— 同 <see cref="VNCharacterOverlay"/>：颜色而非 alpha 主导「淡」的观感
    ///
    /// **痕迹是临时演出，不进存档**（同 mark 的一次性符号）：它自己会消退，
    /// 互动结束时也会被清掉。要「带进后续剧情的印子」是另一回事，那得走存档。
    ///
    /// 褪色不是单纯 alpha 淡出：红→粉→无（饱和度衰减 + alpha）比纯透明化真实得多。
    /// </summary>
    public class VNCharacterImprints : MonoBehaviour
    {
        /// <summary>同屏痕迹上限。连点会瞬间生成几十个 Image，
        /// 不设上限就是 draw call 与 GC 双爆；超了淘汰最旧的一枚。</summary>
        public const int MaxLive = 20;

        class Live
        {
            public GameObject go;
            public Image image;
            public RectTransform rect;
            public Sequence seq;
            public string zoneId;
        }

        readonly List<Live> _live = new List<Live>();
        VNCharacterDef _definition;
        Material _shared;
        RectTransform _host;

        public void Initialize(RectTransform host, VNCharacterDef definition, Material sharedMaterial)
        {
            _host = host;
            _definition = definition;
            _shared = sharedMaterial;
        }

        /// <summary>本角色是否登记过这个痕迹 id（Lint 与命令层用）</summary>
        public bool Has(string id) => FindSprite(id) != null;

        /// <summary>
        /// 印一枚痕迹。
        /// pos = 立绘归一化坐标（-0.5~0.5，(0,0) 是立绘中心，与部位框同一套）。
        /// life = 从印上到完全消失的总时长（秒）。
        /// </summary>
        public bool Add(string id, Vector2 pos, float sizeMultiplier, float life,
            float rotation, string zoneId = null)
        {
            var sprite = FindSprite(id);
            if (sprite == null || _host == null) return false;

            var def = FindDef(id);
            while (_live.Count >= MaxLive) KillOldest();

            var go = new GameObject("Imprint_" + id,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_host, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            // 钳到立绘范围内：正常流程里点击必定落在某个部位内，不会越界，
            // 但坐标来自外部（剧本手写的 pos: / 占位符），钳一下免得印到立绘外面去
            pos = new Vector2(Mathf.Clamp(pos.x, -0.5f, 0.5f),
                              Mathf.Clamp(pos.y, -0.5f, 0.5f));

            Vector2 hostSize = _host.rect.size;
            rect.anchoredPosition = new Vector2(pos.x * hostSize.x, pos.y * hostSize.y);

            // 基准边长 = 立绘显示高度 × 该痕迹的基准比例 × 剧本倍率
            float baseSize = hostSize.y * (def != null ? def.baseScale : 0.16f) * sizeMultiplier;
            float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
            rect.sizeDelta = new Vector2(baseSize * aspect, baseSize);
            float jitter = def != null ? def.randomRotation : 12f;
            rect.localRotation = Quaternion.Euler(0f, 0f,
                rotation + Random.Range(-jitter, jitter));

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;      // 绝不能吃射线，否则挡住继续抚摸
            image.preserveAspect = true;
            if (_shared != null) image.material = _shared;

            Color tint = def != null ? def.tint : new Color(1f, 0.35f, 0.4f, 0.75f);
            image.color = tint;

            var live = new Live { go = go, image = image, rect = rect, zoneId = zoneId };
            _live.Add(live);

            // life <= 0 = 用资产里配的默认时长（剧本不写 life: 时就是这条路）
            if (life <= 0.01f) life = def != null ? def.life : 8f;
            life = Mathf.Max(0.5f, life);
            float pressIn = 0.14f;
            float fadeOut = Mathf.Min(1.2f, life * 0.35f);
            float hold = Mathf.Max(0.1f, life - pressIn - fadeOut);

            // 三段：拍上去的力道 → 缓慢褪色（红→粉）→ 淡出
            Color faded = new Color(tint.r, Mathf.Lerp(tint.g, 1f, 0.55f),
                                    Mathf.Lerp(tint.b, 1f, 0.55f), tint.a * 0.55f);
            rect.localScale = Vector3.one * 1.18f;

            live.seq = DOTween.Sequence()
                .Append(rect.DOScale(1f, pressIn).SetEase(Ease.OutBack))
                .Append(image.DOColor(faded, hold).SetEase(Ease.InQuad))
                .Append(image.DOFade(0f, fadeOut).SetEase(Ease.InQuad))
                .OnComplete(() => Remove(live))
                .SetUpdate(true)
                .SetLink(go);
            return true;
        }

        /// <summary>清掉全部痕迹（互动结束 / 角色退场）</summary>
        public void ClearAll(float fade = 0.25f)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var live = _live[i];
                live.seq?.Kill();
                if (fade <= 0.01f) { Remove(live); continue; }
                live.image.DOFade(0f, fade).SetUpdate(true).SetLink(live.go)
                    .OnComplete(() => Remove(live));
            }
        }

        /// <summary>清掉某个部位上的痕迹</summary>
        public void ClearZone(string zoneId, float fade = 0.25f)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                if (_live[i].zoneId != zoneId) continue;
                var live = _live[i];
                live.seq?.Kill();
                live.image.DOFade(0f, fade).SetUpdate(true).SetLink(live.go)
                    .OnComplete(() => Remove(live));
            }
        }

        public int LiveCount => _live.Count;

        void KillOldest()
        {
            if (_live.Count == 0) return;
            var live = _live[0];
            live.seq?.Kill();
            Remove(live);
        }

        void Remove(Live live)
        {
            _live.Remove(live);
            if (live.go != null) Destroy(live.go);
        }

        Sprite FindSprite(string id) => FindDef(id)?.sprite;

        VNCharacterDef.ImprintDef FindDef(string id)
        {
            if (_definition == null || _definition.imprints == null ||
                string.IsNullOrEmpty(id)) return null;
            foreach (var d in _definition.imprints)
                if (d != null && d.id == id && d.sprite != null) return d;
            return null;
        }

        void OnDestroy() => _live.Clear();
    }
}
