using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ProjectVita.Battle;
using UnityEditor;
using UnityEngine;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// 전투 밸런스 시뮬레이터 (문서 02, 6장). BattleCore를 화면 없이 수천 판 돌려서 결과표를 낸다.
    /// 입력: 조우, 반복 횟수, 아군 AI(랜덤/탐욕), 시드. 출력: 승률, 평균 라운드, 유닛별 평균 피해와 쓰러진 비율, 스킬 사용 빈도.
    /// 결과는 창에 보이고, CSV로 저장할 수 있다 (비디님께 엑셀로 건넬 때).
    /// </summary>
    public sealed class BattleSimulatorWindow : EditorWindow
    {
        int encounterIndex;
        int iterations = 1000;
        bool greedyAllies;
        int seed = 12345;
        string result = "";
        string csv = "";
        Vector2 scroll;

        [MenuItem("Tools/Project Vita/전투 시뮬레이터", priority = 20)]
        public static void Open() => GetWindow<BattleSimulatorWindow>("전투 시뮬레이터");

        void OnGUI()
        {
            var db = AssetDatabase.LoadAssetAtPath<BattleDatabase>("Assets/_Project/Resources/BattleDatabase.asset");
            if (db == null || db.encounters.Count == 0)
            {
                EditorGUILayout.HelpBox("BattleDatabase가 없다. Tools > Project Vita > CSV 임포트를 먼저 실행한다.", MessageType.Warning);
                return;
            }

            var names = db.encounters.Select(e => e.id).ToArray();
            encounterIndex = Mathf.Clamp(EditorGUILayout.Popup("조우", encounterIndex, names), 0, names.Length - 1);
            iterations = Mathf.Clamp(EditorGUILayout.IntField("반복 횟수", iterations), 1, 100000);
            greedyAllies = EditorGUILayout.Popup("아군 AI", greedyAllies ? 1 : 0, new[] { "랜덤 (가중치)", "탐욕" }) == 1;
            seed = EditorGUILayout.IntField("시드", seed);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("실행", GUILayout.Height(30))) Run(db, db.encounters[encounterIndex]);
                GUI.enabled = csv.Length > 0;
                if (GUILayout.Button("CSV로 저장", GUILayout.Height(30), GUILayout.Width(120))) SaveCsv();
                GUI.enabled = true;
            }

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(result, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void Run(BattleDatabase db, EncounterDef enc)
        {
            var summary = Simulate(db, enc, iterations, greedyAllies, seed);
            result = summary.ToText();
            csv = summary.ToCsv();
            Debug.Log("[시뮬레이터]\n" + result);
        }

        void SaveCsv()
        {
            string path = EditorUtility.SaveFilePanel("시뮬레이션 결과 저장", "", "battle_sim.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, csv, new UTF8Encoding(true)); // 엑셀에서 한글이 깨지지 않게 BOM
        }

        // ================================================================ 시뮬레이션 (메뉴, MCP, 테스트에서도 쓴다)

        public sealed class Summary
        {
            public string Encounter;
            public int Battles, Wins;
            public long TotalRounds;
            public readonly Dictionary<string, UnitStats> Units = new Dictionary<string, UnitStats>();
            public readonly Dictionary<string, int> SkillUses = new Dictionary<string, int>();
            public string Error;

            public string ToText()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"조우 {Encounter}, {Battles}판");
                if (Error != null) sb.AppendLine("경고: " + Error);
                sb.AppendLine($"승률 {100f * Wins / Battles:0.0}%   평균 라운드 {(float)TotalRounds / Battles:0.00}");
                sb.AppendLine();
                sb.AppendLine("유닛                    판당 피해   판당 받은 피해   쓰러진 비율");
                foreach (var u in Units.Values)
                    sb.AppendLine($"{u.Label,-22} {u.Dealt / (float)Battles,10:0.0} {u.Taken / (float)Battles,14:0.0} {100f * u.Downs / Battles,12:0.0}%");
                sb.AppendLine();
                sb.AppendLine("스킬 사용 횟수 (판당)");
                foreach (var kv in SkillUses.OrderByDescending(k => k.Value))
                    sb.AppendLine($"  {kv.Key,-26} {kv.Value / (float)Battles:0.00}");
                return sb.ToString();
            }

            public string ToCsv()
            {
                var sb = new StringBuilder();
                sb.Append("encounter,battles,win_rate,avg_rounds\n");
                sb.Append($"{Encounter},{Battles},{(float)Wins / Battles:0.000},{(float)TotalRounds / Battles:0.00}\n\n");
                sb.Append("unit,side,dealt_per_battle,taken_per_battle,down_rate\n");
                foreach (var u in Units.Values)
                    sb.Append($"{u.Id},{u.Side},{u.Dealt / (float)Battles:0.0},{u.Taken / (float)Battles:0.0},{(float)u.Downs / Battles:0.000}\n");
                sb.Append("\nskill,uses_per_battle\n");
                foreach (var kv in SkillUses.OrderByDescending(k => k.Value))
                    sb.Append($"{kv.Key},{kv.Value / (float)Battles:0.00}\n");
                return sb.ToString();
            }
        }

        public sealed class UnitStats
        {
            public string Id, Label;
            public Side Side;
            public long Dealt, Taken;
            public int Downs;
        }

        public static Summary Simulate(BattleDatabase db, EncounterDef enc, int iterations, bool greedyAllies, int seed)
        {
            var summary = new Summary { Encounter = enc.id };
            var rng = new System.Random(seed);
            for (int i = 0; i < iterations; i++)
            {
                var rules = BattleRules.Default();
                if (greedyAllies) rules.AllyAI = new GreedyAI();
                var core = BattleCore.FromEncounter(db, enc, rules, rng.Next(), out var error);
                summary.Error ??= error;
                core.Start();
                int guard = 0;
                while (!core.IsOver && guard++ < 1000)
                {
                    core.RunRound(null); // 아군도 AI가 고른다
                    Collect(summary, core.TakeEvents());
                }
                summary.Battles++;
                if (core.Winner == Side.Ally) summary.Wins++;
                summary.TotalRounds += core.State.Round;
            }
            return summary;
        }

        static void Collect(Summary s, List<BattleEvent> events)
        {
            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case BattleEventKind.SkillUsed:
                        string key = $"{e.Actor.Def.id}:{e.Skill.id}";
                        s.SkillUses[key] = s.SkillUses.TryGetValue(key, out int n) ? n + 1 : 1;
                        break;
                    case BattleEventKind.Damaged:
                        if (e.Actor != null && e.Actor != e.Target) Stats(s, e.Actor).Dealt += e.Amount;
                        Stats(s, e.Target).Taken += e.Amount;
                        break;
                    case BattleEventKind.UnitDown:
                        Stats(s, e.Target).Downs++;
                        break;
                }
            }
        }

        static UnitStats Stats(Summary s, Unit u)
        {
            string key = $"{u.Side}_{u.Slot}_{u.Def.id}";
            if (!s.Units.TryGetValue(key, out var st))
                s.Units[key] = st = new UnitStats { Id = u.Def.id, Label = $"{(u.Side == Side.Ally ? "아군" : "적")} {u.Def.id}", Side = u.Side };
            return st;
        }
    }
}
