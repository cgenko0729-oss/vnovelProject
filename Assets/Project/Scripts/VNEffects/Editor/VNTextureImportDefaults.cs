using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 素材目录里的图片，导入时自动设成 Sprite (2D and UI) + Single。
    ///
    /// 【为什么用 AssetPostprocessor 而不是导入后再改】
    /// `OnPreprocessTexture` 在**导入前**跑，改完 importer 才开始真正导入 ——
    /// 拖进来一次就是对的，不会先按默认设置导一遍再重导一遍。
    ///
    /// 【为什么必须是白名单目录，不能全项目一刀切】
    /// `Assets/Art/Models/**` 下有大量模型贴图（法线 / 粗糙度 / 金属度），
    /// 它们**绝不能变成 Sprite** —— 法线贴图一旦按 sRGB 的 Sprite 导入，
    /// 光照就全错了，而且这种错很难第一时间联想到导入设置。
    /// `Assets/Development/DebugScreenShot` 里的调试截图同理，没必要动。
    ///
    /// 【为什么只管首次导入】
    /// `importSettingsMissing == true` 表示这个文件还没有 .meta，也就是**刚拖进来**。
    /// 只在这时设置，意味着：
    ///   · 新素材自动就位；
    ///   · 你事后手动调过的任何设置（Pivot、Max Size、改成 Multiple 切图、
    ///     Pixels Per Unit…）在以后的重新导入中**永远不会被打回去**。
    /// 无条件强制的话，改成 Multiple 切好的立绘图集会在下次 reimport 时被打回 Single，
    /// 切图数据虽然还在 .meta 里但不再生效 —— 属于"改了没反应"里最难查的一类。
    /// 存量图片要补，用下面的菜单手动对选中项应用。
    /// </summary>
    public class VNTextureImportDefaults : AssetPostprocessor
    {
        /// <summary>
        /// 生效的目录前缀（含末尾斜杠，前缀匹配所以子目录一并覆盖）。
        /// 新开素材目录时往这里补一行即可。
        /// </summary>
        public static readonly string[] Roots =
        {
            "Assets/Art/Images/",     // Background / Character / CG / UI / …
            "Assets/Art/CG/",
            "Assets/Art/BigPhoto/",
            "Assets/Art/Mark/",
            "Assets/Art/InteractionMiniGame/",  // 亲密互动的道具/光标图
            "Assets/Assets/",         // 随手丢素材的地方
        };

        void OnPreprocessTexture()
        {
            var importer = assetImporter as TextureImporter;
            if (importer == null) return;
            if (!InScope(assetPath)) return;
            if (!IsFirstImport(assetPath, importer)) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
        }

        /// <summary>
        /// 这张图的导入设置是不是"从没被人配置过"。
        ///
        /// `importSettingsMissing` 的语义比字面更宽：不只是"没有 .meta"，
        /// **meta 存在却不含完整 importer 设置块时也返回 true** ——
        /// 工程里那些很早以前加进来、一直是 Default 类型没人动过的老图就属于这种。
        ///
        /// 好处是它精确表达了"用户没做过决定"，所以你手动调过的任何设置
        /// （Pivot、Max Size、改成 Multiple 切图…）都会让它变成 false，永远安全。
        /// 代价是它**不等于"刚拖进来"** —— 存量里那些从没配置过的图也会被一并修正。
        ///
        /// 试过用「磁盘上没有 .meta」来卡死"新文件"，**不成立**：
        /// Unity 在调用 preprocessor 之前就已经把 .meta 写盘了，新旧图一律为真，
        /// 加上这条会让整个 postprocessor 完全不生效。
        /// </summary>
        static bool IsFirstImport(string path, AssetImporter importer)
        {
            return importer.importSettingsMissing;
        }

        /// <summary>路径是否落在素材目录白名单内。</summary>
        public static bool InScope(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/');
            for (int i = 0; i < Roots.Length; i++)
                if (path.StartsWith(Roots[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ==================================================================
        // 存量补救：对选中的图手动应用
        // ==================================================================

        const string MenuPath = "Tools/VN Effects/贴图 Textures/套用 Sprite 导入设置到选中项 Apply Sprite Settings";

        [MenuItem(MenuPath, priority = 160)]
        static void ApplyToSelection()
        {
            var paths = CollectSelectedTextures();
            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("VN 贴图设置",
                    "先在 Project 里选中图片或包含图片的文件夹。", "好");
                return;
            }

            var todo = new List<string>();
            foreach (var p in paths)
            {
                var im = AssetImporter.GetAtPath(p) as TextureImporter;
                if (im == null) continue;
                if (im.textureType == TextureImporterType.Sprite &&
                    im.spriteImportMode == SpriteImportMode.Single) continue;
                todo.Add(p);
            }

            if (todo.Count == 0)
            {
                EditorUtility.DisplayDialog("VN 贴图设置",
                    "选中的 " + paths.Count + " 张图已经全部是 Sprite / Single 了。", "好");
                return;
            }

            // 会把 Multiple 切图打回 Single，是破坏性的，所以先问清楚
            int multiple = 0;
            foreach (var p in todo)
            {
                var im = (TextureImporter)AssetImporter.GetAtPath(p);
                if (im.spriteImportMode == SpriteImportMode.Multiple) multiple++;
            }

            string warn = multiple > 0
                ? "\n\n⚠ 其中 " + multiple + " 张目前是 Multiple（切图）模式，" +
                  "改成 Single 后切图将不再生效。"
                : string.Empty;

            if (!EditorUtility.DisplayDialog("VN 贴图设置",
                    "把 " + todo.Count + " 张图设为 Sprite (2D and UI) + Single？" + warn,
                    "应用", "取消"))
                return;

            int done = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < todo.Count; i++)
                {
                    var im = (TextureImporter)AssetImporter.GetAtPath(todo[i]);
                    im.textureType = TextureImporterType.Sprite;
                    im.spriteImportMode = SpriteImportMode.Single;
                    im.SaveAndReimport();
                    done++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
            Debug.Log("[VN 贴图设置] 已设为 Sprite / Single：" + done + " 张");
        }

        /// <summary>选中的贴图 + 选中文件夹下递归的贴图。</summary>
        static List<string> CollectSelectedTextures()
        {
            var result = new List<string>();
            var seen = new HashSet<string>();

            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { path }))
                    {
                        string p = AssetDatabase.GUIDToAssetPath(guid);
                        if (seen.Add(p)) result.Add(p);
                    }
                }
                else if (obj is Texture2D || obj is Sprite)
                {
                    if (seen.Add(path)) result.Add(path);
                }
            }
            return result;
        }
    }
}
