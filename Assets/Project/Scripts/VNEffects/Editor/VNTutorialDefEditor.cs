using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// VNTutorialDef 的 Inspector：只在默认面板顶上加一个「在教程编辑器里打开」的入口，
    /// 字段本身仍走默认绘制（可视化排版 / 拾取锚点 / 真机预览都在窗口里）。
    /// 双击资产也直接进窗口。
    /// </summary>
    [CustomEditor(typeof(VNTutorialDef))]
    public class VNTutorialDefEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var def = (VNTutorialDef)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("在教程编辑器里打开", GUILayout.Height(30f)))
                    VNTutorialEditorWindow.Open(def);
            }
            EditorGUILayout.HelpBox(
                "编辑器里可以：在底图上拖框画洞口、从目录里选锚点、Play Mode 下 Ctrl+点 Game 视图" +
                "直接拾取目标、真机预览停在某一步实时改文字。这里的字段列表只是兜底。",
                MessageType.None);
            EditorGUILayout.Space(6f);
            DrawDefaultInspector();
        }

        [OnOpenAsset]
        static bool OnOpen(int instanceID, int line)
        {
            // InstanceIDToObject / GetInstanceID 在 Unity 6.5 都是 error 级弃用；
            // 双击时 activeObject 就是那份资产，直接用它
            var def = Selection.activeObject as VNTutorialDef;
            if (def == null) return false;
            VNTutorialEditorWindow.Open(def);
            return true;
        }
    }
}
