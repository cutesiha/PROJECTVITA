using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ProjectVita.Core;
using ProjectVita.Story;
using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Battle
{
    /// <summary>
    /// 전투 씬의 흐름과 화면 (문서 02의 BattlePresenter). 화면은 임시 UI 시안 battle.html의 디자인을 따른다.
    /// BattleCore가 낸 이벤트를 순서대로 재생하고, 아군마다 스킬(숫자 키 1~4 또는 클릭)과 대상(클릭)을 고르게 한다.
    /// 연출: 시작 배너(ENGAGE)와 카드 등장, 라운드 배너, 행동 순서 줄, 명령 잠금, 돌진, 피격(번쩍임, 붉은 기, 밀려남, 화면 흔들림, 경직),
    /// 아군 피격 시 화면 가장자리 붉게, 치명타 플래시와 글리치, 약점/내성 표시, 쓰러짐, 결과 창.
    /// 끝나면 Battle_Encounter.csv의 승리/패배 노드로 돌아간다. 패배 처리: gameover(타이틀로), retry(다시), continue(패배 노드).
    /// 전투 규칙(비디님 기획)이 오면 BattleCore와 이 화면의 정보 줄을 바꾼다. 지금 화면은 UI 틀이다.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField] TextAsset episodesCsv;
        [SerializeField] Image background;
        [SerializeField] RectTransform shakeRoot;
        [SerializeField] RectTransform enemyRow;
        [SerializeField] RectTransform allyRow;
        [SerializeField] BattleUnitView enemyTemplate;
        [SerializeField] BattleUnitView allyTemplate;
        [SerializeField] Image flash;
        [SerializeField] Image hitVignette;

        [Header("위쪽")]
        [SerializeField] TMP_Text roundTag;
        [SerializeField] TMP_Text encounterTag;
        [SerializeField] RectTransform orderRoot;
        [SerializeField] BattleOrderChip orderChipTemplate;

        [Header("배너")]
        [SerializeField] CanvasGroup banner;
        [SerializeField] TMP_Text bannerTitle;
        [SerializeField] TMP_Text bannerSub;

        [Header("명령")]
        [SerializeField] RectTransform commandZone;
        [SerializeField] CanvasGroup commandLock;
        [SerializeField] TMP_Text commandHead;
        [SerializeField] BattleSkillButton[] skillButtons = new BattleSkillButton[4];
        [SerializeField] Button statusButton;
        [SerializeField] Button logButton;
        [SerializeField] Button backButton;
        [SerializeField] Button autoButton;
        [SerializeField] TMP_Text logTag;
        [SerializeField] TMP_Text logLine;

        [Header("창")]
        [SerializeField] BattleStatusPanel statusPanel;
        [SerializeField] BacklogPanel battleLogPanel;

        [Header("결과")]
        [SerializeField] UIPanelAnim resultPanel;
        [SerializeField] TMP_Text resultEyebrow;
        [SerializeField] TMP_Text resultTitle;
        [SerializeField] TMP_Text resultSub;
        [SerializeField] TMP_Text[] resultStats = new TMP_Text[3];
        [SerializeField] Button resultButton;

        [Tooltip("이 씬을 바로 플레이했을 때의 조우. 비우면 Battle_Encounter.csv의 첫 조우.")]
        [SerializeField] string fallbackEncounterId = "";

        BattleDatabase db;
        EncounterDef encounter;
        BattleCore core;
        readonly Dictionary<Unit, BattleUnitView> views = new Dictionary<Unit, BattleUnitView>();
        readonly List<BattleOrderChip> chips = new List<BattleOrderChip>();
        bool auto;
        int damageDealt;
        Vector2 commandBase;
        CancellationTokenSource shakeCts, lockCts, vignetteCts;

        // UI 입력 결과 (폴링)
        SkillDef pickedSkill;
        Unit pickedTarget;
        Unit currentActor;
        bool backPressed, resultPressed;

        void Awake()
        {
            enemyTemplate.gameObject.SetActive(false);
            allyTemplate.gameObject.SetActive(false);
            orderChipTemplate.gameObject.SetActive(false);
            resultPanel.HideInstant();
            flash.color = new Color(1, 1, 1, 0);
            hitVignette.color = UITheme.Danger.WithAlpha(0f);
            banner.alpha = 0f;
            commandBase = commandZone.anchoredPosition;
            backButton.onClick.AddListener(() => backPressed = true);
            resultButton.onClick.AddListener(() => resultPressed = true);
            statusButton.onClick.AddListener(() => Inspect(currentActor ?? core?.State.Allies.FirstOrDefault()));
            logButton.onClick.AddListener(() => battleLogPanel.Open());
            autoButton.gameObject.SetActive(Debug.isDebugBuild);
            autoButton.onClick.AddListener(() =>
            {
                auto = !auto;
                autoButton.GetComponentInChildren<TMP_Text>().text = auto ? "자동 ON" : "자동 OFF";
                if (auto) backPressed = true; // 고르는 중이면 빠져나와 AI에게 맡긴다
            });
        }

        async void Start()
        {
            EpisodeFlow.EnsureTable(episodesCsv);
            db = BattleDatabase.Instance;
            string id = EpisodeFlow.TakePendingEncounter();
            if (string.IsNullOrEmpty(id)) id = string.IsNullOrEmpty(fallbackEncounterId) ? db.encounters.FirstOrDefault()?.id : fallbackEncounterId;
            encounter = db.Encounter(id);
            if (encounter == null)
            {
                SetLog("ERROR", $"조우 '{id}'가 Battle_Encounter.csv에 없다");
                Debug.LogError($"[전투] 조우 '{id}'가 Battle_Encounter.csv에 없다.");
                return;
            }

            if (!string.IsNullOrEmpty(encounter.background))
            {
                var sprite = AssetCatalog.Instance.FindBackground(encounter.background);
                if (sprite != null) background.sprite = sprite;
                background.color = sprite != null ? new Color(0.62f, 0.64f, 0.7f) : background.color; // 카드가 잘 보이게 배경을 조금 어둡게
            }
            if (!string.IsNullOrEmpty(encounter.bgm)) AudioManager.Instance.PlayBgm(encounter.bgm, 1f);
            encounterTag.text = encounter.id.ToUpperInvariant();

            try
            {
                await RunBattle();
            }
            catch (OperationCanceledException)
            {
                // 씬이 바뀌었다.
            }
        }

        void Update()
        {
            // I: 지금 행동하는 아군(없으면 첫 아군)의 능력치
            var kb = Keyboard.current;
            if (kb != null && kb.iKey.wasPressedThisFrame && !GamePause.IsPaused && core != null)
                Inspect(currentActor ?? core.State.Allies.FirstOrDefault());
        }

        async Awaitable RunBattle()
        {
            foreach (var v in views.Values) Destroy(v.gameObject);
            views.Clear();
            BattleLog.Clear();
            damageDealt = 0;
            currentActor = null;

            core = BattleCore.FromEncounter(db, encounter, BattleRules.Default(), Environment.TickCount, out var error);
            if (error != null) Debug.LogWarning($"[전투] {encounter.id}: {error}");
            int i = 0;
            foreach (var u in core.State.Enemies) CreateView(u, enemyTemplate, enemyRow).Intro(new Vector2(0f, 90f), 0.35f + 0.1f * i++);
            i = 0;
            foreach (var u in core.State.Allies) CreateView(u, allyTemplate, allyRow).Intro(new Vector2(0f, -90f), 0.55f + 0.1f * i++);
            SetLock(true, "ENGAGING");
            SlideCommand();
            RefreshOrder(null);

            SetLog("ENGAGE", "적이 나타났다. 행동을 고른다.");
            await Banner("ENGAGE", $"ENCOUNTER  //  {encounter.id.ToUpperInvariant()}", 1.1f);

            core.Start();
            await Play(core.TakeEvents());

            while (!core.IsOver)
            {
                var actions = new List<BattleAction>();
                foreach (var ally in core.AlliesNeedingCommand)
                {
                    var a = await ChooseAction(ally);
                    if (a != null) actions.Add(a);
                }
                currentActor = null;
                SetLock(true, "COMMAND LOCK  —  행동 진행 중");
                core.RunRound(actions);
                await Play(core.TakeEvents());
            }
            await ShowResult();
        }

        BattleUnitView CreateView(Unit u, BattleUnitView template, RectTransform parent)
        {
            var v = Instantiate(template, parent);
            v.gameObject.SetActive(true);
            v.Bind(u, view => Inspect(view.Unit));
            views[u] = v;
            return v;
        }

        void Inspect(Unit u)
        {
            if (u == null || GamePause.IsPaused) return;
            statusPanel.Open(u, db);
        }

        // ================================================================ 명령 고르기

        async Awaitable<BattleAction> ChooseAction(Unit ally)
        {
            if (auto) return null; // null이면 BattleCore가 아군 AI로 고른다
            var view = views[ally];
            currentActor = ally;
            view.SetActing(true);
            RefreshOrder(ally);
            try
            {
                while (true)
                {
                    var skill = await PickSkill(ally);
                    if (skill == null) return null; // 자동 전투로 바뀜
                    if (!core.NeedsTargetChoice(skill)) return new BattleAction { Actor = ally, Skill = skill };

                    var target = await PickTarget(ally, skill);
                    if (target != null) return new BattleAction { Actor = ally, Skill = skill, Targets = { target } };
                    if (auto) return null;
                }
            }
            finally
            {
                if (view != null) view.SetActing(false);
            }
        }

        async Awaitable<SkillDef> PickSkill(Unit ally)
        {
            SetLock(false, "");
            backButton.interactable = false;
            commandHead.text = CommandHead(ally);
            SetLog("COMMAND", $"<b>{ally.Name}</b>의 행동을 고른다. (숫자 키 1~4, I: 능력치)");
            pickedSkill = null;
            backPressed = false;

            for (int i = 0; i < skillButtons.Length; i++)
            {
                var sb = skillButtons[i];
                sb.Button.onClick.RemoveAllListeners();
                if (i >= ally.Skills.Count)
                {
                    sb.gameObject.SetActive(false);
                    continue;
                }
                var s = ally.Skills[i];
                sb.gameObject.SetActive(true);
                sb.Set(s, i, core.CanUse(ally, s));
                sb.Button.onClick.AddListener(() => pickedSkill = s);
            }
            UIPanelAnim.Stagger((RectTransform)skillButtons[0].transform.parent, destroyCancellationToken, 0f, 0.05f, 0.25f);

            while (pickedSkill == null)
            {
                if (auto) return null;
                var kb = Keyboard.current;
                if (kb != null && !GamePause.IsPaused)
                {
                    for (int i = 0; i < ally.Skills.Count && i < 4; i++)
                        if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame && core.CanUse(ally, ally.Skills[i])) pickedSkill = ally.Skills[i];
                }
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            }
            return pickedSkill;
        }

        async Awaitable<Unit> PickTarget(Unit ally, SkillDef skill)
        {
            backButton.interactable = true;
            SetLog("TARGET", $"<b>{skill.displayName}</b>  {skill.description}  —  대상을 누른다 (오른쪽 클릭: 취소)");
            foreach (var b in skillButtons) b.Button.interactable = false;
            pickedTarget = null;
            backPressed = false;

            var valid = core.ValidTargets(ally, skill);
            foreach (var t in valid) views[t].SetSelectable(true, v => pickedTarget = v.Unit);

            while (pickedTarget == null && !backPressed && !auto)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.wasPressedThisFrame && !GamePause.IsPaused) backPressed = true;
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            }
            foreach (var v in views.Values) v.SetSelectable(false, null);
            return pickedTarget;
        }

        string CommandHead(Unit ally)
        {
            string gauges = string.Join("  //  ", ally.Gauges.Select(g => $"{g.Key.ToUpperInvariant()} {g.Value} / {(ally.GaugeMax.TryGetValue(g.Key, out int m) ? m : g.Value)}"));
            string dim = UITheme.ToHex(UITheme.TextDim);
            return $"<color={UITheme.ToHex(UITheme.Cyan)}>COMMAND</color>  <color={dim}>//</color>  {ally.Name}  <color={dim}>//</color>  {ally.Def.id.ToUpperInvariant()}" +
                   (gauges.Length > 0 ? $"  <color={dim}>//</color>  {gauges}" : "");
        }

        // ================================================================ 이벤트 재생

        async Awaitable Play(List<BattleEvent> events)
        {
            foreach (var e in events)
            {
                BattleUnitView target = e.Target != null && views.TryGetValue(e.Target, out var tv) ? tv : null;
                BattleUnitView actor = e.Actor != null && views.TryGetValue(e.Actor, out var av) ? av : null;

                switch (e.Kind)
                {
                    case BattleEventKind.BattleStarted:
                        continue;
                    case BattleEventKind.RoundStarted:
                        roundTag.text = $"ROUND {e.Amount:00}";
                        if (e.Amount > 1)
                        {
                            RefreshOrder(null);
                            await Banner($"ROUND {e.Amount:00}", "COMMAND PHASE", 0.75f);
                        }
                        continue;
                    case BattleEventKind.TurnStarted:
                        foreach (var v in views.Values) v.SetActing(false);
                        actor?.SetActing(true);
                        RefreshOrder(e.Actor);
                        continue;
                    case BattleEventKind.SkillUsed:
                        SetLog("SKILL", e.Text, e.Actor);
                        actor?.Lunge();
                        await Wait(0.32f);
                        break;
                    case BattleEventKind.Missed:
                        SetLog("MISS", e.Text, e.Actor);
                        target?.Popup("MISS", UITheme.TextDim, BattleUnitView.PopupKind.Miss);
                        await Wait(0.4f);
                        break;
                    case BattleEventKind.Damaged:
                        await OnDamaged(e, target);
                        break;
                    case BattleEventKind.Healed:
                        SetLog("HEAL", e.Text, e.Target);
                        if (target != null)
                        {
                            target.HealFx();
                            target.Popup("+" + e.Amount, new Color(0.66f, 1f, 0.85f), BattleUnitView.PopupKind.Heal);
                            Tween.Forget(target.AnimateHp(e.HpAfter, 0.4f));
                        }
                        await Wait(0.4f);
                        break;
                    case BattleEventKind.StatusApplied:
                    case BattleEventKind.StatusExpired:
                    case BattleEventKind.StatChanged:
                    {
                        bool up = e.Kind == BattleEventKind.StatChanged && e.Amount >= 0;
                        bool down = e.Kind == BattleEventKind.StatChanged && e.Amount < 0;
                        string label = e.Kind == BattleEventKind.StatusExpired ? "CLEAR"
                            : e.Kind == BattleEventKind.StatChanged ? (up ? "UP" : "DOWN") : (e.Status?.displayName ?? "");
                        var color = up ? UITheme.Cyan : down ? UITheme.Danger : UITheme.Amber;
                        SetLog(e.Kind == BattleEventKind.StatChanged ? (up ? "BUFF" : "DEBUFF") : "STATUS", e.Text, e.Target);
                        target?.Popup(label, color, BattleUnitView.PopupKind.Label, 40f);
                        target?.RefreshInfo();
                        await Wait(0.3f);
                        break;
                    }
                    case BattleEventKind.GaugeChanged:
                        target?.RefreshInfo();
                        continue;
                    case BattleEventKind.ActionSkipped:
                    case BattleEventKind.NotEnoughCost:
                        SetLog("SKIP", e.Text, e.Actor ?? e.Target);
                        await Wait(0.35f);
                        break;
                    case BattleEventKind.UnitDown:
                        SetLog("DOWN", e.Text, e.Target);
                        target?.RefreshInfo();
                        target?.Down();
                        Shake(1.2f, 0.45f);
                        await Wait(0.75f);
                        break;
                    case BattleEventKind.BattleEnded:
                        SetLog("END", e.Text);
                        break;
                    default:
                        SetLog("INFO", e.Text);
                        break;
                }
            }
            foreach (var v in views.Values)
            {
                v.SetActing(false);
                v.RefreshInfo();
            }
        }

        /// <summary>
        /// 피격 연출. 세게 맞으면(치명타, 약점, 최대 HP의 25% 이상) 화면이 흔들리고 잠깐 멈춘다(경직).
        /// 아군이 맞으면 화면 가장자리가 붉게 번쩍인다. 치명타는 흰 플래시와 글리치.
        /// </summary>
        async Awaitable OnDamaged(BattleEvent e, BattleUnitView target)
        {
            if (e.Actor != null && e.Actor.Side == Side.Ally) damageDealt += e.Amount;
            bool weak = e.TypeMultiplier > 1.01f, resisted = e.TypeMultiplier < 0.99f;
            bool big = e.Critical || weak || (e.Target != null && e.Amount >= e.Target.MaxHp * 0.25f);
            SetLog(e.Critical ? "CRITICAL" : weak ? "WEAK" : "HIT", e.Text, e.Actor);

            if (target != null)
            {
                bool allyHit = e.Target.Side == Side.Ally;
                target.Hit(e.Critical || weak);
                var color = allyHit ? new Color(1f, 0.82f, 0.6f) : Color.white;
                target.Popup(e.Amount.ToString(), e.Critical ? UITheme.Amber : color, e.Critical ? BattleUnitView.PopupKind.Critical : BattleUnitView.PopupKind.Damage);
                if (e.Critical) target.Popup("CRITICAL", UITheme.Amber, BattleUnitView.PopupKind.Label, 70f);
                else if (weak) target.Popup("WEAK POINT", UITheme.Cyan, BattleUnitView.PopupKind.Label, 70f);
                else if (resisted) target.Popup("RESISTED", UITheme.TextDim, BattleUnitView.PopupKind.Label, 70f);
                Tween.Forget(target.AnimateHp(e.HpAfter, 0.35f));
                if (allyHit) PulseVignette(big ? 0.75f : 0.45f);
            }

            if (big)
            {
                Shake(e.Critical ? 1.6f : 1f, e.Critical ? 0.4f : 0.3f);
                if (e.Critical) Flash(new Color(1f, 1f, 1f, 0.55f));
                // 경직: 맞는 순간 잠깐 멈춘 듯이
                await Awaitable.WaitForSecondsAsync(0.08f, destroyCancellationToken);
            }
            await Wait(0.45f);
        }

        // ================================================================ 결과

        async Awaitable ShowResult()
        {
            SetLock(true, "COMBAT TERMINATED");
            currentActor = null;
            bool win = core.Winner == Side.Ally;
            var rule = encounter.loseRule;
            await Wait(0.6f);

            resultEyebrow.text = "COMBAT TERMINATED";
            resultTitle.text = win ? "VICTORY" : "DEFEAT";
            resultTitle.color = win ? UITheme.Cyan : UITheme.Danger;
            resultSub.text = win ? "적을 모두 쓰러뜨렸다." : rule == LoseRule.GameOver ? "아군이 모두 쓰러졌다. 게임 오버." : "아군이 모두 쓰러졌다.";
            string dim = UITheme.ToHex(UITheme.TextDim);
            resultStats[0].text = $"{core.State.Round}\n<size=40%><color={dim}>ROUNDS</color></size>";
            resultStats[1].text = $"{damageDealt}\n<size=40%><color={dim}>DAMAGE</color></size>";
            resultStats[2].text = $"{core.State.Allies.Sum(a => Mathf.Max(0, a.Hp))}\n<size=40%><color={dim}>HP LEFT</color></size>";
            resultButton.GetComponentInChildren<TMP_Text>().text = win ? "계속" : rule switch
            {
                LoseRule.GameOver => "타이틀로",
                LoseRule.Retry => "다시 도전",
                _ => "계속",
            };
            if (win) Flash(new Color(0.3f, 0.72f, 1f, 0.25f));
            resultPanel.Show();
            resultPressed = false;
            while (!resultPressed) await Awaitable.NextFrameAsync(destroyCancellationToken);
            resultPanel.Hide();

            if (win) EpisodeFlow.EndBattle(encounter.winNode);
            else if (rule == LoseRule.GameOver) EpisodeFlow.GoTitle();
            else if (rule == LoseRule.Retry) await RunBattle();
            else EpisodeFlow.EndBattle(encounter.loseNode);
        }

        // ================================================================ 화면 부품

        /// <summary>행동 순서 줄: 살아 있는 유닛을 속도순으로. now는 지금 행동(고르는) 유닛.</summary>
        void RefreshOrder(Unit now)
        {
            if (core == null) return;
            var order = core.State.All.Where(u => !u.IsDown).OrderByDescending(u => u.Stat(u.Rules.SpeedStat)).ToList();
            while (chips.Count < order.Count)
            {
                var c = Instantiate(orderChipTemplate, orderRoot);
                chips.Add(c);
            }
            for (int i = 0; i < chips.Count; i++)
            {
                bool used = i < order.Count;
                chips[i].gameObject.SetActive(used);
                if (used) chips[i].Set(order[i].Name, order[i].Side == Side.Ally, order[i] == now, i == 0);
            }
        }

        /// <summary>가운데 배너: 큰 글자가 자간이 좁혀지며 나타났다가 사라진다.</summary>
        async Awaitable Banner(string title, string sub, float hold)
        {
            bannerTitle.text = title;
            bannerSub.text = sub;
            await UIAnim.Run(0.35f, t =>
            {
                banner.alpha = UIAnim.EaseOut(t);
                bannerTitle.characterSpacing = Mathf.Lerp(70f, 30f, UIAnim.EaseOut(t));
            }, destroyCancellationToken);
            await Awaitable.WaitForSecondsAsync(hold, destroyCancellationToken);
            await UIAnim.Run(0.3f, t => banner.alpha = 1f - t, destroyCancellationToken);
        }

        /// <summary>명령 창이 아래에서 올라온다 (전투 시작).</summary>
        void SlideCommand()
        {
            Tween.Forget(UIAnim.Run(0.55f, t =>
            {
                commandZone.anchoredPosition = commandBase + new Vector2(0f, -320f * (1f - UIAnim.EaseOut(t)));
            }, destroyCancellationToken, 0.2f));
        }

        /// <summary>명령 잠금: 적과 아군이 행동하는 동안 명령 창을 덮고 깜빡이는 문구를 보인다.</summary>
        void SetLock(bool locked, string text)
        {
            var label = commandLock.GetComponentInChildren<TMP_Text>(true);
            if (locked && label != null) label.text = text;
            commandLock.blocksRaycasts = locked;
            var token = UIAnim.Restart(ref lockCts, destroyCancellationToken);
            float from = commandLock.alpha;
            Tween.Forget(UIAnim.Run(0.2f, t => commandLock.alpha = Mathf.Lerp(from, locked ? 1f : 0f, t), token));
        }

        void SetLog(string tag, string text, Unit who = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            logTag.text = tag;
            logTag.color = tag == "CRITICAL" || tag == "DOWN" || tag == "DEBUFF" ? UITheme.Danger : tag == "WEAK" || tag == "BUFF" ? UITheme.Cyan : UITheme.Amber;
            logLine.text = text;
            if (tag == "COMMAND" || tag == "TARGET") return; // 안내 문구는 기록에 남기지 않는다
            var color = who == null ? UITheme.Periwinkle : who.Side == Side.Ally ? UITheme.Cyan : UITheme.Amber;
            BattleLog.Add(who?.Name, $"<color={UITheme.ToHex(UITheme.TextDim)}>[{tag}]</color>  {text}", color, who == null ? "" : who.Side == Side.Ally ? "ALLY" : "ENEMY");
        }

        /// <summary>화면 흔들림: 계단식으로 튀는 글리치 느낌. 세기 1 = 약 12px. 설정에서 끌 수 있다.</summary>
        void Shake(float strength, float time)
        {
            if (!GameSettings.ScreenShake) return;
            var token = UIAnim.Restart(ref shakeCts, destroyCancellationToken);
            float amp = 12f * strength;
            Tween.Forget(UIAnim.Run(time, t =>
            {
                if (t >= 1f)
                {
                    shakeRoot.anchoredPosition = Vector2.zero;
                    shakeRoot.localScale = Vector3.one;
                    return;
                }
                // 1/30초마다만 위치를 바꿔서 덜컥거리게
                float step = Mathf.Floor(t * time * 30f);
                var rnd = new System.Random((int)step * 7919 + 17);
                float decay = 1f - t;
                shakeRoot.anchoredPosition = new Vector2((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1) * 0.6f) * amp * decay;
                shakeRoot.localScale = Vector3.one * (1f + 0.018f * strength * decay); // 살짝 다가오는 느낌
            }, token));
        }

        void PulseVignette(float strength)
        {
            var token = UIAnim.Restart(ref vignetteCts, destroyCancellationToken);
            Tween.Forget(UIAnim.Run(0.6f, t => hitVignette.color = UITheme.Danger.WithAlpha(strength * (1f - UIAnim.EaseOut(t))), token));
        }

        void Flash(Color c)
        {
            Tween.Forget(UIAnim.Run(0.3f, t => flash.color = new Color(c.r, c.g, c.b, c.a * (1f - t)), destroyCancellationToken));
        }

        Awaitable Wait(float seconds) => Awaitable.WaitForSecondsAsync(seconds, destroyCancellationToken);
    }
}
