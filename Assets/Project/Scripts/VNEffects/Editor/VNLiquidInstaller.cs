using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 把液体喷溅的两个组件**增量装进当前场景**，不重建场景。
    ///
    /// 【为什么需要它】
    /// Tools → VN Effects → Create Script Demo Scene 会 NewScene(EmptyScene) 从零重造，
    /// 手工整理过的 Hierarchy 与场景上的调参会全部丢失。而 VNStage.AutoWire 只能
    /// 「找得到才接」——场景里本来就没有 VNWetScreen / VNLiquidSplash 时，
    /// liquid 命令会静默无效果（连报错都没有，因为每个分支都 `if (xxx == null) break;`）。
    /// 所以老场景需要这么一个只做加法的安装器，和 VNQuizInstaller 同一思路：
    ///   ① Canvas 下补 WetScreen（屏幕水渍层）
    ///   ② 场外补 LiquidSplash（空中水珠，世界空间粒子）
    ///   ③ 两者互连，并回填到场景的 VNStage
    /// 重复执行安全：已经装过就只补接线，不会造出第二份。
    /// </summary>
    public static class VNLiquidInstaller
    {
        [MenuItem("Tools/VN Effects/Install Liquid Splash To Scene", priority = 211)]
        public static void Install()
        {
            var stage = Object.FindFirstObjectByType<VNStage>(FindObjectsInactive.Include);
            if (stage == null)
            {
                EditorUtility.DisplayDialog("VN Liquid",
                    "当前场景里找不到 VNStage。\n\n" +
                    "请先打开剧本场景（含 VNStage 的那个），" +
                    "或用 Tools → VN Effects → Create Script Demo Scene 造一个新场景。", "OK");
                return;
            }

            var canvas = FindMainCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("VN Liquid",
                    "当前场景里找不到 Canvas。\n\n" +
                    "屏幕水渍层必须挂在 Canvas 下（它是 uGUI 元素，不是粒子）。", "OK");
                return;
            }

            var report = new List<string>();

            // 材质资产：和生成器共用同一份，避免场景里出现两套同名材质
            VNEffectsDemoSetup.EnsureFolder(VNEffectsDemoSetup.SharedMaterialsDir);
            var additiveMat = VNEffectsDemoSetup.EnsureMaterialAsset(
                $"{VNEffectsDemoSetup.SharedMaterialsDir}/VNAdditive.mat", "VN/Additive");
            var alphaMat = VNEffectsDemoSetup.EnsureMaterialAsset(
                $"{VNEffectsDemoSetup.SharedMaterialsDir}/VNParticleAlpha.mat", "VN/ParticleAlpha");

            // ① 屏幕水渍层
            var wet = Object.FindFirstObjectByType<VNWetScreen>(FindObjectsInactive.Include);
            if (wet == null)
            {
                var go = new GameObject("WetScreen", typeof(RectTransform));
                go.transform.SetParent(canvas.transform, false);
                wet = go.AddComponent<VNWetScreen>();
                Undo.RegisterCreatedObjectUndo(go, "Install liquid splash");
                AssignField(wet, "sourceMaterial", additiveMat);
                report.Add("Canvas 下新建 WetScreen（排序 30，让开对话框 40）");
            }
            else report.Add("WetScreen 已存在，跳过");

            // ② 舞台层喷溅（场外物体：它是"场景里的水"，靠 sortingOrder 排序，不进 Canvas）
            var splash = Object.FindFirstObjectByType<VNLiquidSplash>(FindObjectsInactive.Include);
            if (splash == null)
            {
                var go = new GameObject("LiquidSplash");
                splash = go.AddComponent<VNLiquidSplash>();
                Undo.RegisterCreatedObjectUndo(go, "Install liquid splash");
                AssignField(splash, "alphaSourceMaterial", alphaMat);
                AssignField(splash, "additiveSourceMaterial", additiveMat);
                report.Add("场外新建 LiquidSplash（世界空间粒子，排序 28）");
            }
            else report.Add("LiquidSplash 已存在，跳过");

            // ③ 接线（两层互连 + 回填 VNStage）
            if (splash.wetScreen != wet)
            {
                Undo.RecordObject(splash, "Wire liquid splash");
                splash.wetScreen = wet;
                EditorUtility.SetDirty(splash);
                report.Add("LiquidSplash → WetScreen 已接上（缺这条就只有空中水花）");
            }

            if (stage.wetScreen != wet || stage.liquidSplash != splash)
            {
                Undo.RecordObject(stage, "Wire liquid splash");
                stage.wetScreen = wet;
                stage.liquidSplash = splash;
                EditorUtility.SetDirty(stage);
                report.Add("VNStage 引用已回填");
            }

            EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);
            AssetDatabase.SaveAssets();

            string summary = string.Join("\n", report);
            Debug.Log($"[VNLiquid] 已装入当前场景：\n{summary}");
            EditorUtility.DisplayDialog("VN Liquid",
                $"液体喷溅已装进当前场景：\n\n{summary}\n\n" +
                "场景已标记为未保存——记得 Ctrl+S。\n\n" +
                "剧本里就可以写：\n" +
                "  liquid splash x:0.5 y:0.15 power:2\n" +
                "  liquid spray on x:0.5 y:0.08\n" +
                "  liquid wet on", "OK");
            Selection.activeObject = splash.gameObject;
            EditorGUIUtility.PingObject(splash.gameObject);
        }

        /// <summary>
        /// 找主 Canvas：优先取对话框所在的那个（那才是舞台 UI 的 Canvas），
        /// 否则退回场景里第一个非 overlay 的 Canvas。
        /// </summary>
        static Canvas FindMainCanvas()
        {
            var dialogue = Object.FindFirstObjectByType<VNDialogueBox>(FindObjectsInactive.Include);
            if (dialogue != null)
            {
                var c = dialogue.GetComponentInParent<Canvas>();
                if (c != null) return c.rootCanvas != null ? c.rootCanvas : c;
            }

            var all = Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in all)
                if (c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) return c;
            return all.Length > 0 ? all[0] : null;
        }

        static void AssignField(Component comp, string fieldName, Material mat)
        {
            if (mat == null || comp == null) return;
            var so = new SerializedObject(comp);
            var prop = so.FindProperty(fieldName);
            if (prop == null) return;
            prop.objectReferenceValue = mat;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
