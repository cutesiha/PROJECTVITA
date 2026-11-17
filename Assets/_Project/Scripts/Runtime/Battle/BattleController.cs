using System;
using System.Collections.Generic;
using System.Linq;
using ProjectVita.Core;
using ProjectVita.Story;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Battle
{
    /// <summary>
    /// 전투 씬의 흐름과 화면 (문서 02의 BattlePresenter).
    /// BattleCore가 낸 이벤트를 순서대로 재생하고, 아군마다 스킬(숫자 키 1~4 또는 클릭)과 대상(클릭)을 고르게 한다.
    /// 끝나면 Battle_Encounter.csv의 승리/패배 노드로 돌아간다. 패배 처리: gameover(타이틀로), retry(다시), continue(패배 노드).
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

        [Header("명령")]
        [SerializeField] GameObject commandPanel;
        [SerializeField] TMP_Text actorText;
        [SerializeField] Button[] skillButtons = new Button[4];
        [SerializeField] TMP_Text skillInfo;
        [SerializeField] Button backButton;
        [SerializeField] Button autoButton;

        [Header("정보")]
        [SerializeField] TMP_Text roundText;
        [SerializeField] TMP_Text logText;

        [Header("결과")]
        [SerializeField] GameObject resultPanel;
        [SerializeField] TMP_Text resultText;
        [SerializeField] Button resultButton;

        [Tooltip("이 씬을 바로 플레이했을 때의 조우. 비우면 Battle_Encounter.csv의 첫 조우.")]
        [SerializeField] string fallbackEncounterId = "";

        BattleDatabase db;
        EncounterDef encounter;
        BattleCore core;
        readonly Dictionary<Unit, BattleUnitView> views = new Dictionary<Unit, BattleUnitView>();
        readonly Queue<string> log = new Queue<string>();
        bool auto;

        // UI 입력 결과 (폴링)
        SkillDef pickedSkill;
        Unit pickedTarget;
        bool backPressed, resultPressed;

        void Awake()
        {
            enemyTemplate.gameObject.SetActive(false);
            allyTemplate.gameObject.SetActive(false);
            commandPanel.SetActive(false);
            resultPanel.SetActive(false);
            flash.color = new Color(1, 1, 1, 0);
            backButton.onClick.AddListener(() => backPressed = true);
            resultButton.onClick.AddListener(() => resultPressed = true);
            autoButton.gameObject.SetActive(Debug.isDebugBuild);
            autoButton.onClick.AddListener(() =>
            {
                auto = !auto;
                autoButton.GetComponentInChildren<TMP_Text>().text = auto ? "자동 전투: 켬" : "자동 전투: 끔";
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
                Log($"<color=#FF6B6B>조우 '{id}'가 Battle_Encounter.csv에 없다</color>");
                Debug.LogError($"[전투] 조우 '{id}'가 Battle_Encounter.csv에 없다.");
                return;
            }

            if (!string.IsNullOrEmpty(encounter.background))
            {
                var sprite = AssetCatalog.Instance.FindBackground(encounter.background);
                if (sprite != null) background.sprite = sprite;
                background.color = sprite != null ? Color.white : background.color;
            }
            if (!string.IsNullOrEmpty(encounter.bgm)) AudioManager.Instance.PlayBgm(encounter.bgm, 1f);

            try
            {
                await RunBattle();
            }
            catch (OperationCanceledException)
            {
                // 씬이 바뀌었다.
            }
        }

        async Awaitable RunBattle()
        {
            foreach (var v in views.Values) Destroy(v.gameObject);
            views.Clear();
            log.Clear();
            resultPanel.SetActive(false);

            core = BattleCore.FromEncounter(db, encounter, BattleRules.Default(), Environment.TickCount, out var error);
            if (error != null) Debug.LogWarning($"[전투] {encounter.id}: {error}");
            foreach (var u in core.State.Enemies) CreateView(u, enemyTemplate, enemyRow);
            foreach (var u in core.State.Allies) CreateView(u, allyTemplate, allyRow);

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
                commandPanel.SetActive(false);
                core.RunRound(actions);
                await Play(core.TakeEvents());
            }
            await ShowResult();
        }

        void CreateView(Unit u, BattleUnitView template, RectTransform parent)
        {
            var v = Instantiate(template, parent);
            v.gameObject.SetActive(true);
            v.Bind(u);
            views[u] = v;
        }

        // ================================================================ 명령 고르기

        async Awaitable<BattleAction> ChooseAction(Unit ally)
        {
            if (auto) return null; // null이면 BattleCore가 아군 AI로 고른다
            var view = views[ally];
            view.SetActing(true);
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
            commandPanel.SetActive(true);
            backButton.gameObject.SetActive(false);
            actorText.text = $"{ally.Name}의 행동";
            skillInfo.text = "스킬을 고른다 (숫자 키 1~4)";
            pickedSkill = null;
            backPressed = false;

            for (int i = 0; i < skillButtons.Length; i++)
            {
                var button = skillButtons[i];
                button.onClick.RemoveAllListeners();
                if (i >= ally.Skills.Count)
                {
                    button.gameObject.SetActive(false);
                    continue;
                }
                var s = ally.Skills[i];
                button.gameObject.SetActive(true);
                button.interactable = core.CanUse(ally, s);
                string cost = string.IsNullOrEmpty(s.costKind) || s.costValue <= 0 ? "" : $"  <size=70%>{s.costKind.ToUpperInvariant()} {s.costValue}</size>";
                button.GetComponentInChildren<TMP_Text>().text = $"{i + 1}. {s.displayName}{cost}";
                button.onClick.AddListener(() => pickedSkill = s);
            }

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
            backButton.gameObject.SetActive(true);
            skillInfo.text = $"<b>{skill.displayName}</b>  {skill.description}\n대상을 클릭한다";
            foreach (var b in skillButtons) b.interactable = false;
            pickedTarget = null;
            backPressed = false;

            var valid = core.ValidTargets(ally, skill);
            foreach (var t in valid) views[t].SetSelectable(true, v => pickedTarget = v.Unit);

            while (pickedTarget == null && !backPressed && !auto)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.wasPressedThisFrame) backPressed = true;
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            }
            foreach (var v in views.Values) v.SetSelectable(false, null);
            return pickedTarget;
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
                    case BattleEventKind.RoundStarted:
                        roundText.text = $"ROUND {e.Amount}";
                        continue;
                    case BattleEventKind.TurnStarted:
                        continue;
                    case BattleEventKind.SkillUsed:
                        Log(e.Text);
                        actor?.Lunge(e.Actor.Side == Side.Ally ? 24f : -24f);
                        await Wait(0.3f);
                        break;
                    case BattleEventKind.Missed:
                        Log(e.Text);
                        target?.Popup("MISS", new Color(0.7f, 0.7f, 0.7f), 44f);
                        await Wait(0.3f);
                        break;
                    case BattleEventKind.Damaged:
                        if (e.Critical || e.TypeMultiplier > 1.01f) Log(e.Text);
                        if (target != null)
                        {
                            target.Popup(e.Amount.ToString(), e.Critical ? new Color(1f, 0.85f, 0.2f) : Color.white, e.Critical ? 72f : 56f);
                            target.Shake(e.Critical ? 26f : 14f, 0.3f);
                            Tween.Forget(target.AnimateHp(e.HpAfter, 0.35f));
                        }
                        if (e.Critical) Flash(new Color(1f, 1f, 1f, 0.6f));
                        await Wait(0.4f);
                        break;
                    case BattleEventKind.Healed:
                        if (target != null)
                        {
                            target.Popup("+" + e.Amount, new Color(0.4f, 1f, 0.5f));
                            Tween.Forget(target.AnimateHp(e.HpAfter, 0.35f));
                        }
                        await Wait(0.35f);
                        break;
                    case BattleEventKind.StatusApplied:
                    case BattleEventKind.StatusExpired:
                    case BattleEventKind.StatChanged:
                        Log(e.Text);
                        target?.Popup(e.Kind == BattleEventKind.StatusExpired ? "해제" : e.Kind == BattleEventKind.StatChanged ? (e.Amount >= 0 ? "UP" : "DOWN") : e.Status?.displayName ?? "", new Color(0.9f, 0.7f, 0.25f), 40f);
                        target?.RefreshInfo();
                        await Wait(0.25f);
                        break;
                    case BattleEventKind.GaugeChanged:
                        target?.RefreshInfo();
                        continue;
                    case BattleEventKind.ActionSkipped:
                    case BattleEventKind.NotEnoughCost:
                        Log(e.Text);
                        await Wait(0.3f);
                        break;
                    case BattleEventKind.UnitDown:
                        Log(e.Text);
                        target?.RefreshInfo();
                        target?.Shake(30f, 0.5f);
                        await Wait(0.5f);
                        break;
                    case BattleEventKind.BattleEnded:
                        Log(e.Text);
                        break;
                    default:
                        Log(e.Text);
                        break;
                }
            }
            foreach (var v in views.Values) v.RefreshInfo();
        }

        async Awaitable ShowResult()
        {
            commandPanel.SetActive(false);
            bool win = core.Winner == Side.Ally;
            var rule = encounter.loseRule;
            resultText.text = win ? "승리" : rule == LoseRule.GameOver ? "게임 오버" : "패배";
            resultButton.GetComponentInChildren<TMP_Text>().text = win ? "계속" : rule switch
            {
                LoseRule.GameOver => "타이틀로",
                LoseRule.Retry => "다시 도전",
                _ => "계속",
            };
            resultPanel.SetActive(true);
            resultPressed = false;
            while (!resultPressed) await Awaitable.NextFrameAsync(destroyCancellationToken);
            resultPanel.SetActive(false);

            if (win) EpisodeFlow.EndBattle(encounter.winNode);
            else if (rule == LoseRule.GameOver) EpisodeFlow.GoTitle();
            else if (rule == LoseRule.Retry) await RunBattle();
            else EpisodeFlow.EndBattle(encounter.loseNode);
        }

        // ================================================================ 공통

        void Log(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            log.Enqueue(line);
            while (log.Count > 5) log.Dequeue();
            logText.text = string.Join("\n", log);
        }

        void Flash(Color c)
        {
            Tween.Forget(Tween.Run(0.25f, t => flash.color = new Color(c.r, c.g, c.b, c.a * (1f - t)), destroyCancellationToken));
        }

        Awaitable Wait(float seconds) => Awaitable.WaitForSecondsAsync(seconds, destroyCancellationToken);
    }
}
