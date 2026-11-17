using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ProjectVita.Battle
{
    // 전투 데이터 정의. Data/Battle_*.csv를 에디터 임포터가 BattleDatabase 에셋으로 바꾼다.
    // 이 파일의 클래스는 Unity 기능을 쓰지 않는 순수 C# 데이터라서 BattleCore와 시뮬레이터가 그대로 쓴다.
    // 스탯은 문자열 id로 다룬다. 비디님이 스탯을 새로 만들어도 코드를 고칠 필요가 없다.

    public enum Side { Ally, Enemy }

    public enum TargetRule { SingleEnemy, AllEnemies, Self, SingleAlly, AllAllies }

    public enum StatusTrigger { OnTurnStart, OnTurnEnd, OnHit, OnAct }

    public enum StackRule { Refresh, Stack, Ignore }

    public enum LoseRule { GameOver, Retry, Continue }

    [Serializable]
    public sealed class StatValue
    {
        public string stat;
        public int value;
    }

    [Serializable]
    public sealed class StatDef
    {
        public string id;
        public string displayName;
        public string description;
        public int min;
        public int max;
    }

    /// <summary>효과 하나: Name(arg1,arg2,...). 예: Damage(power), ApplyStatus(burn,2,30%)</summary>
    [Serializable]
    public sealed class EffectSpec
    {
        public string name;
        public string[] args = Array.Empty<string>();

        public string Arg(int i, string fallback = "") => i < args.Length && args[i].Length > 0 ? args[i] : fallback;

        public int IntArg(int i, int fallback)
        {
            string a = Arg(i).TrimEnd('%');
            return int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        public override string ToString() => $"{name}({string.Join(",", args)})";

        /// <summary>"Damage(power); ApplyStatus(burn,2,30%)" → 효과 목록. 문법 오류는 errors에 쌓는다.</summary>
        public static List<EffectSpec> ParseList(string text, List<string> errors)
        {
            var list = new List<EffectSpec>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var raw in text.Split(';'))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                int open = part.IndexOf('(');
                int close = part.LastIndexOf(')');
                if (open < 0 && close < 0)
                {
                    list.Add(new EffectSpec { name = part });
                    continue;
                }
                if (open <= 0 || close != part.Length - 1)
                {
                    errors?.Add($"'{part}'의 괄호가 맞지 않다 (예: Damage(power))");
                    continue;
                }
                var args = part.Substring(open + 1, close - open - 1).Split(',');
                for (int i = 0; i < args.Length; i++) args[i] = args[i].Trim();
                if (args.Length == 1 && args[0].Length == 0) args = Array.Empty<string>();
                list.Add(new EffectSpec { name = part.Substring(0, open).Trim(), args = args });
            }
            return list;
        }
    }

    [Serializable]
    public sealed class UnitDef
    {
        public string id;
        public string displayName;
        public Side side;
        public string type;
        public List<StatValue> stats = new List<StatValue>();
        public List<string> skills = new List<string>();
        public string passive;
        /// <summary>전투 그림. Art/Battle/{id}.png 또는 대화 캐릭터 id (기본 표정)</summary>
        public string portraitId;

        public int Stat(string statId, int fallback = 0)
        {
            foreach (var s in stats)
                if (string.Equals(s.stat, statId, StringComparison.OrdinalIgnoreCase)) return s.value;
            return fallback;
        }
    }

    [Serializable]
    public sealed class SkillDef
    {
        public string id;
        public string displayName;
        public string description;
        public string type;
        public int power;
        /// <summary>명중률 0~100. 100 이상이면 반드시 맞는다.</summary>
        public int accuracy = 100;
        /// <summary>비용 게이지 id (비면 비용 없음). 예: sp</summary>
        public string costKind;
        public int costValue;
        public TargetRule target;
        public string effectsText;
        public List<EffectSpec> effects = new List<EffectSpec>();
    }

    [Serializable]
    public sealed class StatusDef
    {
        public string id;
        public string displayName;
        /// <summary>지속 턴 수 (그 유닛의 턴 기준)</summary>
        public int duration;
        public StatusTrigger trigger;
        public string effectsText;
        public List<EffectSpec> effects = new List<EffectSpec>();
        public StackRule stack;
    }

    [Serializable]
    public sealed class TypeRow
    {
        public string attackType;
        public List<StatValue> multipliersPercent = new List<StatValue>(); // stat = 방어 타입, value = 배율 x100
    }

    [Serializable]
    public sealed class EncounterDef
    {
        public string id;
        public List<string> enemies = new List<string>();
        /// <summary>출전 아군. 비면 Side=Ally인 유닛 앞에서부터 BattleRules.PartySize명. TODO(규칙 대기): 출전 인원</summary>
        public List<string> allies = new List<string>();
        public string background;
        public string bgm;
        public string winNode;
        public string loseNode;
        public LoseRule loseRule;
    }
}
