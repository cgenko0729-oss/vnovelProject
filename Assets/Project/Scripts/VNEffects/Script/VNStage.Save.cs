using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    public partial class VNStage
    {
        // ------------------------------------------------------------------
        // UI 皮肤（对话框 / 选项面板）
        // ------------------------------------------------------------------

        /// <summary>当前对话框皮肤 id（null = 程序化默认，进存档快照）</summary>
        public string CurrentDialogueSkinId { get; private set; }
        /// <summary>当前选项面板皮肤 id（null = 程序化默认，进存档快照）</summary>
        public string CurrentChoiceSkinId { get; private set; }
        /// <summary>当前名字样式名（null = 出厂样式，进存档快照）</summary>
        public string CurrentNameplateStyleId { get; private set; }

        /// <summary>
        /// 剧本 ui 命令入口：ui dialogue|choice &lt;id|default&gt;。
        /// id 在 VNGameConfig 的 dialogueSkins/choiceSkins 里登记；
        /// default（或空）= 回程序化默认样式。找不到 id 时报错并保持现状。
        /// </summary>
        public void SetUiSkin(string kind, string id, int line = 0)
        {
            bool toDefault = string.IsNullOrEmpty(id) || id == "default";
            var cfg = VNGameConfig.Active;

            switch (kind)
            {
                case "dialogue":
                {
                    VNDialogueSkin skin = null;
                    if (!toDefault)
                    {
                        var prefab = cfg != null
                            ? VNGameConfig.FindSkin(cfg.dialogueSkins, id) : null;
                        skin = prefab != null ? prefab.GetComponent<VNDialogueSkin>() : null;
                        if (skin == null)
                        {
                            Debug.LogError($"[VNScript] 第 {line} 行：对话框皮肤「{id}」未在 " +
                                           "VNGameConfig.dialogueSkins 登记（或 prefab 缺 VNDialogueSkin）");
                            return;
                        }
                    }
                    dialogue?.ApplySkin(skin);
                    CurrentDialogueSkinId = toDefault ? null : id;
                    break;
                }
                case "choice":
                {
                    VNChoiceSkin skin = null;
                    if (!toDefault)
                    {
                        var prefab = cfg != null
                            ? VNGameConfig.FindSkin(cfg.choiceSkins, id) : null;
                        skin = prefab != null ? prefab.GetComponent<VNChoiceSkin>() : null;
                        if (skin == null)
                        {
                            Debug.LogError($"[VNScript] 第 {line} 行：选项皮肤「{id}」未在 " +
                                           "VNGameConfig.choiceSkins 登记（或 prefab 缺 VNChoiceSkin）");
                            return;
                        }
                    }
                    choicePanel?.ApplySkin(skin);
                    CurrentChoiceSkinId = toDefault ? null : id;
                    break;
                }
                case "name":
                {
                    // ui name <样式|default>：名字（说话人）的装饰样式。
                    // 与 dialogue/choice 不同——这里的 id 不是 VNGameConfig 里登记的 prefab，
                    // 而是 VNNameplateStyle 的内置预设名（中英双写，见 Aliases）。
                    var styleId = VNNameplateStyleId.Bold; // default = 出厂样式
                    if (!toDefault && !VNNameplateStyle.TryParseId(id, out styleId))
                    {
                        var names = new System.Text.StringBuilder();
                        foreach (var a in VNNameplateStyle.Aliases)
                        {
                            if (names.Length > 0) names.Append(' ');
                            names.Append(a.name);
                        }
                        Debug.LogError($"[VNScript] 第 {line} 行：名字样式「{id}」不认识。" +
                                       $"可用：{names} default");
                        return;
                    }
                    dialogue?.SetNameplateStyle(styleId);
                    CurrentNameplateStyleId = toDefault ? null : VNNameplateStyle.NameOf(styleId);
                    break;
                }
                default:
                    Debug.LogWarning($"[VNScript] 第 {line} 行：ui 命令用法为" +
                                     "「ui dialogue|choice|name <id|default>」");
                    break;
            }
        }

        // ------------------------------------------------------------------
        // 存档快照 / 读档恢复
        // ------------------------------------------------------------------

        /// <summary>把当前舞台状态写入存档数据</summary>
        public void CaptureSnapshot(VNSaveData data)
        {
            data.backgroundId = CurrentBackgroundId;
            // CG 暂停环境特效期间：存的是"CG 背后"的天气与 fx（读档重放 CG 时会再次暂停）
            data.weather = _cgFxPaused && !string.IsNullOrEmpty(_cgSavedWeatherId)
                ? _cgSavedWeatherId
                : weather != null ? weather.CurrentId : null;
            data.weatherDensity = _weatherDensity;
            data.weatherSpeed = _weatherSpeed;
            data.weatherSize = _weatherSize;
            data.weatherWindSet = _weatherWindSet;
            data.weatherWind = _weatherWind;
            if (bgScroll != null)
            {
                data.scrollOn = bgScroll.IsScrolling;
                data.scrollSpeed = bgScroll.Speed;
                data.scrollDir = bgScroll.DirectionDeg;
                data.scrollMode = bgScroll.Mode.ToString();
            }
            data.mood = mood != null ? mood.Current.ToString() : null;
            data.bgm = vnAudio != null ? vnAudio.CurrentBgm : null;
            data.bgmVol = vnAudio != null ? vnAudio.CurrentBgmVol : 1f;
            data.portraitOff = _portraitOff;
            data.cgId = CurrentCgId;
            data.cgKeepChars = _cgKeepChars;
            data.cgKeepFx = _cgKeepFx;
            data.cgFit = _cgFit;
            data.dialogueSkin = CurrentDialogueSkinId;
            data.choiceSkin = CurrentChoiceSkinId;
            data.nameplateStyle = CurrentNameplateStyleId;

            CaptureLiquid(data);

            data.fxOn.Clear();
            foreach (var kv in _fxStates)
                if (kv.Value) data.fxOn.Add(kv.Key);
            foreach (var name in _cgPausedFx)
                if (!data.fxOn.Contains(name)) data.fxOn.Add(name);

            // SNS 会话（未打开时写入 snsOpen=false，字段留空）
            if (sns != null) sns.CaptureSnapshot(data);
            VNAiMemory.CaptureSnapshot(data);   // AI 聊天的跨场记忆（存档态）

            data.characters.Clear();
            foreach (var kv in _active)
            {
                data.characters.Add(new VNSaveData.CharSave
                {
                    id = kv.Key,
                    x = kv.Value.rect.anchoredPosition.x,
                    expr = kv.Value.expression,
                    // 只有 keep 漫符是持续状态；一次性符号播完即逝，不进存档
                    marks = kv.Value.marks != null ? kv.Value.marks.SerializeKeep() : null,
                    overlays = kv.Value.overlay != null ? kv.Value.overlay.Serialize() : null,
                    casualEntrance = kv.Value.casualEntrance,
                });
            }
        }

        /// <summary>清空舞台并按存档数据瞬间摆台（读档用）</summary>
        public void RestoreSnapshot(VNSaveData data) => RestoreSnapshot(data, false);

        /// <summary>恢复舞台快照；instant 用于“从选中行播放”的静默状态重建。</summary>
        public void RestoreSnapshot(VNSaveData data, bool instant)
        {
            ClearStage();

            // UI 皮肤最先恢复：随后重放的台词/选项直接落在正确皮肤上
            // （旧存档字段为空 = 默认样式，且仅在与当前不同时才切换，避免无谓重建）
            if (data.dialogueSkin != CurrentDialogueSkinId)
                SetUiSkin("dialogue", string.IsNullOrEmpty(data.dialogueSkin)
                    ? "default" : data.dialogueSkin);
            if (data.choiceSkin != CurrentChoiceSkinId)
                SetUiSkin("choice", string.IsNullOrEmpty(data.choiceSkin)
                    ? "default" : data.choiceSkin);
            if (data.nameplateStyle != CurrentNameplateStyleId)
                SetUiSkin("name", string.IsNullOrEmpty(data.nameplateStyle)
                    ? "default" : data.nameplateStyle);

            if (!string.IsNullOrEmpty(data.backgroundId))
                SetBackground(data.backgroundId, null);
            else
            {
                CurrentBackgroundId = null;
                if (backgroundImage != null) backgroundImage.sprite = null;
            }

            // 旧存档里 weather 是 VNWeather 枚举名（"Petals"/"Rain"），
            // SetWeatherId 的三级解析照常认得，不需要迁移
            SetWeather(data.weather, data.weatherDensity, data.weatherWind,
                data.weatherWindSet, data.weatherSpeed, data.weatherSize,
                instant ? 0.01f : 0.1f);
            // 背景滚动：读档直接就位，不缓入（缓入会让读档后画面先"起步"一下）
            if (bgScroll != null)
                bgScroll.RestoreState(data.scrollOn,
                    data.scrollSpeed > 0f ? data.scrollSpeed : VNBackgroundScroll.DefaultSpeed,
                    data.scrollDir,
                    VNScriptParser.ParseEnum(data.scrollMode, VNScrollMode.Mirror, 0));

            var restoredMood = VNScriptParser.ParseEnum(data.mood, VNMood.Neutral, 0);
            if (mood != null)
                mood.SetMood(restoredMood, instant ? 0.01f : 0.3f);

            // 先全部关掉可开关 fx，再打开存档里记录的
            foreach (var name in ToggleFxNames) Fx(name, "off");
            foreach (var name in data.fxOn) Fx(name, "on");
            // 黑边若与回忆色调同时恢复，视为自动黑边（之后离开回忆会自动撤掉）
            _letterboxAuto = restoredMood == VNMood.Memory && data.fxOn.Contains("letterbox");
            // 复古滤镜同理：与对应 mood 同时恢复视为自动滤镜
            _retroAuto = (restoredMood == VNMood.Memory && data.fxOn.Contains("filmgrain")) ||
                         (restoredMood == VNMood.Dream && data.fxOn.Contains("crt"));

            if (vnAudio != null)
            {
                if (instant) vnAudio.ResetForDebug();
                float fade = instant ? 0.01f : 0.6f;
                if (!string.IsNullOrEmpty(data.bgm))
                    vnAudio.PlayBgm(data.bgm, fade, data.bgmVol > 0f ? data.bgmVol : 1f);
                else vnAudio.StopBgm(fade);
            }

            SetPortraitEnabled(!data.portraitOff);
            RestoreLiquid(data);

            foreach (var cs in data.characters)
            {
                ShowInstant(cs.id, cs.x, cs.expr, cs.marks, cs.casualEntrance);
                // 叠加层单独还原（不塞进 ShowInstant 签名，那个重载已经四个参数了）
                Get(cs.id)?.overlay?.Restore(cs.overlays);
            }

            // CG 最后重放：立绘/天气/fx 已就位，ShowCg 会按 keep 参数再次隐藏/暂停
            if (!string.IsNullOrEmpty(data.cgId))
                ShowCg(data.cgId, null, data.cgKeepChars, data.cgKeepFx, 0, true,
                       fit: data.cgFit);

            // SNS 最后恢复：会话开着就按消息列表重建整屏气泡，否则确保界面收起
            if (sns != null) sns.RestoreSnapshot(data, this);
            VNAiMemory.RestoreSnapshot(data);   // 与 CaptureSnapshot 成对
        }

        /// <summary>SNS 手机聊天界面是否打开（Runner 据此把台词渲染成气泡）</summary>
        public bool IsSnsOpen => sns != null && sns.IsOpen;

        /// <summary>清空舞台：销毁全部在场角色、关闭残留的选项面板与 SNS 界面</summary>
        public void ClearStage()
        {
            StopSpeaking();
            if (sns != null && sns.IsOpen) sns.Close(true); // 读档/停止剧本时不留残留手机
            if (interlude != null) interlude.HideImmediate(); // 过场层同理，不能留在屏幕上
            ResetCgState(); // CG 状态清零（立绘层复显；天气/fx 由随后的恢复流程接管）
            foreach (var kv in _active)
                if (kv.Value.go != null) Destroy(kv.Value.go);
            _active.Clear();
            if (choicePanel != null) choicePanel.ForceClose();
            RefreshRegistries();
        }

        /// <summary>瞬间摆台一个角色（无出场动画，读档用）</summary>
        public void ShowInstant(string id, float x, string expr) =>
            ShowInstant(id, x, expr, null, false);

        /// <summary>瞬间摆台一个角色，并还原常驻漫符（marks = 英文正名逗号串，旧存档为空）</summary>
        public void ShowInstant(string id, float x, string expr, string marks) =>
            ShowInstant(id, x, expr, marks, false);

        /// <summary>
        /// 瞬间摆台一个角色。casual = 登场时用的是日常向预设，
        /// 恢复常驻效果时同样不开周期扫光（否则读档后画面会突然开始闪）。
        /// </summary>
        public void ShowInstant(string id, float x, string expr, string marks, bool casual)
        {
            var def = characters.Find(c => c.id == id);
            if (def == null)
            {
                Debug.LogError($"[VNSave] 存档里的角色「{id}」未在 VNStage.characters 注册");
                return;
            }

            var c = Get(id) ?? CreateCharacter(def);
            c.fx.SetBaseRotationZ(def.rotationZOffset);
            // 存档里的 x 已含偏移，y 用标定偏移重建
            var pos = new Vector2(x, -60f + def.positionOffset.y);
            c.rect.anchoredPosition = pos;
            c.animator.SetBasePosition(pos);
            c.emotes.SetBasePosition(pos);
            c.fx.SetFloatBaseY(pos.y);
            ApplyExpression(c, expr);

            // 直接置为完全可见状态
            c.fx.SetDissolve(1f);
            c.fx.SetFlash(0f);
            var group = c.go.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 1f;
            c.casualEntrance = casual;
            c.animator.StartIdleEffects(shineInterval: casual ? 0f : 7f);

            // 常驻漫符：先清干净再按存档重挂，避免同一角色被反复摆台时越堆越多
            if (c.marks != null)
            {
                c.marks.ClearImmediate();
                if (!string.IsNullOrEmpty(marks))
                    foreach (var name in marks.Split(','))
                        if (VNCharacterMarks.TryParse(name, out var kind))
                            c.marks.ShowInstant(kind);
            }
        }

        /// <summary>在场角色变化后，刷新色调匹配等注册表</summary>
        void RefreshRegistries()
        {
            if (speakerHighlight != null)
                speakerHighlight.characters.RemoveAll(f => f == null);

            var characterFx = new List<VNImageEffectController>();
            foreach (var kv in _active) characterFx.Add(kv.Value.fx);

            if (toneMatch != null)
            {
                toneMatch.characters = characterFx.ToArray();
                if (backgroundImage != null && backgroundImage.sprite != null)
                    toneMatch.MatchTo(backgroundImage.sprite);
            }

            // 情绪色调的分层目标：中途出场的角色也要立刻带上当前情绪的颜色
            if (mood != null) mood.SetCharacterTargets(characterFx);

            // 运镜缩放（camseq mode:depth/bg/char）的目标，与情绪色调同一时机维护
            if (vnCamera != null) vnCamera.SetCharacterTargets(characterFx);
        }
    }
}
