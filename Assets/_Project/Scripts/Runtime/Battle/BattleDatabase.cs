using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectVita.Battle
{
    /// <summary>모든 전투 데이터. Resources/BattleDatabase.asset (손으로 고치지 않는다)</summary>
    public sealed class BattleDatabase : ScriptableObject
    {
        public const string ResourcePath = "BattleDatabase";

        public List<StatDef> stats = new List<StatDef>();
        public List<UnitDef> units = new List<UnitDef>();
        public List<SkillDef> skills = new List<SkillDef>();
        public List<StatusDef> statuses = new List<StatusDef>();
        public List<TypeRow> typeChart = new List<TypeRow>();
        public List<EncounterDef> encounters = new List<EncounterDef>();

        static BattleDatabase instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static BattleDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<BattleDatabase>(ResourcePath);
                    if (instance == null)
                    {
                        Debug.LogError("[전투] Resources/BattleDatabase가 없다. Tools > Project Vita > CSV 임포트를 실행한다.");
                        instance = CreateInstance<BattleDatabase>();
                    }
                }
                return instance;
            }
        }

        public UnitDef Unit(string id) => units.Find(u => Same(u.id, id));
        public SkillDef Skill(string id) => skills.Find(s => Same(s.id, id));
        public StatusDef Status(string id) => statuses.Find(s => Same(s.id, id));
        public EncounterDef Encounter(string id) => encounters.Find(e => Same(e.id, id));

        /// <summary>공격 타입 → 방어 타입 배율. 표에 없으면 1.0 (상성이 없으면 전부 1.0)</summary>
        public float TypeMultiplier(string attackType, string defenseType)
        {
            if (string.IsNullOrEmpty(attackType) || string.IsNullOrEmpty(defenseType)) return 1f;
            var row = typeChart.Find(r => Same(r.attackType, attackType));
            var cell = row?.multipliersPercent.Find(c => Same(c.stat, defenseType));
            return cell == null ? 1f : cell.value / 100f;
        }

        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
