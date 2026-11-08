using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    public partial class VNStage
    {
        // ------------------------------------------------------------------
        // 角色
        // ------------------------------------------------------------------

        static Vector2 SlotPosition(string at)
        {
            switch (at)
            {
                case "left": return new Vector2(-380f, -60f);
                case "right": return new Vector2(380f, -60f);
                case "center": return new Vector2(0f, -60f);
                default:
                    // 支持直接写数字 = 横向像素坐标
                    if (float.TryParse(at, out float x)) return new Vector2(x, -60f);
                    return new Vector2(0f, -60f);
            }
        }

        /// <summary>该角色的实际显示高度（舞台统一高度 × 角色尺寸标定）</summary>
        float HeightFor(VNCharacterDef def) =>
            characterHeight * Mathf.Max(0.05f, def.sizeScale);

        /// <summary>
        /// 出入场方向：剧本没写 from:/to: 时按站位推断——
        /// 站画面左侧的角色就从左边进来 / 往左边离开，右侧反之，
        /// 站中间的进场默认从下方（只走水平的预设会在 Animator 里折成左）。
        /// </summary>
        static VNSide SideFor(string keyword, float x, int line)
        {
            if (!string.IsNullOrEmpty(keyword))
            {
                switch (keyword.ToLower())
                {
                    case "left": return VNSide.Left;
                    case "right": return VNSide.Right;
                    case "top": return VNSide.Top;
                    case "bottom": return VNSide.Bottom;
                    case "auto": break;
                    default:
                        Debug.LogWarning($"[VNScript] 第 {line} 行：未知方向「{keyword}」" +
                                         "（left/right/top/bottom），改为按站位推断");
                        break;
                }
            }
            return x < -60f ? VNSide.Left : x > 60f ? VNSide.Right : VNSide.Bottom;
        }

        /// <summary>角色登场（已在场则换位置/表情并重播出场）</summary>
        public Sequence Show(string id, string at, string expr, string presetName, int line = 0) =>
            Show(id, at, expr, presetName, null, 0f, line);

        /// <summary>
        /// 角色登场。from = 进场方向（空 = 按站位推断）；
        /// duration = 目标时长秒（&lt;= 0 用预设基准时长）。
        /// </summary>
        public Sequence Show(string id, string at, string expr, string presetName,
            string from, float duration, int line = 0)
        {
            var def = characters.Find(c => c.id == id);
            if (def == null)
            {
                Debug.LogError($"[VNScript] 第 {line} 行：未注册的角色「{id}」（检查 VNStage.characters）");
                return null;
            }

            var c = Get(id) ?? CreateCharacter(def);
            c.fx.SetBaseRotationZ(def.rotationZOffset);
            if (!string.IsNullOrEmpty(at))
            {
                // 标准站位 + 该角色的标定偏移（吸收素材构图差异）
                var pos = SlotPosition(at) + def.positionOffset;
                c.rect.anchoredPosition = pos;
                // 关键：同步各组件缓存的"基准位"，否则出场动画会把角色重置回旧位置
                c.animator.SetBasePosition(pos);
                c.emotes.SetBasePosition(pos);
                c.fx.SetFloatBaseY(pos.y);
            }
            ApplyExpression(c, expr);

            var preset = VNScriptParser.ParseEnum(presetName, VNEntrancePreset.Crossfade, line);
            var side = SideFor(from, c.rect.anchoredPosition.x, line);
            float scale = duration > 0.01f
                ? duration / VNEntranceAnimator.BaseDuration(preset) : 1f;

            var seq = c.animator.PlayEntrance(preset, side, scale);
            // 日常向预设不开周期扫光：每隔几秒闪一下对频繁的对话切换太吵
            c.casualEntrance = VNEntranceAnimator.IsCasual(preset);
            float shine = c.casualEntrance ? 0f : 7f;
            seq.OnComplete(() => c.animator.StartIdleEffects(shineInterval: shine));
            return seq;
        }

        /// <summary>角色退场（预设 fade / dissolve / runout / sink），完成后销毁</summary>
        public Sequence Hide(string id, string style, int line = 0) =>
            Hide(id, style, null, 0f, line);

        /// <summary>
        /// 角色退场。to = 离开方向（空 = 按站位推断，只有 runout 用）；
        /// duration = 目标时长秒（&lt;= 0 用预设基准时长）。
        /// </summary>
        public Sequence Hide(string id, string style, string to, float duration, int line = 0)
        {
            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：hide 的角色「{id}」不在场上");
                return null;
            }
            c.mouth?.ForceClosed();
            _active.Remove(id);
            RefreshRegistries();

            var preset = VNScriptParser.ParseEnum(style, VNExitPreset.Fade, line);
            var side = SideFor(to, c.rect.anchoredPosition.x, line);
            float scale = duration > 0.01f
                ? duration / VNEntranceAnimator.BaseDuration(preset) : 1f;

            var seq = c.animator.PlayExit(preset, side, scale);
            seq.OnComplete(() => Destroy(c.go));
            return seq;
        }

        /// <summary>情绪演出动作</summary>
        public Sequence Emote(string id, string emoteName, int line = 0)
        {
            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：emote 的角色「{id}」不在场上");
                return null;
            }
            // 动作清单的唯一真相在 VNEmoteCatalog（反射 VNCharacterEmotes 上的 [VNEmote]），
            // 加动作不用回来改这里
            if (!VNEmoteCatalog.TryGet(emoteName, out var entry))
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：未知情绪动作「{emoteName}」" +
                                 $"（可用：{VNEmoteCatalog.NamesJoined()}）");
                return null;
            }
            return entry.invoke(c.emotes);
        }

        /// <summary>
        /// 漫符：在立绘上方弹出汗滴 / 井字怒气 / 感叹号等符号。
        /// 返回值只含"弹出"那一小段，停留与消失在外面自己跑（详见 VNCharacterMarks.Show）。
        /// </summary>
        public Sequence Mark(string id, string name, string mode, Vector2? pos,
            float size, float hold, int line = 0)
        {
            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：mark 的角色「{id}」不在场上");
                return null;
            }
            if (c.marks == null) return null;

            if (string.IsNullOrEmpty(name) || name == "clear")
            {
                c.marks.ClearAll();
                return null;
            }
            if (!VNCharacterMarks.TryParse(name, out var kind))
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：未知漫符「{name}」，可用：" +
                                 string.Join(" / ", VNCharacterMarks.CanonicalNames));
                return null;
            }
            if (mode == "off")
            {
                c.marks.FadeOut(kind);
                return null;
            }
            return c.marks.Show(kind, mode == "keep", pos, size, hold);
        }

        /// <summary>切换表情立绘（P0 为瞬间切换）</summary>
        /// <summary>
        /// 设置角色的情绪叠加层强度（潮红/汗/泪）。layer = "clear" 清空全部。
        /// 剧本 overlay 命令与互动模块共用这一个入口。
        /// </summary>
        public void SetOverlay(string id, string layer, float strength, float time, int line = 0)
        {
            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：角色「{id}」不在场，overlay 无效");
                return;
            }
            if (c.overlay == null) return;

            if (string.IsNullOrEmpty(layer) || layer == "clear" || layer == "off")
            {
                c.overlay.ClearAll(time);
                return;
            }
            if (!c.overlay.SetStrength(layer, strength, time))
                Debug.LogWarning($"[VNScript] 第 {line} 行：角色「{id}」没有叠加层「{layer}」" +
                                 "（在 VNCharacterDef.overlays 登记）");
        }

        /// <summary>
        /// 印一枚立绘痕迹。pos 是立绘归一化坐标（-0.5~0.5），与部位框同一套语义。
        /// id 为 clear/off 时清空全部。剧本 imprint 命令与互动模块共用这一个入口。
        /// </summary>
        public void Imprint(string id, string charId, Vector2 pos, float size, float life,
            float rotation, string zoneId = null, int line = 0)
        {
            var c = Get(charId);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：角色「{charId}」不在场，imprint 无效");
                return;
            }
            if (c.imprints == null) return;

            if (string.IsNullOrEmpty(id) || id == "clear" || id == "off")
            {
                c.imprints.ClearAll();
                return;
            }
            if (!c.imprints.Add(id, pos, size, life, rotation, zoneId))
                Debug.LogWarning($"[VNScript] 第 {line} 行：角色「{charId}」没有痕迹「{id}」" +
                                 "（在 VNCharacterDef.imprints 登记）");
        }

        public void SetExpression(string id, string expr)
        {
            var c = Get(id);
            if (c != null) ApplyExpression(c, expr);
        }

        void ApplyExpression(ActiveCharacter c, string expr)
        {
            var sprite = c.def.GetSprite(expr);
            if (sprite == null) return;

            bool isDefault = c.def.IsDefaultExpression(expr);
            c.blink?.PrepareForExpressionChange();
            c.blinkOverlay?.PrepareForExpressionChange();
            c.mouth?.PrepareForExpressionChange();

            if (sprite == c.image.sprite)
            {
                c.expression = expr;
                c.blink?.SetExpression(sprite, isDefault);
                c.blinkOverlay?.SetExpression(isDefault);
                c.mouth?.SetExpression(expr);
                return;
            }

            // 交叉溶解：角色完全可见时，旧表情以"残像"覆盖在上面淡出（新表情立即生效）
            var group = c.go.GetComponent<CanvasGroup>();
            bool visible = c.fx.GetDissolve() > 0.9f && (group == null || group.alpha > 0.5f);
            if (visible && expressionCrossfade > 0.01f && c.image.sprite != null)
                SpawnExpressionGhost(c);

            c.image.sprite = sprite;
            c.expression = expr;
            // 不同表情图宽高比可能不同 → 以该角色的标定高度重算宽度
            float h = HeightFor(c.def);
            float aspect = sprite.rect.width / sprite.rect.height;
            c.rect.sizeDelta = new Vector2(h * aspect, h);
            c.blink?.SetExpression(sprite, isDefault);
            c.blinkOverlay?.SetExpression(isDefault);
            c.mouth?.SetExpression(expr);
        }

        /// <summary>复制一份旧表情立绘覆盖在角色上淡出（表情交叉溶解）</summary>
        void SpawnExpressionGhost(ActiveCharacter c)
        {
            var go = new GameObject("ExprGhost",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(c.rect.parent, false);
            rect.SetSiblingIndex(c.rect.GetSiblingIndex() + 1); // 盖在本体之上
            rect.anchorMin = c.rect.anchorMin;
            rect.anchorMax = c.rect.anchorMax;
            rect.pivot = c.rect.pivot;
            rect.sizeDelta = c.rect.sizeDelta;
            rect.anchoredPosition = c.rect.anchoredPosition;
            rect.localScale = c.rect.localScale;
            rect.localRotation = c.rect.localRotation;

            var img = go.GetComponent<Image>();
            img.sprite = c.image.sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;

            img.DOFade(0f, expressionCrossfade).SetEase(Ease.InOutSine)
               .SetLink(go).OnComplete(() => Destroy(go));
        }

        /// <summary>角色滑步换位（move 命令）：悬浮暂停、基准位同步、到位后恢复</summary>
        public Tween Move(string id, string at, float duration, int line = 0)
        {
            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：move 的角色「{id}」不在场上");
                return null;
            }

            var target = SlotPosition(at) + c.def.positionOffset;
            bool wasFloating = c.fx.IsFloating;
            c.fx.StopFloating(); // 会重置到旧基准位，从干净状态起步
            c.animator.SetBasePosition(target);
            c.emotes.SetBasePosition(target);
            c.fx.SetFloatBaseY(target.y);

            var t = c.rect.DOAnchorPos(target, Mathf.Max(0.05f, duration))
                          .SetEase(Ease.InOutSine).SetLink(c.go);
            if (wasFloating) t.OnComplete(() => c.fx.ResumeFloating());
            return t;
        }

        /// <summary>运行时生成完整的角色特效组件栈</summary>
        ActiveCharacter CreateCharacter(VNCharacterDef def)
        {
            var go = new GameObject($"Char_{def.id}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(characterLayer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -60f) + def.positionOffset;

            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            var sprite = def.GetSprite(null);
            img.sprite = sprite;
            if (sprite != null)
            {
                float h = HeightFor(def);
                float aspect = sprite.rect.width / sprite.rect.height;
                rect.sizeDelta = new Vector2(h * aspect, h);
            }

            var c = new ActiveCharacter
            {
                def = def,
                go = go,
                image = img,
                rect = rect,
                fx = go.AddComponent<VNImageEffectController>(),
            };
            c.fx.SetBaseRotationZ(def.rotationZOffset);
            go.AddComponent<VNGlowBackdrop>();
            c.animator = go.AddComponent<VNEntranceAnimator>();
            c.emotes = go.AddComponent<VNCharacterEmotes>();
            c.blink = go.AddComponent<VNCharacterBlink>();
            c.blink.Initialize(img, def);
            c.blinkOverlay = go.AddComponent<VNCharacterBlinkOverlay>();
            c.blinkOverlay.Initialize(img, def, c.fx.Mat);
            c.mouth = go.AddComponent<VNCharacterMouth>();
            c.mouth.Initialize(img, def, c.fx.Mat);
            c.marks = go.AddComponent<VNCharacterMarks>();
            c.marks.Initialize(rect, def, c.fx.Mat);
            c.overlay = go.AddComponent<VNCharacterOverlay>();
            c.overlay.Initialize(def, c.fx.Mat);
            c.imprints = go.AddComponent<VNCharacterImprints>();
            c.imprints.Initialize(rect, def, c.fx.Mat);
            go.AddComponent<VNFootShadow>();

            _active[def.id] = c;
            if (speakerHighlight != null) speakerHighlight.Register(c.fx);
            RefreshRegistries();
            return c;
        }

        // ------------------------------------------------------------------
        // 镜头目标点解析（camseq / camto / camcut 用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 解析镜头目标点 token → 画布坐标（中心为原点，1920×1080 坐标系）。
        /// 支持：
        ///   九宫格锚点：topleft top topright left middle(center) right
        ///               bottomleft bottom bottomright，origin/reset = 中心
        ///   角色[:部位]：亚里沙 / 亚里沙:head|chest|waist|feet|up|mid|down
        ///   裸坐标：300,200
        /// 解析失败返回 null 并告警。
        /// </summary>
        public Vector2? ResolveCamPoint(string token, int line = 0)
        {
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：镜头目标点为空");
                return null;
            }

            // 九宫格锚点
            switch (token.ToLower())
            {
                case "topleft": return new Vector2(-620f, 340f);
                case "top": return new Vector2(0f, 340f);
                case "topright": return new Vector2(620f, 340f);
                case "left": return new Vector2(-620f, 0f);
                case "middle":
                case "center":
                case "origin":
                case "reset": return Vector2.zero;
                case "right": return new Vector2(620f, 0f);
                case "bottomleft": return new Vector2(-620f, -340f);
                case "bottom": return new Vector2(0f, -340f);
                case "bottomright": return new Vector2(620f, -340f);
            }

            // 裸坐标 x,y
            int comma = token.IndexOf(',');
            if (comma > 0)
            {
                if (float.TryParse(token.Substring(0, comma), out float px) &&
                    float.TryParse(token.Substring(comma + 1), out float py))
                    return new Vector2(px, py);
                Debug.LogWarning($"[VNScript] 第 {line} 行：坐标「{token}」格式应为 x,y");
                return null;
            }

            // 角色[:部位]
            string id = token;
            string part = null;
            int colon = token.IndexOf(':');
            if (colon > 0)
            {
                id = token.Substring(0, colon);
                part = token.Substring(colon + 1).ToLower();
            }

            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：镜头目标「{token}」不是锚点/坐标，角色「{id}」也不在场上");
                return null;
            }

            float frac = 0f; // 相对立绘高度的纵向偏移比例
            switch (part)
            {
                case null: case "": frac = 0f; break;
                case "head": frac = 0.36f; break;
                case "chest": frac = 0.15f; break;
                case "waist": frac = -0.08f; break;
                case "feet": frac = -0.42f; break;
                case "up": frac = 0.3f; break;
                case "mid": frac = 0f; break;
                case "down": frac = -0.3f; break;
                default:
                    Debug.LogWarning($"[VNScript] 第 {line} 行：未知身体部位「{part}」" +
                                     "（head/chest/waist/feet/up/mid/down），使用角色中心");
                    break;
            }
            return c.rect.anchoredPosition + new Vector2(0f, c.rect.sizeDelta.y * frac);
        }

        /// <summary>角色显示名（未注册时原样返回，Backlog 用）</summary>
        public string GetDisplayName(string speaker)
        {
            var def = characters.Find(c => c.id == speaker);
            return def != null ? def.LocalizedDisplayName : speaker;
        }
    }
}
