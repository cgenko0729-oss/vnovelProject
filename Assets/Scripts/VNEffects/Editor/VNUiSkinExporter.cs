using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VNEffects.EditorTools; // VNFontAssetBuilder（预烘焙字体资产）在这个命名空间

namespace VNEffects
{
    /// <summary>
    /// UI 皮肤导出工具：把程序化默认样式"物化"成可编辑的 prefab 资产。
    ///
    /// 【为什么需要它】对话框/选项面板的程序化贴图（圆角面板/描边框）只存在于
    /// 运行时内存，prefab 没法引用——所以做皮肤的第一步是把贴图烘焙成 PNG 资产。
    /// 本工具一键完成：烘焙贴图 → 生成与默认样式一模一样的 DialogueSkin_Default /
    /// ChoiceSkin_Default，外加两个改造示范（对话框顶部 / 选项右列），
    /// 并自动登记进 VNGameConfig 的 UI 皮肤区。
    ///
    /// 【工作流】复制某个导出的 prefab → 改布局/换自己的美术图 → 在 VNGameConfig
    /// 里登记新 id → 剧本 `ui dialogue <id>` / `ui choice <id>` 切换。
    /// 重复执行安全：贴图与 prefab 覆盖重建，VNGameConfig 里已有的 id 不重复添加。
    /// </summary>
    public static class VNUiSkinExporter
    {
        const string SkinDir = "Assets/VNEffects/UISkins";
        const string TextureDir = SkinDir + "/Textures";

        [MenuItem("Tools/VN Effects/UI Skins/Export Skin Prefabs (Default + Samples)")]
        public static void ExportAll()
        {
            EnsureFolder(SkinDir);
            EnsureFolder(TextureDir);

            Sprite rounded = BakeSprite("VN_RoundedRect",
                VNProceduralTextures.RoundedRectSprite.texture, new Vector4(22, 22, 22, 22));
            Sprite frame = BakeSprite("VN_RoundedFrame",
                VNProceduralTextures.RoundedFrameSprite.texture, new Vector4(22, 22, 22, 22));

            var dialogueDefault = BuildDialoguePrefab("DialogueSkin_Default", rounded, frame, top: false);
            var dialogueTop = BuildDialoguePrefab("DialogueSkin_Top", rounded, frame, top: true);
            var choiceDefault = BuildChoicePrefab("ChoiceSkin_Default", rounded, right: false);
            var choiceRight = BuildChoicePrefab("ChoiceSkin_Right", rounded, right: true);

            RegisterInConfig(dialogueDefault, dialogueTop, choiceDefault, choiceRight);

            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(SkinDir));
            Debug.Log($"[VNUiSkin] 皮肤套件已导出到 {SkinDir}：\n" +
                      "  DialogueSkin_Default（=程序化默认样式） / DialogueSkin_Top（顶部示范）\n" +
                      "  ChoiceSkin_Default（居中列） / ChoiceSkin_Right（右侧列示范）\n" +
                      "复制 prefab 修改布局/替换美术图，在 VNGameConfig 的 UI 皮肤区登记 id，" +
                      "剧本 ui dialogue|choice <id> 即可切换。");
        }

        // ==============================================================
        // 贴图烘焙
        // ==============================================================

        /// <summary>程序化贴图 → PNG 资产（9-slice 边距在导入设置里配好）</summary>
        static Sprite BakeSprite(string name, Texture2D texture, Vector4 border)
        {
            string path = $"{TextureDir}/{name}.png";
            // 程序化贴图 Apply(false, true) 释放了 CPU 拷贝（不可读），
            // 经 RenderTexture 走一遍 GPU → ReadPixels 拿回可读副本再编码。
            var readable = ReadableCopy(texture);
            File.WriteAllBytes(path, readable.EncodeToPNG());
            Object.DestroyImmediate(readable);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>不可读贴图的可读副本：Blit 到临时 RenderTexture 后 ReadPixels 取回</summary>
        static Texture2D ReadableCopy(Texture2D source)
        {
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        // ==============================================================
        // 对话框皮肤 prefab
        // ==============================================================

        static GameObject BuildDialoguePrefab(string name, Sprite rounded, Sprite frameSprite, bool top)
        {
            var font = VNFontAssetBuilder.EnsureFontAsset(); // 持久化字体：prefab 引用运行时字体会 Missing

            var root = new GameObject(name, typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);
            var skin = root.AddComponent<VNDialogueSkin>();

            // 面板根：默认贴底（与场景生成器的对话框矩形一致）；top 变体贴顶
            var panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(rootRect, false);
            if (top)
            {
                panelRect.anchorMin = new Vector2(0.05f, 1f);
                panelRect.anchorMax = new Vector2(0.95f, 1f);
                panelRect.pivot = new Vector2(0.5f, 1f);
                panelRect.anchoredPosition = new Vector2(0f, -28f);
            }
            else
            {
                panelRect.anchorMin = new Vector2(0.05f, 0f);
                panelRect.anchorMax = new Vector2(0.95f, 0f);
                panelRect.pivot = new Vector2(0.5f, 0f);
                panelRect.anchoredPosition = new Vector2(0f, 28f);
            }
            panelRect.sizeDelta = new Vector2(0f, 230f);
            skin.panel = panelRect;
            skin.toolbarAnchor = panelRect;

            var bg = CreateImage(panelRect, "Bg", rounded,
                new Color(0.05f, 0.07f, 0.13f, 0.78f), Image.Type.Sliced);
            Stretch(bg.rectTransform);

            var frame = CreateImage(panelRect, "Frame", frameSprite,
                new Color(1f, 0.85f, 0.5f, 0.9f), Image.Type.Sliced);
            Stretch(frame.rectTransform);
            skin.shineFrame = frame;

            // 名牌（骑在面板顶边上）
            var nameTag = new GameObject("NameTag", typeof(RectTransform));
            var tagRect = (RectTransform)nameTag.transform;
            tagRect.SetParent(panelRect, false);
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = new Vector2(0f, 0.5f);
            tagRect.anchoredPosition = new Vector2(44f, 4f);
            tagRect.sizeDelta = new Vector2(210f, 50f);
            var tagBg = CreateImage(tagRect, "Bg", rounded,
                new Color(0.45f, 0.3f, 0.75f, 0.9f), Image.Type.Sliced);
            Stretch(tagBg.rectTransform);
            var nameText = CreateText(tagRect, "Name", font, 26, TextAlignmentOptions.Center);
            Stretch(nameText.rectTransform);
            nameText.fontStyle = FontStyles.Bold;
            skin.nameTag = nameTag;
            skin.nameText = nameText;

            // 正文：基准边距 = 无头像状态；头像显示时的避让量在皮肤字段里声明
            var body = CreateText(panelRect, "Body", font, 28, TextAlignmentOptions.TopLeft);
            Stretch(body.rectTransform);
            body.rectTransform.offsetMin = new Vector2(40f, 26f);
            body.rectTransform.offsetMax = new Vector2(-40f, -40f);
            body.lineSpacing = 25f;
            skin.bodyText = body;
            skin.portraitBodyInset = 244f; // 头像窗宽 230 + 14 间距（与程序化默认一致）
            skin.portraitTagShift = 244f;

            // 头像窗（左下，可高出面板顶边）
            var window = new GameObject("PortraitWindow", typeof(RectTransform),
                typeof(UnityEngine.UI.RectMask2D));
            var windowRect = (RectTransform)window.transform;
            windowRect.SetParent(panelRect, false);
            windowRect.anchorMin = windowRect.anchorMax = Vector2.zero;
            windowRect.pivot = Vector2.zero;
            windowRect.anchoredPosition = new Vector2(14f, 12f);
            windowRect.sizeDelta = new Vector2(230f, 300f);
            var portrait = CreateImage(windowRect, "Portrait", null, Color.white, Image.Type.Simple);
            portrait.rectTransform.anchorMin = portrait.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            portrait.rectTransform.pivot = new Vector2(0.5f, 1f);
            window.SetActive(false);
            skin.portraitWindow = windowRect;
            skin.portraitImage = portrait;

            // 继续箭头
            var arrow = CreateText(panelRect, "Arrow", font, 26, TextAlignmentOptions.Center);
            arrow.text = "▼";
            var arrowRect = arrow.rectTransform;
            arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(1f, 0f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowRect.anchoredPosition = new Vector2(-38f, 26f);
            arrowRect.sizeDelta = new Vector2(40f, 34f);
            skin.arrow = arrow;

            return SavePrefab(root, name);
        }

        // ==============================================================
        // 选项面板皮肤 prefab
        // ==============================================================

        static GameObject BuildChoicePrefab(string name, Sprite rounded, bool right)
        {
            var font = VNFontAssetBuilder.EnsureFontAsset();

            var root = new GameObject(name, typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);
            var skin = root.AddComponent<VNChoiceSkin>();

            var container = new GameObject("Container", typeof(RectTransform));
            var containerRect = (RectTransform)container.transform;
            containerRect.SetParent(rootRect, false);
            Stretch(containerRect);
            skin.container = containerRect;

            // 按钮模板：默认居中列（与程序化样式一致）；right 变体锚到右侧
            var template = new GameObject("ButtonTemplate", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var templateRect = (RectTransform)template.transform;
            templateRect.SetParent(containerRect, false);
            if (right)
            {
                templateRect.anchorMin = templateRect.anchorMax = new Vector2(1f, 0.5f);
                templateRect.pivot = new Vector2(1f, 0.5f);
                templateRect.anchoredPosition = new Vector2(-64f, 190f);
                templateRect.sizeDelta = new Vector2(480f, 76f);
            }
            else
            {
                templateRect.anchorMin = templateRect.anchorMax = new Vector2(0.5f, 0.5f);
                templateRect.pivot = new Vector2(0.5f, 0.5f);
                templateRect.anchoredPosition = new Vector2(0f, 170f);
                templateRect.sizeDelta = new Vector2(560f, 84f);
            }
            var templateImage = template.GetComponent<Image>();
            templateImage.sprite = rounded;
            templateImage.type = Image.Type.Sliced;
            templateImage.color = new Color(0.07f, 0.09f, 0.16f, 0.88f);

            var label = CreateText(templateRect, "Label", font, 30,
                right ? TextAlignmentOptions.Left : TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            if (right) label.rectTransform.offsetMin = new Vector2(28f, 0f);
            skin.buttonLabel = label;

            var cost = CreateText(templateRect, "Cost", font, 22, TextAlignmentOptions.MidlineRight);
            var costRect = cost.rectTransform;
            costRect.anchorMin = new Vector2(1f, 0f);
            costRect.anchorMax = new Vector2(1f, 1f);
            costRect.pivot = new Vector2(1f, 0.5f);
            costRect.anchoredPosition = new Vector2(-18f, 0f);
            costRect.sizeDelta = new Vector2(160f, 0f);
            cost.fontStyle = FontStyles.Bold;
            cost.color = new Color(1f, 0.84f, 0.42f, 0.95f);
            skin.buttonCost = cost;

            template.SetActive(false); // 模板保持禁用，运行时克隆后激活
            skin.spacing = 26f;

            return SavePrefab(root, name);
        }

        // ==============================================================
        // 注册进 VNGameConfig
        // ==============================================================

        static void RegisterInConfig(GameObject dialogueDefault, GameObject dialogueTop,
            GameObject choiceDefault, GameObject choiceRight)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (cfg == null)
            {
                Debug.LogWarning("[VNUiSkin] 未找到 " + VNGameConfig.AssetPath +
                                 "：prefab 已导出但未登记。建了配置资产后重跑本菜单即可自动登记。");
                return;
            }

            bool changed = false;
            changed |= AddEntry(cfg.dialogueSkins, "经典", dialogueDefault);
            changed |= AddEntry(cfg.dialogueSkins, "顶部", dialogueTop);
            changed |= AddEntry(cfg.choiceSkins, "经典", choiceDefault);
            changed |= AddEntry(cfg.choiceSkins, "右列", choiceRight);
            if (changed)
            {
                EditorUtility.SetDirty(cfg);
                Debug.Log("[VNUiSkin] 已登记进 VNGameConfig：对话框皮肤 经典/顶部，选项皮肤 经典/右列");
            }
        }

        static bool AddEntry(List<VNGameConfig.UiSkinEntry> list, string id, GameObject prefab)
        {
            foreach (var e in list)
                if (e != null && e.id == id) return false; // 已登记：不动用户数据
            list.Add(new VNGameConfig.UiSkinEntry { id = id, prefab = prefab });
            return true;
        }

        // ==============================================================
        // 小工具
        // ==============================================================

        static GameObject SavePrefab(GameObject temp, string name)
        {
            string path = $"{SkinDir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        static Image CreateImage(RectTransform parent, string name, Sprite sprite,
            Color color, Image.Type type)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = type;
            image.raycastTarget = false;
            return image;
        }

        static TextMeshProUGUI CreateText(RectTransform parent, string name,
            TMP_FontAsset font, int size, TextAlignmentOptions anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = new Color(1f, 1f, 1f, 0.96f);
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
