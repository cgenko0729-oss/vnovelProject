using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VNEffects;

namespace VNEffectsEditor
{
    /// <summary>
    /// 人格资产的**草稿层**：试聊台改参数时改的是这一份内存副本，不动磁盘上的资产。
    ///
    /// 【为什么用「临时 ScriptableObject 副本」而不是自己声明一堆字段】
    ///   1. `VNAiConversation` 只认 `VNAiPersonaDef` 这个类型，草稿是同一个类型
    ///      → 逻辑层一行都不用改就能吃草稿
    ///   2. 左栏直接用 SerializedObject 迭代画，**零 UI 代码就有全部字段**；
    ///      将来给 VNAiPersonaDef 加新字段，窗口自动就有，不会两边脱节
    ///   3. 写回是逐属性 CopyFromSerializedProperty，自带 Undo
    ///
    /// 【为什么不用 EditorUtility.CopySerialized 写回】
    ///   它连 m_Name 一起复制。副本是 Instantiate 出来的，名字是「xxx(Clone)」，
    ///   照抄回去资产的内部名就和文件名对不上了。逐属性复制天然跳过 m_Name
    ///   （它不是 visible property），顺带还能跳过 m_Script。
    ///
    /// 【域重载】
    ///   HideFlags.DontSave 的对象活不过域重载，所以窗口负责在重载前把
    ///   ToJson() 的结果存进自己的 [SerializeField]，重载后 Restore 回来。
    /// </summary>
    public class VNAiStudioDraft
    {
        VNAiPersonaDef _source;
        VNAiPersonaDef _draft;
        SerializedObject _so;

        /// <summary>磁盘上的原资产（写回的目标）</summary>
        public VNAiPersonaDef Source => _source;

        /// <summary>喂给 VNAiConversation 的那一份（改了参数的）</summary>
        public VNAiPersonaDef Draft => _draft;

        public bool IsValid => _source != null && _draft != null;

        // 字段级 diff，形如「boundaries: … → …」。每次重绘时重算，
        // 字段不多（20 来个），开销可以忽略。
        readonly List<string> _changed = new List<string>();
        public IReadOnlyList<string> ChangedFields => _changed;
        public bool IsDirty => _changed.Count > 0;

        // ──────────────── 绑定 / 销毁 ────────────────

        public void Bind(VNAiPersonaDef asset)
        {
            if (asset == _source && _draft != null) return;
            Dispose();
            _source = asset;
            if (_source == null) return;

            _draft = Object.Instantiate(_source);
            _draft.hideFlags = HideFlags.DontSave;   // 绝不写进场景或资产
            _draft.name = _source.name + " (草稿)";
            _so = new SerializedObject(_draft);
            RecomputeDiff();
        }

        public void Dispose()
        {
            _so = null;
            if (_draft != null) Object.DestroyImmediate(_draft);
            _draft = null;
            _source = null;
            _changed.Clear();
        }

        // ──────────────── 绘制 ────────────────

        /// <summary>
        /// 画出草稿的全部字段。改过的字段名前面标一个 ● ，
        /// 一眼就知道「这次试聊到底和资产差在哪」。
        /// </summary>
        public void DrawFields()
        {
            if (!IsValid) return;

            _so.Update();
            var p = _so.GetIterator();
            bool enterChildren = true;
            var dirtyPaths = new HashSet<string>();
            foreach (string path in _changedPaths) dirtyPaths.Add(path);

            while (p.NextVisible(enterChildren))
            {
                enterChildren = false;               // 只画顶层，复合字段由 PropertyField 自己展开
                if (p.propertyPath == "m_Script") continue;

                bool dirty = dirtyPaths.Contains(p.propertyPath);
                var label = new GUIContent(
                    (dirty ? "● " : "") + p.displayName, p.tooltip);

                if (dirty) GUI.color = DirtyColor;
                EditorGUILayout.PropertyField(p, label, true);
                if (dirty) GUI.color = Color.white;
            }

            if (_so.ApplyModifiedProperties()) RecomputeDiff();
        }

        static readonly Color DirtyColor = new Color(1f, 0.85f, 0.4f);

        /// <summary>
        /// 绕过 SerializedObject **直接改了草稿字段**之后调一次（工具栏的供应商/模型下拉就是）。
        /// 不调的话左栏还画着旧值、● 标记也不会亮——diff 是靠 RecomputeDiff 算出来的，
        /// 不会自己发现有人动了对象。
        /// </summary>
        public void NotifyExternalEdit()
        {
            if (!IsValid) return;
            _so.Update();
            RecomputeDiff();
        }

        // ──────────────── 写回 / 还原 ────────────────

        /// <summary>草稿写回磁盘资产（带 Undo，Ctrl+Z 能撤销）。</summary>
        public void ApplyToAsset()
        {
            if (!IsValid || !IsDirty) return;

            var src = new SerializedObject(_draft);
            var dst = new SerializedObject(_source);
            var p = src.GetIterator();
            bool enterChildren = true;
            while (p.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (p.propertyPath == "m_Script") continue;
                dst.CopyFromSerializedProperty(p);
            }
            dst.ApplyModifiedProperties();           // 自带 Undo 注册

            EditorUtility.SetDirty(_source);
            AssetDatabase.SaveAssetIfDirty(_source);
            RecomputeDiff();
        }

        /// <summary>丢掉草稿改动，重新从资产拷一份。</summary>
        public void RevertFromAsset()
        {
            var asset = _source;
            Dispose();
            Bind(asset);
        }

        // ──────────────── 域重载存活 ────────────────

        public string ToJson() => IsValid ? EditorJsonUtility.ToJson(_draft) : null;

        /// <summary>重载后：重新绑定资产，再把草稿改动盖回去。</summary>
        public void Restore(VNAiPersonaDef asset, string json)
        {
            Bind(asset);
            if (!IsValid || string.IsNullOrEmpty(json)) return;
            EditorJsonUtility.FromJsonOverwrite(json, _draft);
            _draft.hideFlags = HideFlags.DontSave;   // FromJsonOverwrite 会把 hideFlags 一起盖掉
            _so = new SerializedObject(_draft);
            RecomputeDiff();
        }

        // ──────────────── diff ────────────────

        readonly List<string> _changedPaths = new List<string>();

        void RecomputeDiff()
        {
            _changed.Clear();
            _changedPaths.Clear();
            if (!IsValid) return;

            var a = new SerializedObject(_source);
            var b = new SerializedObject(_draft);
            var pa = a.GetIterator();
            bool enterChildren = true;
            while (pa.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (pa.propertyPath == "m_Script") continue;

                var pb = b.FindProperty(pa.propertyPath);
                if (pb == null) continue;
                if (SameValue(pa, pb)) continue;

                _changedPaths.Add(pa.propertyPath);
                _changed.Add($"{pa.displayName}：{Describe(pa)} → {Describe(pb)}");
            }
        }

        /// <summary>
        /// 值比较。VNAiPersonaDef 的字段就是 基元 / 枚举 / 对象引用 / List&lt;string&gt;，
        /// 所以这几类比对齐就够；遇到没覆盖的复合类型一律当作「没改」，
        /// 宁可漏报也不要误报——误报会让「已改 N 项」永远亮着，那个提示就废了。
        /// </summary>
        static bool SameValue(SerializedProperty a, SerializedProperty b)
        {
            if (a.propertyType != b.propertyType) return false;

            switch (a.propertyType)
            {
                case SerializedPropertyType.Integer:   return a.intValue == b.intValue;
                case SerializedPropertyType.Boolean:   return a.boolValue == b.boolValue;
                case SerializedPropertyType.Float:     return Mathf.Approximately(a.floatValue, b.floatValue);
                case SerializedPropertyType.String:    return a.stringValue == b.stringValue;
                case SerializedPropertyType.Enum:      return a.enumValueIndex == b.enumValueIndex;
                case SerializedPropertyType.ObjectReference:
                    return a.objectReferenceValue == b.objectReferenceValue;
            }

            if (a.isArray)
            {
                if (a.arraySize != b.arraySize) return false;
                for (int i = 0; i < a.arraySize; i++)
                    if (!SameValue(a.GetArrayElementAtIndex(i), b.GetArrayElementAtIndex(i)))
                        return false;
                return true;
            }
            return true;
        }

        static string Describe(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:   return p.intValue.ToString();
                case SerializedPropertyType.Boolean:   return p.boolValue ? "开" : "关";
                case SerializedPropertyType.Float:     return p.floatValue.ToString("0.##");
                case SerializedPropertyType.String:    return Ellipsis(p.stringValue);
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                        ? p.enumDisplayNames[p.enumValueIndex] : p.enumValueIndex.ToString();
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue != null ? p.objectReferenceValue.name : "（空）";
            }
            if (p.isArray) return $"{p.arraySize} 项";
            return "…";
        }

        static string Ellipsis(string s)
        {
            if (string.IsNullOrEmpty(s)) return "（空）";
            s = s.Replace("\n", "⏎");
            return s.Length <= 24 ? s : s.Substring(0, 24) + "…";
        }
    }
}
