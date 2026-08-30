using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>部位区域的形状。归一化空间里 x/y 不等比（立绘通常是竖长的），
    /// 所以「圆」实际上是贴着框长宽的椭圆——编辑器画布与运行时用同一套数学，
    /// 所见即所得，不必纠结它在像素空间是不是正圆。</summary>
    public enum VNZoneShape
    {
        [InspectorName("矩形")] Rect = 0,
        [InspectorName("圆 / 椭圆")] Ellipse = 1,
        [InspectorName("胶囊（圆角矩形）")] Capsule = 2,
    }

    /// <summary>
    /// 一个可互动部位。坐标全部是**相对立绘 RectTransform 的归一化值**：
    /// (0,0) = 立绘中心，x/y 各取 -0.5 ~ +0.5 —— 与 VNCharacterDef.markAnchor
    /// 完全同一套语义，所以漫符位置和部位框可以互相参照着调。
    /// </summary>
    [Serializable]
    public class VNTouchZone
    {
        [Header("部位 id（剧本与互动规则里引用，如 头 / 脸 / 手）")]
        public string id;

        [Header("显示名（UI 提示用；留空则显示 id）")]
        public string displayName;

        [Header("形状")]
        public VNZoneShape shape = VNZoneShape.Ellipse;

        [Header("中心（归一化，(0,0) = 立绘中心，(0.5,0.5) = 右上角）")]
        public Vector2 center;

        [Header("尺寸（归一化的宽高，0.2 = 立绘宽/高的两成）")]
        public Vector2 size = new Vector2(0.2f, 0.12f);

        [Header("旋转（度，正值逆时针）：斜着的手臂、大腿用得上")]
        public float rotation;

        [Header("重叠时谁赢：数值大的优先（脸压在头里面 → 脸给更大的值）")]
        public int priority;

        [Header("增益倍率：这个部位每单位抚摸量的收益系数")]
        [Min(0f)]
        public float gainScale = 1f;

        [Header("解禁阶段：互动阶段 < 此值时摸这里会被拒绝（0 = 一开始就能摸）")]
        [Min(0)]
        public int unlockStage;

        [Header("关掉 = 这个框只是占位，不参与命中（临时屏蔽用）")]
        public bool enabled = true;

        public string Label => string.IsNullOrEmpty(displayName) ? id : displayName;

        public VNTouchZone Clone() => (VNTouchZone)MemberwiseClone();
    }

    /// <summary>
    /// 某一张立绘（或某个表情）的部位覆盖。只写和基准**有差异**的框，
    /// 同 id 的覆盖基准、新 id 追加、没提到的继承基准 —— 换姿势立绘
    /// （坐下 / 躺下 / 近景）只需重画动过的那几个框。
    /// </summary>
    [Serializable]
    public class VNZoneSpriteOverride
    {
        [Header("匹配的立绘图（优先级高于下面的表情名）")]
        public Sprite sprite;

        [Header("匹配的表情名（sprite 留空时才用它匹配）")]
        public string expression;

        [Header("勾上 = 完全不继承基准，只用下面这一套（构图彻底不同的立绘用）")]
        public bool replaceAll;

        [Header("覆盖 / 追加的部位")]
        public List<VNTouchZone> zones = new List<VNTouchZone>();

        [Header("要从基准里删掉的部位 id（这张立绘上不存在的部位）")]
        public List<string> removeZoneIds = new List<string>();
    }

    /// <summary>
    /// 角色的部位区域定义。一个角色一份（Create → VN → Touch Zone Definition），
    /// 由「部位区域编辑器」在立绘上拖框生成。
    ///
    /// 命中判定是纯数学（<see cref="Contains"/> / <see cref="Pick"/> 都是静态或无副作用），
    /// 编辑器预览与运行时共用同一份 —— 同 VNShakeSpec / VNCamera 缩放公式的做法，
    /// 避免「编辑器里画的框和游戏里摸到的地方不一样」。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Touch Zone Definition", fileName = "NewTouchZones")]
    public class VNTouchZoneDef : ScriptableObject
    {
        [Header("对应的角色 id（与 VNCharacterDef.id 一致）")]
        public string characterId;

        [Header("基准部位（在默认表情立绘上画的一套）")]
        public List<VNTouchZone> baseZones = new List<VNTouchZone>();

        [Header("按立绘 / 表情覆盖（只写差异，其余继承基准）")]
        public List<VNZoneSpriteOverride> overrides = new List<VNZoneSpriteOverride>();

        // 合成结果缓存：Update 里每帧都要查，不能每次重新分配列表
        readonly Dictionary<object, List<VNTouchZone>> _cache =
            new Dictionary<object, List<VNTouchZone>>();

        void OnDisable() => _cache.Clear();
#if UNITY_EDITOR
        void OnValidate() => _cache.Clear();   // 编辑器里改完框立刻生效
#endif

        /// <summary>编辑器改完数据后手动清缓存（画框工具用）</summary>
        public void InvalidateCache() => _cache.Clear();

        /// <summary>
        /// 取某张立绘 / 某个表情实际生效的部位列表（基准 + 覆盖合并后的结果）。
        /// 返回的是内部缓存列表，**调用方不要改**。
        /// </summary>
        public List<VNTouchZone> ZonesFor(Sprite sprite, string expression)
        {
            object key = (object)sprite ?? expression ?? "";
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var ov = FindOverride(sprite, expression);
            var list = new List<VNTouchZone>();

            if (ov == null || !ov.replaceAll)
                foreach (var z in baseZones)
                    if (z != null && !string.IsNullOrEmpty(z.id)) list.Add(z);

            if (ov != null)
            {
                foreach (var id in ov.removeZoneIds)
                    list.RemoveAll(z => z.id == id);

                foreach (var z in ov.zones)
                {
                    if (z == null || string.IsNullOrEmpty(z.id)) continue;
                    int i = list.FindIndex(e => e.id == z.id);
                    if (i >= 0) list[i] = z;      // 同 id → 覆盖
                    else list.Add(z);             // 新 id → 追加
                }
            }

            // 高优先级排前面，命中时第一个匹配的就是赢家
            list.Sort((a, b) => b.priority.CompareTo(a.priority));
            _cache[key] = list;
            return list;
        }

        VNZoneSpriteOverride FindOverride(Sprite sprite, string expression)
        {
            if (sprite != null)
                foreach (var o in overrides)
                    if (o != null && o.sprite == sprite) return o;

            if (!string.IsNullOrEmpty(expression))
                foreach (var o in overrides)
                    if (o != null && o.sprite == null && o.expression == expression) return o;

            return null;
        }

        // ------------------------------------------------------------------
        // 命中数学（纯静态，可单测；编辑器画布与运行时共用）
        // ------------------------------------------------------------------

        /// <summary>点 p（归一化，立绘中心为原点）是否落在该部位内</summary>
        public static bool Contains(VNTouchZone zone, Vector2 p)
        {
            if (zone == null || !zone.enabled) return false;

            Vector2 half = zone.size * 0.5f;
            if (half.x <= 0.0001f || half.y <= 0.0001f) return false;

            // 平移到框中心，再反向旋转回轴对齐空间
            Vector2 d = p - zone.center;
            if (Mathf.Abs(zone.rotation) > 0.01f)
            {
                float rad = -zone.rotation * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                d = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            }

            switch (zone.shape)
            {
                case VNZoneShape.Rect:
                    return Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y;

                case VNZoneShape.Ellipse:
                {
                    float nx = d.x / half.x, ny = d.y / half.y;
                    return nx * nx + ny * ny <= 1f;
                }

                case VNZoneShape.Capsule:
                {
                    // 圆角矩形：角半径取短边的一半
                    float r = Mathf.Min(half.x, half.y);
                    float cx = Mathf.Max(Mathf.Abs(d.x) - (half.x - r), 0f);
                    float cy = Mathf.Max(Mathf.Abs(d.y) - (half.y - r), 0f);
                    return Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y &&
                           cx * cx + cy * cy <= r * r;
                }
            }
            return false;
        }

        /// <summary>
        /// 取点 p 命中的部位（优先级最高的那个）。找不到返回 null。
        /// 注意这里**不过滤解禁阶段** —— 禁忌部位也要能命中，
        /// 否则摸到禁区时模块无从知道该播「拒绝」反馈。
        /// </summary>
        public VNTouchZone Pick(Vector2 p, Sprite sprite, string expression)
        {
            var zones = ZonesFor(sprite, expression);
            for (int i = 0; i < zones.Count; i++)          // 已按 priority 降序
                if (Contains(zones[i], p)) return zones[i];
            return null;
        }

        /// <summary>按 id 找部位（跨立绘取基准的那份，UI 显示名用）</summary>
        public VNTouchZone FindById(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return null;
            foreach (var z in baseZones)
                if (z != null && z.id == zoneId) return z;
            foreach (var o in overrides)
                foreach (var z in o.zones)
                    if (z != null && z.id == zoneId) return z;
            return null;
        }
    }
}
