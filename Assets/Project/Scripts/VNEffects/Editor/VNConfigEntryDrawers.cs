using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 配置条目（背景 / CG / 音频 / UI 皮肤）的紧凑单行绘制。
    ///
    /// 【为什么需要】
    /// 这几个条目类把说明文字写成了字段上的 [Header]（见 VNStage.BackgroundEntry 等）。
    /// Header 是 DecoratorDrawer，Unity 默认 Inspector 会**给列表里的每一项都重画一遍**，
    /// 于是一个 CG 条目要占 6~7 行 —— 7 张 CG 就 50 行，这才是"要滚很久"的真正原因。
    /// 类型上一旦挂了 CustomPropertyDrawer，Unity 就不再递归画子字段，那些 Header 自然消失；
    /// 说明文字改挂 tooltip，不占版面但鼠标悬停仍看得到。
    ///
    /// 所以这里**一行代码都不用改运行时**，纯靠接管绘制解决。
    /// 又因为 drawer 是挂在类型上的，VNStage / VNAudio 组件 Inspector 上的同名列表
    /// 也一并变紧凑，不只 VNGameConfig 受益。
    ///
    /// 【布局】缩略图够高（>=34px）时排两行：上 id、下 资产 + 附加字段；
    /// 缩略图调小时自动退回单行。缩略图格子本身可拖入资产、单击 ping 到 Project。
    /// </summary>
    public abstract class VNEntryDrawerBase : PropertyDrawer
    {
        /// <summary>两行布局的门槛：缩略图矮于这个值就排单行</summary>
        protected const float TwoLineMinThumb = 34f;

        protected static bool TwoLine { get { return VNAssetUi.ThumbSize >= TwoLineMinThumb; } }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return VNAssetUi.RowHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // 独立字段（不是数组元素）时仍要画出字段名，否则看不出这是什么
            if (!IsArrayElement(property) && label != null && label != GUIContent.none &&
                !string.IsNullOrEmpty(label.text))
            {
                var lr = VNAssetUi.Line(position);
                lr.width = EditorGUIUtility.labelWidth;
                EditorGUI.LabelField(lr, label);
                position.x += EditorGUIUtility.labelWidth;
                position.width -= EditorGUIUtility.labelWidth;
            }

            int indentWas = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;      // rect 已经手算好了，缩进会让控件错位
            DrawRow(position, property);
            EditorGUI.indentLevel = indentWas;

            EditorGUI.EndProperty();
        }

        protected abstract void DrawRow(Rect row, SerializedProperty property);

        protected static bool IsArrayElement(SerializedProperty p)
        {
            return !string.IsNullOrEmpty(p.propertyPath) && p.propertyPath.EndsWith("]");
        }

        // ------------------------------------------------------------------
        // 共用零件
        // ------------------------------------------------------------------

        /// <summary>
        /// 画缩略图格子：可拖入资产、单击 ping。返回格子矩形。
        /// objProp 为对应的对象引用属性，wanted 为期望类型（拖入的贴图会自动转 Sprite）。
        /// </summary>
        protected static Rect ThumbSlot(ref Rect row, SerializedProperty objProp,
                                        System.Type wanted, System.Action<Rect, Object> draw,
                                        float widthScale = 1f)
        {
            float w = VNAssetUi.ThumbSize * widthScale;
            var slot = VNAssetUi.CutLeft(ref row, w);
            var current = objProp != null ? objProp.objectReferenceValue : null;

            draw(slot, current);

            var e = Event.current;

            // 拖入替换
            var dropped = VNAssetUi.DropTarget(slot, wanted);
            if (dropped != null && objProp != null)
            {
                objProp.objectReferenceValue = dropped;
                objProp.serializedObject.ApplyModifiedProperties();
                GUI.changed = true;
            }
            else if (e.type == EventType.DragUpdated && slot.Contains(e.mousePosition) &&
                     VNAssetUi.FirstDraggedOfType(wanted) != null)
            {
                VNAssetUi.DrawOutline(slot, new Color(0.4f, 0.8f, 1f, 0.9f), 2f);
            }

            // 单击 ping
            if (e.type == EventType.MouseDown && e.button == 0 && slot.Contains(e.mousePosition))
            {
                if (current != null) { VNAssetUi.Ping(current); e.Use(); }
            }

            return slot;
        }

        /// <summary>id 输入框；为空时叠一行淡色占位提示。</summary>
        protected static void IdField(Rect r, SerializedProperty idProp, string tooltip, string placeholder)
        {
            EditorGUI.PropertyField(r, idProp, GUIContent.none);
            if (string.IsNullOrEmpty(idProp.stringValue))
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                EditorGUI.LabelField(new Rect(r.x + 3f, r.y, r.width - 6f, r.height),
                                     placeholder, EditorStyles.miniLabel);
                GUI.color = old;
            }
            if (!string.IsNullOrEmpty(tooltip))
                EditorGUI.LabelField(r, new GUIContent(string.Empty, tooltip));
        }

        /// <summary>右侧淡色附注（尺寸 / 时长 / 文件名）。</summary>
        protected static void Note(Rect r, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            EditorGUI.LabelField(r, text, VNAssetUi.RowLabel);
        }
    }

    // ======================================================================
    // 背景库
    // ======================================================================

    [CustomPropertyDrawer(typeof(VNStage.BackgroundEntry))]
    public class VNBackgroundEntryDrawer : VNEntryDrawerBase
    {
        protected override void DrawRow(Rect row, SerializedProperty property)
        {
            var idProp = property.FindPropertyRelative("id");
            var spProp = property.FindPropertyRelative("sprite");
            if (idProp == null || spProp == null) { EditorGUI.LabelField(row, "(字段缺失)"); return; }

            ThumbSlot(ref row, spProp, typeof(Sprite),
                      (r, o) => VNAssetUi.DrawSpriteThumb(r, o as Sprite));

            var sprite = spProp.objectReferenceValue as Sprite;

            if (TwoLine)
            {
                Rect top, bottom;
                VNAssetUi.TwoLines(row, out top, out bottom);

                var sizeRect = VNAssetUi.CutRight(ref top, 72f);
                IdField(top, idProp, "剧本 bg 命令引用的背景 id（可中文）", "（背景 id）");
                Note(sizeRect, VNAssetUi.SpriteSizeText(sprite));

                EditorGUI.PropertyField(bottom, spProp, GUIContent.none);
            }
            else
            {
                var line = VNAssetUi.Line(row);
                var idRect = VNAssetUi.CutLeft(ref line, line.width * 0.4f);
                IdField(idRect, idProp, "剧本 bg 命令引用的背景 id（可中文）", "（背景 id）");
                EditorGUI.PropertyField(line, spProp, GUIContent.none);
            }
        }
    }

    // ======================================================================
    // CG 库
    // ======================================================================

    [CustomPropertyDrawer(typeof(VNStage.CgEntry))]
    public class VNCgEntryDrawer : VNEntryDrawerBase
    {
        protected override void DrawRow(Rect row, SerializedProperty property)
        {
            var idProp = property.FindPropertyRelative("id");
            var spProp = property.FindPropertyRelative("sprite");
            var grProp = property.FindPropertyRelative("group");
            if (idProp == null || spProp == null) { EditorGUI.LabelField(row, "(字段缺失)"); return; }

            ThumbSlot(ref row, spProp, typeof(Sprite),
                      (r, o) => VNAssetUi.DrawSpriteThumb(r, o as Sprite));

            if (TwoLine)
            {
                Rect top, bottom;
                VNAssetUi.TwoLines(row, out top, out bottom);

                var sizeRect = VNAssetUi.CutRight(ref top, 72f);
                IdField(top, idProp, "剧本 cg 命令引用的 CG id", "（CG id）");
                Note(sizeRect, VNAssetUi.SpriteSizeText(spProp.objectReferenceValue as Sprite));

                if (grProp != null)
                {
                    var grRect = VNAssetUi.CutRight(ref bottom, 96f);
                    EditorGUI.PropertyField(grRect, grProp, GUIContent.none);
                    if (string.IsNullOrEmpty(grProp.stringValue))
                    {
                        var old = GUI.color;
                        GUI.color = new Color(1f, 1f, 1f, 0.35f);
                        EditorGUI.LabelField(new Rect(grRect.x + 3f, grRect.y, grRect.width - 6f, grRect.height),
                                             "（差分组）", EditorStyles.miniLabel);
                        GUI.color = old;
                    }
                    EditorGUI.LabelField(grRect, new GUIContent(string.Empty,
                        "差分组名：同组 CG 在鉴赏画廊里归为一格翻页；留空 = 独立一格"));
                }
                EditorGUI.PropertyField(bottom, spProp, GUIContent.none);
            }
            else
            {
                var line = VNAssetUi.Line(row);
                if (grProp != null)
                {
                    var grRect = VNAssetUi.CutRight(ref line, 80f);
                    EditorGUI.PropertyField(grRect, grProp, GUIContent.none);
                }
                var idRect = VNAssetUi.CutLeft(ref line, line.width * 0.42f);
                IdField(idRect, idProp, "剧本 cg 命令引用的 CG id", "（CG id）");
                EditorGUI.PropertyField(line, spProp, GUIContent.none);
            }
        }
    }

    // ======================================================================
    // 音频库（BGM / SE / Voice 共用同一个条目类型）
    // ======================================================================

    [CustomPropertyDrawer(typeof(VNAudio.AudioEntry))]
    public class VNAudioEntryDrawer : VNEntryDrawerBase
    {
        const float PlayBtnW = 22f;
        /// <summary>波形比方形缩略图宽，看起来才像波形</summary>
        const float WaveScale = 1.7f;

        protected override void DrawRow(Rect row, SerializedProperty property)
        {
            var idProp = property.FindPropertyRelative("id");
            var clipProp = property.FindPropertyRelative("clip");
            var volProp = property.FindPropertyRelative("volume");
            if (idProp == null || clipProp == null) { EditorGUI.LabelField(row, "(字段缺失)"); return; }

            var clip = clipProp.objectReferenceValue as AudioClip;

            // ▶ / ■ 试听
            var btn = VNAssetUi.CutLeft(ref row, PlayBtnW, 2f);
            btn = new Rect(btn.x, btn.y + (btn.height - 20f) * 0.5f, btn.width, 20f);
            bool playing = VNAssetUi.IsPreviewing(clip);
            using (new EditorGUI.DisabledScope(clip == null || !VNAssetUi.CanPreviewAudio))
            {
                var content = new GUIContent(playing ? "■" : "▶",
                    clip == null ? "先指定音频素材"
                                 : (playing ? "停止试听" : "试听（编辑器内，不影响游戏音量设置）"));
                if (GUI.Button(btn, content, VNAssetUi.TinyButton))
                {
                    if (playing) VNAssetUi.StopPreview();
                    else VNAssetUi.PlayPreview(clip);
                }
            }

            // 波形（点击 = ping，可拖入替换）
            ThumbSlot(ref row, clipProp, typeof(AudioClip),
                      (r, o) => VNAssetUi.DrawWaveform(r, o as AudioClip), WaveScale);

            if (TwoLine)
            {
                Rect top, bottom;
                VNAssetUi.TwoLines(row, out top, out bottom);

                var lenRect = VNAssetUi.CutRight(ref top, 44f);
                IdField(top, idProp, "剧本 bgm / se / voice 命令引用的 id（可中文）", "（音频 id）");
                Note(lenRect, VNAssetUi.ClipLengthText(clip));

                if (volProp != null)
                {
                    var volRect = VNAssetUi.CutRight(ref bottom, 112f);
                    EditorGUI.PropertyField(volRect, volProp, GUIContent.none);
                    EditorGUI.LabelField(volRect, new GUIContent(string.Empty,
                        "该素材的基准音量。素材本身偏响就往下调；Unity 音量上限为 1，无法放大素材本身"));
                }
                EditorGUI.PropertyField(bottom, clipProp, GUIContent.none);
            }
            else
            {
                var line = VNAssetUi.Line(row);
                if (volProp != null)
                {
                    var volRect = VNAssetUi.CutRight(ref line, 96f);
                    EditorGUI.PropertyField(volRect, volProp, GUIContent.none);
                }
                var idRect = VNAssetUi.CutLeft(ref line, line.width * 0.42f);
                IdField(idRect, idProp, "剧本 bgm / se / voice 命令引用的 id（可中文）", "（音频 id）");
                EditorGUI.PropertyField(line, clipProp, GUIContent.none);
            }
        }
    }

    // ======================================================================
    // UI 皮肤条目
    // ======================================================================

    [CustomPropertyDrawer(typeof(VNGameConfig.UiSkinEntry))]
    public class VNUiSkinEntryDrawer : VNEntryDrawerBase
    {
        protected override void DrawRow(Rect row, SerializedProperty property)
        {
            var idProp = property.FindPropertyRelative("id");
            var pfProp = property.FindPropertyRelative("prefab");
            if (idProp == null || pfProp == null) { EditorGUI.LabelField(row, "(字段缺失)"); return; }

            ThumbSlot(ref row, pfProp, typeof(GameObject),
                      (r, o) => VNAssetUi.DrawObjectThumb(r, o));

            if (TwoLine)
            {
                Rect top, bottom;
                VNAssetUi.TwoLines(row, out top, out bottom);
                IdField(top, idProp, "剧本 ui 命令引用的 id（可中文，如 华丽 / 顶部）", "（皮肤 id）");
                EditorGUI.PropertyField(bottom, pfProp, GUIContent.none);
            }
            else
            {
                var line = VNAssetUi.Line(row);
                var idRect = VNAssetUi.CutLeft(ref line, line.width * 0.4f);
                IdField(idRect, idProp, "剧本 ui 命令引用的 id（可中文，如 华丽 / 顶部）", "（皮肤 id）");
                EditorGUI.PropertyField(line, pfProp, GUIContent.none);
            }
        }
    }
}
