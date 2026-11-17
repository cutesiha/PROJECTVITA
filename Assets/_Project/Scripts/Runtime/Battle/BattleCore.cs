using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectVita.Battle
{
    // ================================================================ 상태

    public sealed class StatusInstance
    {
        public StatusDef Def;
        public int TurnsLeft;
        public int Stacks = 1;
    }

    public sealed class Unit
    {
        public readonly UnitDef Def;
        public readonly Side Side;
        public readonly int Slot;
        public readonly int Uid;
        public readonly BattleRules Rules;

        public readonly Dictionary<string, int> Stages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Gauges = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> GaugeMax = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly List<StatusInstance> Statuses = new List<StatusInstance>();
        public readonly List<SkillDef> Skills = new List<SkillDef>();

        public int MaxHp;
        public int Hp;
        public bool SkipThisTurn;
        public bool IsDown => Hp <= 0;
        public string Name => Def.displayName;
        public float HpRatio => MaxHp <= 0 ? 0f : (float)Hp / MaxHp;

        public Unit(UnitDef def, Side side, int slot, int uid, BattleRules rules, BattleDatabase data)
        {
            Def = def;
            Side = side;
            Slot = slot;
            Uid = uid;
            Rules = rules;
            MaxHp = Math.Max(1, def.Stat(rules.HpStat, 1));
            Hp = MaxHp;
            foreach (var id in def.skills)
            {
                var s = data.Skill(id);
                if (s != null) Skills.Add(s);
            }
            // 게이지 = 스킬 비용 종류. 처음 값과 최대값은 같은 이름의 스탯 (예: sp 스탯 → sp 게이지)
            foreach (var s in Skills)
            {
                if (string.IsNullOrEmpty(s.costKind) || Gauges.ContainsKey(s.costKind) || s.costKind == rules.HpStat) continue;
                int v = def.Stat(s.costKind, 0);
                Gauges[s.costKind] = v;
                GaugeMax[s.costKind] = v;
            }
        }

        /// <summary>현재 스탯. 단계(ModifyStat)를 포켓몬식 배율로 반영한다: +1 = x1.5, -1 = x0.67</summary>
        public int Stat(string id)
        {
            int baseValue = Def.Stat(id, 0);
            Stages.TryGetValue(id, out int stage);
            float mul = stage >= 0 ? (2f + stage) / 2f : 2f / (2f - stage);
            return Math.Max(1, (int)(baseValue * mul));
        }

        public bool HasStatus(string id) => Statuses.Any(s => string.Equals(s.Def.id, id, StringComparison.OrdinalIgnoreCase));

        public override string ToString() => $"{Name}({Side} {Slot})";
    }

    public sealed class BattleState
    {
        public readonly List<Unit> Allies = new List<Unit>();
        public readonly List<Unit> Enemies = new List<Unit>();
        public int Round;

        public IEnumerable<Unit> All => Allies.Concat(Enemies);
        public List<Unit> Team(Side side) => side == Side.Ally ? Allies : Enemies;
        public List<Unit> Living(Side side) => Team(side).Where(u => !u.IsDown).ToList();
        public List<Unit> Opponents(Unit u) => Living(u.Side == Side.Ally ? Side.Enemy : Side.Ally);
        public List<Unit> Friends(Unit u) => Living(u.Side);
    }

    public sealed class BattleAction
    {
        public Unit Actor;
        public SkillDef Skill;
        public List<Unit> Targets = new List<Unit>();
    }

    // ================================================================ 이벤트 (화면이 재생한다)

    public enum BattleEventKind
    {
        BattleStarted, RoundStarted, TurnStarted, SkillUsed, Missed, Damaged, Healed,
        StatusApplied, StatusExpired, StatChanged, GaugeChanged, ActionSkipped, NotEnoughCost, UnitDown, BattleEnded,
    }

    public sealed class BattleEvent
    {
        public BattleEventKind Kind;
        public Unit Actor;
        public Unit Target;
        public SkillDef Skill;
        public StatusDef Status;
        public int Amount;
        public int HpAfter;
        public float TypeMultiplier = 1f;
        public bool Critical;
        public Side Winner;
        public string Text;

        public override string ToString() => Text ?? Kind.ToString();
    }

    // ================================================================ 교체 가능한 규칙

    /// <summary>행동 순서. 기본값: 속도순 (포켓몬식)</summary>
    public interface ITurnOrderRule
    {
        List<BattleAction> Order(BattleState state, List<BattleAction> actions, Random rng);
    }

    /// <summary>데미지 공식. 기본값: 포켓몬식</summary>
    public interface IDamageFormula
    {
        int Compute(Unit attacker, Unit defender, SkillDef skill, int power, BattleDatabase data, Random rng, out bool critical, out float typeMultiplier);
    }

    /// <summary>승패 판정. 기본값: 한쪽 전멸. null이면 계속</summary>
    public interface IWinCondition
    {
        Side? Winner(BattleState state);
    }

    /// <summary>행동 해결. 기본값: 순서대로 하나씩. (림버스식 합 같은 규칙이 오면 이 자리를 교체)</summary>
    public interface IActionResolver
    {
        void Resolve(BattleCore core, List<BattleAction> orderedActions);
    }

    /// <summary>행동 고르기 (적 AI, 자동 전투, 시뮬레이터의 아군)</summary>
    public interface IUnitAI
    {
        BattleAction Choose(BattleCore core, Unit unit, Random rng);
    }

    public sealed class BattleRules
    {
        // TODO(규칙 대기): 스탯 이름, 출전 인원, 턴 제한은 비디님의 양식이 오면 맞춘다.
        public string HpStat = "hp";
        public string AttackStat = "atk";
        public string DefenseStat = "def";
        public string SpeedStat = "spd";
        public int PartySize = 3;
        public int MaxRounds = 99;

        public ITurnOrderRule TurnOrder = new SpeedOrder();
        public IDamageFormula Damage = new PokemonLikeDamage();
        public IWinCondition Win = new WipeOut();
        public IActionResolver Resolver = new SequentialResolver();
        public IUnitAI EnemyAI = new WeightedRandomAI();
        public IUnitAI AllyAI = new WeightedRandomAI();

        public static BattleRules Default() => new BattleRules();
    }

    // ================================================================ 전투 본체

    /// <summary>
    /// 규칙에 묶이지 않는 턴제 전투 (문서 02). MonoBehaviour가 아닌 순수 C#이고, 결과를 BattleEvent 목록으로만 내보낸다.
    /// 쓰는 법: Start() → (아군 행동을 모아서) RunRound(actions) → TakeEvents()를 화면이 재생 → IsOver까지 반복.
    /// 난수는 System.Random(seed) 하나만 쓴다 (시뮬레이터, 버그 재현용).
    /// </summary>
    public sealed class BattleCore
    {
        public readonly BattleState State = new BattleState();
        public readonly BattleRules Rules;
        public readonly BattleDatabase Data;
        public readonly Random Rng;

        public bool IsOver { get; private set; }
        public Side Winner { get; private set; }

        readonly List<BattleEvent> events = new List<BattleEvent>();
        int nextUid;

        public BattleCore(BattleDatabase data, IEnumerable<UnitDef> allies, IEnumerable<UnitDef> enemies, BattleRules rules, int seed)
        {
            Data = data;
            Rules = rules ?? BattleRules.Default();
            Rng = new Random(seed);
            int i = 0;
            foreach (var d in allies) State.Allies.Add(new Unit(d, Side.Ally, i++, nextUid++, Rules, data));
            i = 0;
            foreach (var d in enemies) State.Enemies.Add(new Unit(d, Side.Enemy, i++, nextUid++, Rules, data));
        }

        /// <summary>조우 데이터로 만든다. 아군이 비어 있으면 Side=Ally 유닛 앞에서부터 PartySize명.</summary>
        public static BattleCore FromEncounter(BattleDatabase data, EncounterDef enc, BattleRules rules, int seed, out string error)
        {
            error = null;
            rules ??= BattleRules.Default();
            var allyIds = enc.allies.Count > 0 ? enc.allies : data.units.Where(u => u.side == Side.Ally).Take(rules.PartySize).Select(u => u.id).ToList();
            var allies = new List<UnitDef>();
            var enemies = new List<UnitDef>();
            foreach (var id in allyIds)
            {
                var u = data.Unit(id);
                if (u == null) error = $"유닛 '{id}'가 Battle_Unit.csv에 없다";
                else allies.Add(u);
            }
            foreach (var id in enc.enemies)
            {
                var u = data.Unit(id);
                if (u == null) error = $"유닛 '{id}'가 Battle_Unit.csv에 없다";
                else enemies.Add(u);
            }
            if (allies.Count == 0 || enemies.Count == 0) error ??= "아군이나 적이 한 명도 없다";
            return new BattleCore(data, allies, enemies, rules, seed);
        }

        public void Start()
        {
            Emit(new BattleEvent { Kind = BattleEventKind.BattleStarted, Text = "전투 시작" });
            BeginRound();
        }

        public List<BattleEvent> TakeEvents()
        {
            var copy = new List<BattleEvent>(events);
            events.Clear();
            return copy;
        }

        /// <summary>이번 라운드에 플레이어가 명령을 내려야 하는 아군</summary>
        public List<Unit> AlliesNeedingCommand => State.Living(Side.Ally);

        public bool CanUse(Unit u, SkillDef s)
        {
            if (string.IsNullOrEmpty(s.costKind) || s.costValue <= 0) return true;
            if (string.Equals(s.costKind, Rules.HpStat, StringComparison.OrdinalIgnoreCase)) return u.Hp > s.costValue;
            return u.Gauges.TryGetValue(s.costKind, out int v) && v >= s.costValue;
        }

        public bool NeedsTargetChoice(SkillDef s) => s.target == TargetRule.SingleEnemy || s.target == TargetRule.SingleAlly;

        public List<Unit> ValidTargets(Unit actor, SkillDef s) => s.target switch
        {
            TargetRule.SingleEnemy or TargetRule.AllEnemies => State.Opponents(actor),
            TargetRule.SingleAlly or TargetRule.AllAllies => State.Friends(actor),
            _ => new List<Unit> { actor },
        };

        /// <summary>대상이 정해지지 않은 행동의 대상을 채운다 (전체 공격, 자신 등).</summary>
        public void FillTargets(BattleAction a)
        {
            if (a.Targets.Count > 0) return;
            var valid = ValidTargets(a.Actor, a.Skill);
            if (NeedsTargetChoice(a.Skill)) { if (valid.Count > 0) a.Targets.Add(valid[Rng.Next(valid.Count)]); }
            else a.Targets.AddRange(valid);
        }

        /// <summary>한 라운드를 진행한다. allyActions는 살아 있는 아군의 행동 (비면 아군 AI가 고른다).</summary>
        public void RunRound(List<BattleAction> allyActions)
        {
            if (IsOver) return;
            var actions = new List<BattleAction>();
            foreach (var ally in State.Living(Side.Ally))
            {
                var a = allyActions?.Find(x => x.Actor == ally) ?? Rules.AllyAI.Choose(this, ally, Rng);
                if (a != null) actions.Add(a);
            }
            foreach (var enemy in State.Living(Side.Enemy))
            {
                var a = Rules.EnemyAI.Choose(this, enemy, Rng);
                if (a != null) actions.Add(a);
            }
            foreach (var a in actions) FillTargets(a);

            Rules.Resolver.Resolve(this, Rules.TurnOrder.Order(State, actions, Rng));

            if (!IsOver && State.Round >= Rules.MaxRounds) End(Side.Enemy, $"{Rules.MaxRounds}라운드가 지나 패배");
            if (!IsOver) BeginRound();
        }

        void BeginRound()
        {
            State.Round++;
            Emit(new BattleEvent { Kind = BattleEventKind.RoundStarted, Amount = State.Round, Text = $"라운드 {State.Round}" });
        }

        /// <summary>행동 하나를 처리한다. IActionResolver가 부른다.</summary>
        public void ExecuteAction(BattleAction a)
        {
            var actor = a.Actor;
            if (IsOver || actor.IsDown) return;

            Emit(new BattleEvent { Kind = BattleEventKind.TurnStarted, Actor = actor, Text = $"{actor.Name}의 차례" });
            TriggerStatuses(actor, StatusTrigger.OnTurnStart, null);
            if (CheckEnd() || actor.IsDown) return;

            actor.SkipThisTurn = false;
            TriggerStatuses(actor, StatusTrigger.OnAct, null);
            if (CheckEnd() || actor.IsDown) return;

            if (actor.SkipThisTurn)
            {
                Emit(new BattleEvent { Kind = BattleEventKind.ActionSkipped, Actor = actor, Text = $"{actor.Name}는 움직일 수 없다" });
            }
            else if (!CanUse(actor, a.Skill))
            {
                Emit(new BattleEvent { Kind = BattleEventKind.NotEnoughCost, Actor = actor, Skill = a.Skill, Text = $"{actor.Name}: {a.Skill.costKind}가 부족하다" });
            }
            else
            {
                PayCost(actor, a.Skill);
                Emit(new BattleEvent { Kind = BattleEventKind.SkillUsed, Actor = actor, Skill = a.Skill, Text = $"{actor.Name}의 {a.Skill.displayName}" });
                foreach (var target in Retarget(a))
                {
                    if (IsOver) break;
                    if (a.Skill.accuracy < 100 && Rng.Next(100) >= a.Skill.accuracy)
                    {
                        Emit(new BattleEvent { Kind = BattleEventKind.Missed, Actor = actor, Target = target, Skill = a.Skill, Text = $"{target.Name}에게 빗나갔다" });
                        continue;
                    }
                    foreach (var e in a.Skill.effects) ApplyEffect(e, actor, target, a.Skill, null, 1);
                    if (CheckEnd()) return;
                }
            }

            if (!actor.IsDown) TriggerStatuses(actor, StatusTrigger.OnTurnEnd, null);
            TickStatuses(actor);
            CheckEnd();
        }

        /// <summary>단일 대상이 이미 쓰러졌으면 같은 편의 다른 대상으로 바꾼다.</summary>
        List<Unit> Retarget(BattleAction a)
        {
            var result = a.Targets.Where(t => !t.IsDown).ToList();
            if (result.Count == 0 && NeedsTargetChoice(a.Skill))
            {
                var valid = ValidTargets(a.Actor, a.Skill);
                if (valid.Count > 0) result.Add(valid[Rng.Next(valid.Count)]);
            }
            return result;
        }

        void PayCost(Unit u, SkillDef s)
        {
            if (string.IsNullOrEmpty(s.costKind) || s.costValue <= 0) return;
            if (string.Equals(s.costKind, Rules.HpStat, StringComparison.OrdinalIgnoreCase))
            {
                u.Hp = Math.Max(1, u.Hp - s.costValue);
                return;
            }
            ChangeGauge(u, s.costKind, -s.costValue);
        }

        // ------------------------------------------------------------ 효과

        /// <summary>
        /// 효과 하나를 적용한다. 이름 앞에 self: 를 붙이면 사용자(상태이상이면 그 주인)에게, other: 를 붙이면
        /// 상대(OnHit 상태이상의 공격자)에게 적용한다.
        /// </summary>
        public void ApplyEffect(EffectSpec e, Unit source, Unit target, SkillDef skill, StatusDef fromStatus, int stacks)
        {
            string name = e.name;
            if (name.StartsWith("self:", StringComparison.OrdinalIgnoreCase)) { target = source; name = name.Substring(5); }
            else if (name.StartsWith("other:", StringComparison.OrdinalIgnoreCase)) { name = name.Substring(6); }
            if (target == null || (target.IsDown && !name.Equals("Revive", StringComparison.OrdinalIgnoreCase))) return;

            switch (name.ToLowerInvariant())
            {
                case "damage":
                {
                    int power = e.Arg(0, "power") == "power" ? skill?.power ?? 0 : e.IntArg(0, 0);
                    int dmg = Rules.Damage.Compute(source, target, skill, power, Data, Rng, out bool crit, out float typeMul);
                    DealDamage(source, target, dmg * stacks, skill, crit, typeMul);
                    break;
                }
                case "fixeddamage":
                    DealDamage(source, target, e.IntArg(0, 0) * stacks, skill, false, 1f);
                    break;
                case "percentdamage":
                    DealDamage(source, target, Math.Max(1, target.MaxHp * e.IntArg(0, 0) / 100) * stacks, skill, false, 1f);
                    break;
                case "heal":
                {
                    int pct = e.Arg(0, "power") == "power" ? skill?.power ?? 0 : e.IntArg(0, 0);
                    Heal(target, Math.Max(1, target.MaxHp * pct / 100) * stacks);
                    break;
                }
                case "applystatus":
                {
                    var def = Data.Status(e.Arg(0));
                    if (def == null) break;
                    int chance = e.IntArg(2, 100);
                    if (chance < 100 && Rng.Next(100) >= chance) break;
                    AddStatus(target, def, e.IntArg(1, def.duration));
                    break;
                }
                case "removestatus":
                {
                    int removed = target.Statuses.RemoveAll(s => string.Equals(s.Def.id, e.Arg(0), StringComparison.OrdinalIgnoreCase));
                    if (removed > 0) Emit(new BattleEvent { Kind = BattleEventKind.StatusExpired, Target = target, Status = Data.Status(e.Arg(0)), Text = $"{target.Name}의 {e.Arg(0)}가 풀렸다" });
                    break;
                }
                case "modifystat":
                {
                    string stat = e.Arg(0);
                    target.Stages.TryGetValue(stat, out int stage);
                    int next = Math.Clamp(stage + e.IntArg(1, 0), -6, 6);
                    target.Stages[stat] = next;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatChanged, Target = target, Amount = next - stage, Text = $"{target.Name}의 {stat} {(next >= stage ? "상승" : "하락")}" });
                    break;
                }
                case "changegauge":
                    ChangeGauge(target, e.Arg(0), e.IntArg(1, 0));
                    break;
                case "skipturn":
                    target.SkipThisTurn = true;
                    break;
                default:
                    // TODO(규칙 대기): 비디님의 특수 시스템은 여기에 효과를 추가한다.
                    Emit(new BattleEvent { Kind = BattleEventKind.ActionSkipped, Actor = source, Text = $"(모르는 효과 {e})" });
                    break;
            }
        }

        void DealDamage(Unit source, Unit target, int amount, SkillDef skill, bool crit, float typeMul)
        {
            amount = Math.Max(0, amount);
            target.Hp = Math.Max(0, target.Hp - amount);
            string note = crit ? " (급소)" : typeMul > 1.01f ? " (효과가 굉장하다)" : typeMul < 0.99f ? " (효과가 별로다)" : "";
            Emit(new BattleEvent { Kind = BattleEventKind.Damaged, Actor = source, Target = target, Skill = skill, Amount = amount, HpAfter = target.Hp, Critical = crit, TypeMultiplier = typeMul, Text = $"{target.Name}에게 {amount}{note}" });

            if (target.IsDown)
            {
                target.Statuses.Clear();
                Emit(new BattleEvent { Kind = BattleEventKind.UnitDown, Target = target, Text = $"{target.Name} 쓰러짐" });
                return;
            }
            if (source != null && source != target) TriggerStatuses(target, StatusTrigger.OnHit, source);
        }

        void Heal(Unit target, int amount)
        {
            int before = target.Hp;
            target.Hp = Math.Min(target.MaxHp, target.Hp + amount);
            Emit(new BattleEvent { Kind = BattleEventKind.Healed, Target = target, Amount = target.Hp - before, HpAfter = target.Hp, Text = $"{target.Name} {target.Hp - before} 회복" });
        }

        void ChangeGauge(Unit u, string gauge, int delta)
        {
            if (string.IsNullOrEmpty(gauge)) return;
            u.Gauges.TryGetValue(gauge, out int v);
            int max = u.GaugeMax.TryGetValue(gauge, out int m) && m > 0 ? m : int.MaxValue;
            int next = Math.Clamp(v + delta, 0, max);
            u.Gauges[gauge] = next;
            Emit(new BattleEvent { Kind = BattleEventKind.GaugeChanged, Target = u, Amount = next - v, HpAfter = next, Text = $"{u.Name} {gauge} {next}" });
        }

        void AddStatus(Unit target, StatusDef def, int turns)
        {
            var existing = target.Statuses.Find(s => s.Def == def);
            if (existing != null)
            {
                if (def.stack == StackRule.Ignore) return;
                existing.TurnsLeft = Math.Max(existing.TurnsLeft, turns);
                if (def.stack == StackRule.Stack) existing.Stacks++;
            }
            else
            {
                target.Statuses.Add(new StatusInstance { Def = def, TurnsLeft = turns });
            }
            Emit(new BattleEvent { Kind = BattleEventKind.StatusApplied, Target = target, Status = def, Amount = turns, Text = $"{target.Name}: {def.displayName}" });
        }

        void TriggerStatuses(Unit owner, StatusTrigger trigger, Unit other)
        {
            foreach (var s in owner.Statuses.Where(s => s.Def.trigger == trigger).ToList())
            {
                foreach (var e in s.Def.effects)
                {
                    if (owner.IsDown) return;
                    var target = e.name.StartsWith("other:", StringComparison.OrdinalIgnoreCase) && other != null ? other : owner;
                    ApplyEffect(e, owner, target, null, s.Def, s.Stacks);
                }
            }
        }

        void TickStatuses(Unit owner)
        {
            foreach (var s in owner.Statuses.ToList())
            {
                if (--s.TurnsLeft > 0) continue;
                owner.Statuses.Remove(s);
                Emit(new BattleEvent { Kind = BattleEventKind.StatusExpired, Target = owner, Status = s.Def, Text = $"{owner.Name}의 {s.Def.displayName}가 풀렸다" });
            }
        }

        bool CheckEnd()
        {
            if (IsOver) return true;
            var winner = Rules.Win.Winner(State);
            if (winner == null) return false;
            End(winner.Value, winner == Side.Ally ? "승리" : "패배");
            return true;
        }

        void End(Side winner, string text)
        {
            IsOver = true;
            Winner = winner;
            Emit(new BattleEvent { Kind = BattleEventKind.BattleEnded, Winner = winner, Text = text });
        }

        void Emit(BattleEvent e) => events.Add(e);
    }

    // ================================================================ 기본 규칙 (포켓몬과 비슷하게)

    public sealed class SpeedOrder : ITurnOrderRule
    {
        public List<BattleAction> Order(BattleState state, List<BattleAction> actions, Random rng)
        {
            // 같은 속도는 무작위. 정렬 전에 섞어서 동점 순서를 정한다.
            var shuffled = actions.OrderBy(_ => rng.Next()).ToList();
            return shuffled.OrderByDescending(a => a.Actor.Stat(a.Actor.Rules.SpeedStat)).ToList();
        }
    }

    public sealed class PokemonLikeDamage : IDamageFormula
    {
        public int Level = 50;
        public float CritChance = 1f / 16f;
        public float CritMultiplier = 1.5f;
        public float SameTypeBonus = 1.5f;

        public int Compute(Unit attacker, Unit defender, SkillDef skill, int power, BattleDatabase data, Random rng, out bool critical, out float typeMultiplier)
        {
            critical = false;
            typeMultiplier = data.TypeMultiplier(skill?.type, defender.Def.type);
            if (power <= 0 || typeMultiplier <= 0f) return 0;

            float a = attacker.Stat(attacker.Rules.AttackStat);
            float d = Math.Max(1, defender.Stat(defender.Rules.DefenseStat));
            float baseDamage = ((2f * Level / 5f + 2f) * power * a / d) / 50f + 2f;

            float mul = typeMultiplier;
            if (skill != null && !string.IsNullOrEmpty(skill.type) && string.Equals(skill.type, attacker.Def.type, StringComparison.OrdinalIgnoreCase)) mul *= SameTypeBonus;
            if (rng.NextDouble() < CritChance)
            {
                critical = true;
                mul *= CritMultiplier;
            }
            mul *= 0.85f + (float)rng.NextDouble() * 0.15f;
            return Math.Max(1, (int)(baseDamage * mul));
        }
    }

    public sealed class WipeOut : IWinCondition
    {
        public Side? Winner(BattleState state)
        {
            if (state.Living(Side.Enemy).Count == 0) return Side.Ally;
            if (state.Living(Side.Ally).Count == 0) return Side.Enemy;
            return null;
        }
    }

    public sealed class SequentialResolver : IActionResolver
    {
        public void Resolve(BattleCore core, List<BattleAction> orderedActions)
        {
            foreach (var a in orderedActions)
            {
                if (core.IsOver) return;
                core.ExecuteAction(a);
            }
        }
    }

    /// <summary>기본 AI: 쓸 수 있는 스킬 중 가중치 랜덤, 공격은 체력 비율이 낮은 대상을 우선 (70%).</summary>
    public sealed class WeightedRandomAI : IUnitAI
    {
        public BattleAction Choose(BattleCore core, Unit unit, Random rng)
        {
            var usable = unit.Skills.Where(s => core.CanUse(unit, s)).ToList();
            if (usable.Count == 0) return null;

            var weights = usable.Select(s => Weight(core, unit, s)).ToList();
            float total = weights.Sum();
            float pick = (float)rng.NextDouble() * total;
            var skill = usable[usable.Count - 1];
            for (int i = 0; i < usable.Count; i++)
            {
                pick -= weights[i];
                if (pick <= 0f) { skill = usable[i]; break; }
            }

            var action = new BattleAction { Actor = unit, Skill = skill };
            var valid = core.ValidTargets(unit, skill);
            if (core.NeedsTargetChoice(skill) && valid.Count > 0)
            {
                var weakest = valid.OrderBy(t => t.HpRatio).First();
                action.Targets.Add(rng.NextDouble() < 0.7 ? weakest : valid[rng.Next(valid.Count)]);
            }
            return action;
        }

        static float Weight(BattleCore core, Unit unit, SkillDef s)
        {
            bool heals = s.effects.Any(e => e.name.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0);
            if (heals) return core.State.Friends(unit).Any(f => f.HpRatio < 0.5f) ? 3f : 0.2f;
            return s.effects.Any(e => e.name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0) ? 2f : 1f;
        }
    }

    /// <summary>탐욕 AI (시뮬레이터용): 아군이 위험하면 회복, 아니면 기대 피해가 가장 큰 스킬과 대상.</summary>
    public sealed class GreedyAI : IUnitAI
    {
        public BattleAction Choose(BattleCore core, Unit unit, Random rng)
        {
            var usable = unit.Skills.Where(s => core.CanUse(unit, s)).ToList();
            if (usable.Count == 0) return null;

            var hurt = core.State.Friends(unit).OrderBy(f => f.HpRatio).First();
            var heal = usable.FirstOrDefault(s => s.effects.Any(e => e.name.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0));
            if (heal != null && hurt.HpRatio < 0.4f)
            {
                var a = new BattleAction { Actor = unit, Skill = heal };
                if (core.NeedsTargetChoice(heal)) a.Targets.Add(heal.target == TargetRule.SingleAlly ? hurt : unit);
                return a;
            }

            BattleAction best = null;
            float bestScore = float.MinValue;
            foreach (var s in usable)
            {
                foreach (var t in core.ValidTargets(unit, s))
                {
                    float typeMul = core.Data.TypeMultiplier(s.type, t.Def.type);
                    int targets = core.NeedsTargetChoice(s) ? 1 : core.ValidTargets(unit, s).Count;
                    float score = s.power * typeMul * Math.Min(100, s.accuracy) / 100f * targets + (t.HpRatio < 0.3f ? 20f : 0f);
                    if (s.power <= 0) score = 10f + (float)rng.NextDouble();
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new BattleAction { Actor = unit, Skill = s };
                        if (core.NeedsTargetChoice(s)) best.Targets.Add(t);
                    }
                    if (!core.NeedsTargetChoice(s)) break;
                }
            }
            return best;
        }
    }
}
