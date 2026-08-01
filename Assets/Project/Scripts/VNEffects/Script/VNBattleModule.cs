using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：回合制小战斗（事件接口 P4 的"重玩法"验证）。
    /// 我方选择 攻击/重击/防御/逃跑 → 敌方反击，直到一方 HP 归零或逃脱。
    /// UI 全程序化生成（敌人 = 光晕色块 + 双眼），零素材依赖。
    ///
    /// 剧本用法（参数全部可省，示 default）：
    ///   event battle enemy:暗影史莱姆 ehp:26 eatk:5 php:30 patk:6 pdef:1 escape:50
    ///   * 胜利 -> 打赢了
    ///   * 失败 -> 打输了
    ///   * 逃跑 -> 溜了
    ///
    /// 与养成系统联动（可选）：patkstat:体力 → 我方攻击改读 flag「体力」
    /// （同理 phpstat / pdefstat）；剧本先 stat 练属性，战斗自动变强——
    /// 属性影响战斗的桥全在 flags，模块不认识任何具体属性名。
    ///
    /// 结果名固定中文：胜利 / 失败 / 逃跑（事件结果名是逻辑标识符，永不翻译）。
    /// 战斗结束额外写 flag「战斗剩余HP」（失败时为 0），供剧本做车轮战/伤势分支。
    ///
    /// 遵守事件模块三铁律：不碰舞台演出 / unscaled 计时 + SetUpdate(true) /
    /// 全部 Tween SetLink。
    /// </summary>
    public class VNBattleModule : VNEventModule
    {
        [Header("默认参数（剧本 kwargs 可覆盖）")]
        [Header("敌人名（剧本 enemy: 覆盖）")]
        public string enemyName = "魔物";
        [Header("敌人 HP / 攻击（剧本 ehp: / eatk: 覆盖）")]
        public int enemyHp = 25;
        public int enemyAtk = 5;
        [Header("我方 HP / 攻击 / 防御（剧本 php: / patk: / pdef: 覆盖）")]
        public int playerHp = 30;
        public int playerAtk = 6;
        public int playerDef = 0;
        [Header("逃跑成功率 %（剧本 escape: 覆盖；0 = 不显示逃跑按钮）")]
        public int escapeChance = 50;

        static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.14f, 0.92f);
        static readonly Color PlayerHpColor = new Color(0.4f, 1f, 0.6f, 1f);
        static readonly Color EnemyHpColor = new Color(1f, 0.42f, 0.4f, 1f);
        static readonly Color EnemyBodyColor = new Color(0.55f, 0.3f, 0.85f, 0.95f);
        static readonly Color WinColor = new Color(1f, 0.85f, 0.4f, 1f);
        static readonly Color LoseColor = new Color(0.75f, 0.75f, 0.78f, 1f);

        enum Phase { PlayerTurn, Resolving, Ending }
        Phase _phase = Phase.Resolving;

        int _php, _phpMax, _patk, _pdef;
        int _ehp, _ehpMax, _eatk;
        bool _guarding;          // 本回合防御：受伤减 60%
        string _playerLabel;
        System.Random _rng = new System.Random();

        RectTransform _panel;
        RectTransform _enemyBody;
        Image _enemyImage;
        RectTransform _playerHpFill, _enemyHpFill;
        TextMeshProUGUI _playerHpText, _enemyHpText, _log, _banner;
        Button[] _actionButtons;

        protected override void OnLaunch(VNEventContext ctx)
        {
            enemyName = ctx.Kw("enemy", enemyName);
            _ehpMax = _ehp = Mathf.Max(1, ctx.KwI("ehp", enemyHp));
            _eatk = Mathf.Max(1, ctx.KwI("eatk", enemyAtk));
            _phpMax = _php = Mathf.Max(1, StatOrKw(ctx, "phpstat", "php", playerHp));
            _patk = Mathf.Max(1, StatOrKw(ctx, "patkstat", "patk", playerAtk));
            _pdef = Mathf.Max(0, StatOrKw(ctx, "pdefstat", "pdef", playerDef));
            escapeChance = Mathf.Clamp(ctx.KwI("escape", escapeChance), 0, 100);
            _playerLabel = ctx.Kw("pname", VNLocale.T("battle.player"));

            BuildUi(ctx.Kw("title", VNLocale.T("battle.title", enemyName)));
            SetLog(VNLocale.T("battle.logStart", enemyName));
            // 入场演出一拍后进入我方回合
            DOVirtual.DelayedCall(0.7f, StartPlayerTurn, true).SetLink(gameObject);
        }

        /// <summary>数值来源三级：statKey 指定的 flag（养成联动）> 剧本直填 > 组件默认</summary>
        static int StatOrKw(VNEventContext ctx, string statKey, string valueKey, int def)
        {
            string statName = ctx.Kw(statKey);
            if (!string.IsNullOrEmpty(statName)) return VNFlags.Get(statName);
            return ctx.KwI(valueKey, def);
        }

        // ------------------------------------------------------------------
        // 回合流程
        // ------------------------------------------------------------------

        void StartPlayerTurn()
        {
            _guarding = false;
            _phase = Phase.PlayerTurn;
            SetButtonsInteractable(true);
            SetLog(VNLocale.T("battle.logPlayerTurn"));
        }

        void OnAttack()
        {
            if (_phase != Phase.PlayerTurn) return;
            BeginResolve();

            int damage = Variance(_patk);
            bool crit = _rng.Next(100) < 10;
            if (crit) damage = Mathf.RoundToInt(damage * 1.7f);
            DealToEnemy(damage, crit);
        }

        void OnHeavy()
        {
            if (_phase != Phase.PlayerTurn) return;
            BeginResolve();

            if (_rng.Next(100) < 65)
                DealToEnemy(Mathf.RoundToInt(Variance(_patk) * 1.8f), true);
            else
            {
                SetLog(VNLocale.T("battle.logMiss", _playerLabel));
                PopText(_enemyBody, VNLocale.T("battle.miss"),
                    new Color(0.8f, 0.85f, 1f, 1f));
                DOVirtual.DelayedCall(0.8f, EnemyTurn, true).SetLink(gameObject);
            }
        }

        void OnGuard()
        {
            if (_phase != Phase.PlayerTurn) return;
            BeginResolve();
            _guarding = true;
            SetLog(VNLocale.T("battle.logGuard", _playerLabel));
            DOVirtual.DelayedCall(0.7f, EnemyTurn, true).SetLink(gameObject);
        }

        void OnEscape()
        {
            if (_phase != Phase.PlayerTurn) return;
            BeginResolve();
            if (_rng.Next(100) < escapeChance)
            {
                ShowBanner(VNLocale.T("battle.escaped"), LoseColor);
                FinishBattle("逃跑");
            }
            else
            {
                SetLog(VNLocale.T("battle.logEscapeFail"));
                DOVirtual.DelayedCall(0.8f, EnemyTurn, true).SetLink(gameObject);
            }
        }

        void BeginResolve()
        {
            _phase = Phase.Resolving;
            SetButtonsInteractable(false);
        }

        void DealToEnemy(int damage, bool crit)
        {
            _ehp = Mathf.Max(0, _ehp - damage);
            RefreshHp();
            SetLog(VNLocale.T(crit ? "battle.logCrit" : "battle.logHit",
                enemyName, damage));
            PopText(_enemyBody, "-" + damage,
                crit ? WinColor : new Color(1f, 0.95f, 0.9f, 1f), crit ? 44 : 34);

            // 受击演出：白闪 + 抖动
            _enemyImage.DOKill();
            _enemyImage.color = Color.white;
            _enemyImage.DOColor(EnemyBodyColor, 0.3f).SetUpdate(true).SetLink(gameObject);
            _enemyBody.DOKill(true);
            _enemyBody.DOShakeAnchorPos(0.3f, 18f, 30, 90f, false, true)
                      .SetUpdate(true).SetLink(gameObject);

            if (_ehp <= 0)
            {
                // 击破：敌人缩没 + 胜利横幅
                _enemyBody.DOScale(0f, 0.5f).SetEase(Ease.InBack)
                          .SetUpdate(true).SetLink(gameObject);
                ShowBanner(VNLocale.T("battle.win"), WinColor);
                FinishBattle("胜利");
                return;
            }
            DOVirtual.DelayedCall(0.85f, EnemyTurn, true).SetLink(gameObject);
        }

        void EnemyTurn()
        {
            if (_phase == Phase.Ending) return;

            // 敌人扑向我方一小步再回位
            _enemyBody.DOKill(true);
            _enemyBody.DOPunchAnchorPos(new Vector2(0f, -60f), 0.4f, 6, 0.4f)
                      .SetUpdate(true).SetLink(gameObject);

            int damage = Variance(_eatk);
            bool heavy = _rng.Next(100) < 15;
            if (heavy) damage = Mathf.RoundToInt(damage * 1.5f);
            damage = Mathf.Max(1, damage - _pdef);
            if (_guarding) damage = Mathf.Max(1, Mathf.RoundToInt(damage * 0.4f));

            _php = Mathf.Max(0, _php - damage);
            RefreshHp();
            SetLog(VNLocale.T(heavy ? "battle.logEnemyHeavy" : "battle.logEnemyHit",
                enemyName, damage) +
                (_guarding ? VNLocale.T("battle.logGuarded") : ""));
            PopText(_panel, "-" + damage, EnemyHpColor, heavy ? 44 : 34);

            // 我方受击：面板短震
            _panel.DOKill(true);
            _panel.DOShakeAnchorPos(0.25f, 12f, 25, 90f, false, true)
                  .SetUpdate(true).SetLink(gameObject);

            if (_php <= 0)
            {
                ShowBanner(VNLocale.T("battle.lose"), LoseColor);
                FinishBattle("失败");
                return;
            }
            DOVirtual.DelayedCall(0.7f, StartPlayerTurn, true).SetLink(gameObject);
        }

        void FinishBattle(string outcome)
        {
            _phase = Phase.Ending;
            SetButtonsInteractable(false);
            VNFlags.Set("战斗剩余HP", _php); // 车轮战/伤势分支素材
            DOVirtual.DelayedCall(1.3f, () => Done(outcome), true).SetLink(gameObject);
        }

        /// <summary>±30% 浮动（至少 1）</summary>
        int Variance(int baseValue)
        {
            float f = baseValue * (0.7f + (float)_rng.NextDouble() * 0.6f);
            return Mathf.Max(1, Mathf.RoundToInt(f));
        }

        // ------------------------------------------------------------------
        // 程序化 UI
        // ------------------------------------------------------------------

        void BuildUi(string titleText)
        {
            var root = (RectTransform)transform;

            var dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.6f));
            Stretch(dim);
            dim.GetComponent<Image>().raycastTarget = true; // 拦截点击穿透

            // ---------- 敌人区（上半） ----------
            var title = CreateText("Title", root, 40, new Color(1f, 0.88f, 0.62f, 1f), titleText);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.92f);
            titleRect.sizeDelta = new Vector2(1200f, 56f);

            _enemyBody = CreateImage("EnemyBody", root,
                VNProceduralTextures.RadialGlowSprite, EnemyBodyColor);
            _enemyBody.anchorMin = _enemyBody.anchorMax = new Vector2(0.5f, 0.62f);
            _enemyBody.sizeDelta = new Vector2(340f, 340f);
            _enemyImage = _enemyBody.GetComponent<Image>();

            // 一对眼睛让色块像个生物（柔圆贴图两点）
            for (int i = 0; i < 2; i++)
            {
                var eye = CreateImage($"Eye_{i}", _enemyBody, VNProceduralTextures.RadialGlowSprite,
                    new Color(1f, 0.9f, 0.4f, 0.95f));
                eye.anchorMin = eye.anchorMax = new Vector2(0.5f, 0.5f);
                eye.anchoredPosition = new Vector2(i == 0 ? -38f : 38f, 20f);
                eye.sizeDelta = new Vector2(46f, 58f);
            }
            // 待机呼吸
            _enemyBody.DOScale(1.06f, 1.6f).SetEase(Ease.InOutSine)
                      .SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(gameObject);

            // 敌人名 + HP 条
            var enemyLabel = CreateText("EnemyName", root, 30, Color.white, enemyName);
            var enemyLabelRect = (RectTransform)enemyLabel.transform;
            enemyLabelRect.anchorMin = enemyLabelRect.anchorMax = new Vector2(0.5f, 0.83f);
            enemyLabelRect.sizeDelta = new Vector2(600f, 42f);

            _enemyHpFill = CreateHpBar(root, new Vector2(0.5f, 0.785f), 460f,
                EnemyHpColor, out _enemyHpText);

            // ---------- 我方区（下条） ----------
            _panel = CreateImage("PlayerPanel", root,
                VNProceduralTextures.RoundedRectSprite, PanelColor);
            _panel.GetComponent<Image>().type = Image.Type.Sliced;
            _panel.GetComponent<Image>().raycastTarget = true;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.17f);
            _panel.sizeDelta = new Vector2(1100f, 250f);

            var playerLabel = CreateText("PlayerName", _panel, 28, Color.white, _playerLabel);
            var playerLabelRect = (RectTransform)playerLabel.transform;
            playerLabelRect.anchorMin = playerLabelRect.anchorMax = new Vector2(0.16f, 0.82f);
            playerLabelRect.sizeDelta = new Vector2(300f, 40f);

            _playerHpFill = CreateHpBar(_panel, new Vector2(0.62f, 0.82f), 480f,
                PlayerHpColor, out _playerHpText);

            _log = CreateText("Log", _panel, 26, new Color(0.85f, 0.9f, 1f, 0.95f), "");
            var logRect = (RectTransform)_log.transform;
            logRect.anchorMin = logRect.anchorMax = new Vector2(0.5f, 0.56f);
            logRect.sizeDelta = new Vector2(1020f, 40f);

            // 行动按钮排
            (string key, UnityEngine.Events.UnityAction action)[] actions =
                escapeChance > 0
                ? new (string, UnityEngine.Events.UnityAction)[]
                  {
                      ("battle.attack", OnAttack), ("battle.heavy", OnHeavy),
                      ("battle.guard", OnGuard), ("battle.escape", OnEscape),
                  }
                : new (string, UnityEngine.Events.UnityAction)[]
                  {
                      ("battle.attack", OnAttack), ("battle.heavy", OnHeavy),
                      ("battle.guard", OnGuard),
                  };
            _actionButtons = new Button[actions.Length];
            float buttonWidth = 230f, spacing = 20f;
            float totalWidth = actions.Length * buttonWidth + (actions.Length - 1) * spacing;
            for (int i = 0; i < actions.Length; i++)
            {
                var buttonRect = CreateImage($"Action_{i}", _panel,
                    VNProceduralTextures.RoundedRectSprite, new Color(0.13f, 0.17f, 0.28f, 1f));
                buttonRect.GetComponent<Image>().type = Image.Type.Sliced;
                buttonRect.GetComponent<Image>().raycastTarget = true;
                buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.22f);
                buttonRect.sizeDelta = new Vector2(buttonWidth, 62f);
                buttonRect.anchoredPosition = new Vector2(
                    -totalWidth * 0.5f + buttonWidth * 0.5f + i * (buttonWidth + spacing), 0f);

                var button = buttonRect.gameObject.AddComponent<Button>();
                var colors = button.colors;
                colors.highlightedColor = new Color(1.6f, 1.6f, 1.9f, 1f);
                colors.pressedColor = new Color(0.7f, 0.75f, 0.9f, 1f);
                colors.disabledColor = new Color(0.5f, 0.52f, 0.58f, 0.5f);
                button.colors = colors;
                button.onClick.AddListener(actions[i].action);
                _actionButtons[i] = button;

                var label = CreateText("Label", buttonRect, 26, Color.white,
                    VNLocale.T(actions[i].key));
                Stretch((RectTransform)label.transform);
            }
            SetButtonsInteractable(false);

            // 结果横幅（居中大字，平时隐藏）
            _banner = CreateText("Banner", root, 92, WinColor, "");
            _banner.fontStyle = FontStyles.Bold;
            var bannerRect = (RectTransform)_banner.transform;
            bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.sizeDelta = new Vector2(1400f, 130f);
            _banner.gameObject.SetActive(false);

            RefreshHp();

            // 面板与敌人弹入
            _panel.localScale = Vector3.one * 0.85f;
            _panel.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
            _enemyBody.localScale = Vector3.zero;
            _enemyBody.DOScale(1f, 0.45f).SetEase(Ease.OutBack).SetUpdate(true)
                      .SetLink(gameObject);
        }

        RectTransform CreateHpBar(RectTransform parent, Vector2 anchor, float width,
            Color color, out TextMeshProUGUI valueText)
        {
            var barBg = CreateImage("HpBarBg", parent, VNProceduralTextures.RoundedRectSprite,
                new Color(1f, 1f, 1f, 0.12f));
            barBg.GetComponent<Image>().type = Image.Type.Sliced;
            barBg.anchorMin = barBg.anchorMax = anchor;
            barBg.sizeDelta = new Vector2(width, 26f);

            var fill = CreateImage("HpFill", barBg, VNProceduralTextures.RoundedRectSprite, color);
            fill.GetComponent<Image>().type = Image.Type.Sliced;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;

            valueText = CreateText("HpValue", barBg, 22, Color.white, "");
            var textRect = (RectTransform)valueText.transform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(width, 30f);
            return fill;
        }

        void RefreshHp()
        {
            SetHpFill(_enemyHpFill, _enemyHpText, _ehp, _ehpMax);
            SetHpFill(_playerHpFill, _playerHpText, _php, _phpMax);
        }

        static void SetHpFill(RectTransform fill, TextMeshProUGUI text, int hp, int max)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(hp / (float)max), 1f);
            if (text != null) text.text = $"{hp} / {max}";
        }

        void SetButtonsInteractable(bool on)
        {
            if (_actionButtons == null) return;
            foreach (var button in _actionButtons)
                if (button != null) button.interactable = on;
        }

        void SetLog(string message)
        {
            if (_log != null) _log.text = message;
        }

        /// <summary>伤害/MISS 飘字：出生点上飘 + 淡出，结束自毁</summary>
        void PopText(RectTransform origin, string content, Color color, int size = 34)
        {
            var text = CreateText("Pop", (RectTransform)transform, size, color, content);
            text.fontStyle = FontStyles.Bold;
            var rect = (RectTransform)text.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(400f, 60f);
            // 换算 origin 中心到全屏坐标（两者同在事件层矩形内，直接用相对位置即可）
            rect.position = origin.position;
            rect.anchoredPosition += new Vector2(
                (float)_rng.NextDouble() * 60f - 30f, 40f);

            rect.DOAnchorPosY(rect.anchoredPosition.y + 90f, 0.8f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(text.gameObject);
            text.DOFade(0f, 0.8f).SetEase(Ease.InQuad).SetUpdate(true)
                .SetLink(text.gameObject)
                .OnComplete(() => Destroy(text.gameObject));
        }

        void ShowBanner(string content, Color color)
        {
            _banner.text = content;
            _banner.color = color;
            _banner.gameObject.SetActive(true);
            _banner.transform.localScale = Vector3.one * 1.6f;
            _banner.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack)
                   .SetUpdate(true).SetLink(gameObject);
        }

        void Update()
        {
            // 键盘快捷：1234 对应四个按钮（鼠标党可无视）
            if (_phase != Phase.PlayerTurn) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) OnAttack();
            else if (kb.digit2Key.wasPressedThisFrame) OnHeavy();
            else if (kb.digit3Key.wasPressedThisFrame) OnGuard();
            else if (kb.digit4Key.wasPressedThisFrame && escapeChance > 0) OnEscape();
        }

        static RectTransform CreateImage(string name, RectTransform parent,
            Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        static TextMeshProUGUI CreateText(string name, RectTransform parent,
            int size, Color color, string content)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
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
    }
}
