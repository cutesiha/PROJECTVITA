using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ProjectVita.Battle;
using ProjectVita.Core;
using ProjectVita.Exploration;
using ProjectVita.Story;
using UnityEditor;
using UnityEngine;
using Yarn.Unity;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// Data/Exploration_*.csv, Data/Battle_*.csv → Resources/ExplorationData.asset, Resources/BattleDatabase.asset.
    /// CSV가 바뀌면 자동으로 다시 임포트한다. 수동: Tools > Project Vita > CSV 임포트 (탐색, 전투).
    /// 없는 Yarn 노드, 중복 id, 없는 스킬/상태이상/유닛, 효과 문법 오류, 조건식 오류를 행 번호와 함께 경고한다.
    /// 탐색 자동 변수($오브젝트_seen, $장소_visited)의 Yarn 선언 파일도 만든다 (Dialogue/_Generated/Exploration.yarn).
    /// </summary>
    public sealed class DataImporter : AssetPostprocessor
    {
        const string MenuRoot = "Tools/Project Vita/";
        const string DataDir = "Assets/_Project/Data";
        const string ExplorationAssetPath = "Assets/_Project/Resources/ExplorationData.asset";
        const string BattleAssetPath = "Assets/_Project/Resources/BattleDatabase.asset";
        const string YarnProjectPath = "Assets/_Project/Dialogue/Dialogue.yarnproject";
        const string GeneratedYarnPath = "Assets/_Project/Dialogue/_Generated/Exploration.yarn";

        static readonly string[] KnownEffects =
            { "damage", "fixeddamage", "percentdamage", "heal", "applystatus", "removestatus", "modifystat", "changegauge", "skipturn" };

        static bool queued;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
        {
            if (queued) return;
            bool relevant = imported.Concat(deleted).Concat(moved).Any(p =>
                p.StartsWith(DataDir + "/Exploration_", StringComparison.Ordinal) || p.StartsWith(DataDir + "/Battle_", StringComparison.Ordinal));
            if (!relevant) return;
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                ImportAll();
            };
        }

        [MenuItem(MenuRoot + "CSV 임포트 (탐색, 전투)", priority = 14)]
        public static void ImportAll()
        {
            var report = new Report();
            var nodes = LoadNodeNames(report);
            var battle = ImportBattle(report, nodes);
            ImportExploration(report, nodes, battle);
            AssetDatabase.SaveAssets();
            report.Print();
        }

        // ================================================================ 탐색

        static void ImportExploration(Report report, HashSet<string> nodes, BattleDatabase battle)
        {
            var locTable = Load("Exploration_Location", report);
            var objTable = Load("Exploration_Object", report);
            if (locTable == null || objTable == null) return;

            var data = LoadOrCreate<ExplorationData>(ExplorationAssetPath);
            data.locations.Clear();
            data.objects.Clear();
            var catalog = AssetCatalog.Instance;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in locTable.Rows)
            {
                string where = $"Exploration_Location.csv {row.LineNumber}행";
                var loc = new ExplorationData.LocationDef
                {
                    id = row["location_id"],
                    displayName = row["display_name"],
                    background = row["background"],
                    enterNode = row["enter_node"],
                    unlockIf = row["unlock_if"],
                };
                if (!CheckId(loc.id, ids, where, report)) continue;
                if (string.IsNullOrEmpty(loc.displayName)) loc.displayName = loc.id;
                CheckNode(loc.enterNode, nodes, where, report);
                CheckCondition(loc.unlockIf, where, report);
                if (!string.IsNullOrEmpty(loc.background) && catalog.FindBackground(loc.background) == null)
                    report.Warn($"{where}: 배경 '{loc.background}'이 Art/Backgrounds에 없다.");
                data.locations.Add(loc);
            }

            ids.Clear();
            foreach (var row in objTable.Rows)
            {
                string where = $"Exploration_Object.csv {row.LineNumber}행";
                var obj = new ExplorationData.ObjectDef
                {
                    id = row["object_id"],
                    locationId = row["location_id"],
                    displayName = row["display_name"],
                    node = row["node"],
                    showIf = row["show_if"],
                    once = ParseBool(row["once"]),
                    encounterId = row["encounter_id"],
                };
                if (!CheckId(obj.id, ids, where, report)) continue;
                if (string.IsNullOrEmpty(obj.displayName)) obj.displayName = obj.id;
                if (data.FindLocation(obj.locationId) == null) report.Error($"{where}: 장소 '{obj.locationId}'가 Exploration_Location.csv에 없다.");
                CheckNode(obj.node, nodes, where, report);
                CheckCondition(obj.showIf, where, report);
                if (!string.IsNullOrEmpty(obj.encounterId) && battle != null && battle.Encounter(obj.encounterId) == null)
                    report.Error($"{where}: 조우 '{obj.encounterId}'가 Battle_Encounter.csv에 없다.");
                if (!TryParseArea(row["area"], out obj.area))
                    report.Error($"{where}: area '{row["area"]}'를 읽지 못했다. \"x y 너비 높이\" (1920x1080, 왼쪽 위 기준 픽셀)로 쓴다.");
                data.objects.Add(obj);
            }

            EditorUtility.SetDirty(data);
            report.Info($"탐색: 장소 {data.locations.Count}, 오브젝트 {data.objects.Count}");
            WriteGeneratedYarn(data, report);
        }

        /// <summary>탐색 자동 변수를 Yarn에서 쓸 수 있게 선언한다. 내용이 같으면 다시 쓰지 않는다 (불필요한 재임포트 방지).</summary>
        static void WriteGeneratedYarn(ExplorationData data, Report report)
        {
            var sb = new StringBuilder();
            sb.Append("title: Generated_Exploration\n---\n");
            sb.Append("// 자동 생성 파일이다. 고치지 않는다. (CSV 임포트가 Exploration CSV에서 만든다)\n");
            sb.Append("// 탐색에서 자동으로 켜지는 변수: $오브젝트id_seen (조사함), $장소id_visited (들어가 봄)\n");
            sb.Append("// 같은 변수를 다른 .yarn에서 또 <<declare>>하면 중복 선언 오류가 난다.\n");
            foreach (var loc in data.locations) sb.Append("<<declare ").Append(ExplorationData.VisitedVariable(loc.id)).Append(" = false>>\n");
            foreach (var obj in data.objects) sb.Append("<<declare ").Append(ExplorationData.SeenVariable(obj.id)).Append(" = false>>\n");
            sb.Append("===\n");

            string text = sb.ToString();
            if (File.Exists(GeneratedYarnPath) && File.ReadAllText(GeneratedYarnPath) == text) return;
            Directory.CreateDirectory(Path.GetDirectoryName(GeneratedYarnPath));
            File.WriteAllText(GeneratedYarnPath, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(GeneratedYarnPath);
            report.Info("탐색 변수 선언 갱신: " + GeneratedYarnPath);
        }

        static bool TryParseArea(string text, out Rect rect)
        {
            rect = default;
            var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return false;
            var v = new float[4];
            for (int i = 0; i < 4; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
            rect = new Rect(v[0], v[1], v[2], v[3]);
            return v[2] > 0 && v[3] > 0;
        }

        // ================================================================ 전투

        static BattleDatabase ImportBattle(Report report, HashSet<string> nodes)
        {
            var statT = Load("Battle_Stat", report);
            var unitT = Load("Battle_Unit", report);
            var skillT = Load("Battle_Skill", report);
            var statusT = Load("Battle_Status", report);
            var typeT = Load("Battle_TypeChart", report);
            var encT = Load("Battle_Encounter", report);
            if (statT == null || unitT == null || skillT == null || statusT == null || encT == null) return null;

            var db = LoadOrCreate<BattleDatabase>(BattleAssetPath);
            db.stats.Clear();
            db.units.Clear();
            db.skills.Clear();
            db.statuses.Clear();
            db.typeChart.Clear();
            db.encounters.Clear();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ---- 스탯
            foreach (var row in statT.Rows)
            {
                string where = $"Battle_Stat.csv {row.LineNumber}행";
                var s = new StatDef { id = row["stat_id"], displayName = row["display_name"], description = row["description"], min = Int(row["min"], 0), max = Int(row["max"], 9999) };
                if (CheckId(s.id, ids, where, report)) db.stats.Add(s);
            }
            if (db.stats.Count > 0 && !unitT.HasColumn(db.stats[0].id))
                report.Warn("Battle_Unit.csv에 스탯 열이 없다. Battle_Stat.csv의 stat_id와 같은 이름의 열을 만든다.");

            // ---- 상태이상
            ids.Clear();
            foreach (var row in statusT.Rows)
            {
                string where = $"Battle_Status.csv {row.LineNumber}행";
                var s = new StatusDef
                {
                    id = row["status_id"],
                    displayName = row["display_name"],
                    duration = Int(row["duration"], 1),
                    effectsText = row["effects"],
                };
                if (!CheckId(s.id, ids, where, report)) continue;
                if (string.IsNullOrEmpty(s.displayName)) s.displayName = s.id;
                s.trigger = ParseEnum(row["trigger"], where, report, StatusTrigger.OnTurnEnd,
                    ("turn_start", StatusTrigger.OnTurnStart), ("turn_end", StatusTrigger.OnTurnEnd), ("on_hit", StatusTrigger.OnHit), ("on_act", StatusTrigger.OnAct));
                s.stack = ParseEnum(row["stack"], where, report, StackRule.Refresh,
                    ("refresh", StackRule.Refresh), ("stack", StackRule.Stack), ("ignore", StackRule.Ignore));
                s.effects = ParseEffects(s.effectsText, where, report);
                db.statuses.Add(s);
            }

            // ---- 스킬
            ids.Clear();
            foreach (var row in skillT.Rows)
            {
                string where = $"Battle_Skill.csv {row.LineNumber}행";
                var s = new SkillDef
                {
                    id = row["skill_id"],
                    displayName = row["display_name"],
                    description = row["description"],
                    type = row["type"],
                    power = Int(row["power"], 0),
                    accuracy = Int(row["accuracy"], 100),
                    costKind = row["cost_kind"],
                    costValue = Int(row["cost_value"], 0),
                    effectsText = row["effects"],
                };
                if (!CheckId(s.id, ids, where, report)) continue;
                if (string.IsNullOrEmpty(s.displayName)) s.displayName = s.id;
                s.target = ParseEnum(row["target"], where, report, TargetRule.SingleEnemy,
                    ("single_enemy", TargetRule.SingleEnemy), ("all_enemy", TargetRule.AllEnemies), ("self", TargetRule.Self),
                    ("single_ally", TargetRule.SingleAlly), ("all_ally", TargetRule.AllAllies));
                s.effects = ParseEffects(s.effectsText, where, report);
                foreach (var e in s.effects)
                    if (Bare(e.name) == "applystatus" && db.Status(e.Arg(0)) == null)
                        report.Error($"{where}: 상태이상 '{e.Arg(0)}'가 Battle_Status.csv에 없다.");
                db.skills.Add(s);
            }

            // ---- 유닛 (스탯 열은 Battle_Stat.csv의 stat_id와 같은 이름)
            ids.Clear();
            foreach (var row in unitT.Rows)
            {
                string where = $"Battle_Unit.csv {row.LineNumber}행";
                var u = new UnitDef
                {
                    id = row["unit_id"],
                    displayName = row["display_name"],
                    type = row["type"],
                    passive = row["passive"],
                    portraitId = row["portrait_id"],
                };
                if (!CheckId(u.id, ids, where, report)) continue;
                if (string.IsNullOrEmpty(u.displayName)) u.displayName = u.id;
                if (string.IsNullOrEmpty(u.portraitId)) u.portraitId = u.id;
                u.side = ParseEnum(row["side"], where, report, Side.Enemy, ("ally", Side.Ally), ("enemy", Side.Enemy));
                foreach (var stat in db.stats)
                {
                    int v = Int(row[stat.id], 0);
                    if (v < stat.min || v > stat.max) report.Warn($"{where}: {stat.id} {v}가 범위({stat.min}~{stat.max}) 밖이다.");
                    u.stats.Add(new StatValue { stat = stat.id, value = v });
                }
                for (int i = 1; i <= 4; i++)
                {
                    string sk = row["skill_" + i];
                    if (string.IsNullOrEmpty(sk)) continue;
                    if (db.Skill(sk) == null) report.Error($"{where}: 스킬 '{sk}'가 Battle_Skill.csv에 없다.");
                    else u.skills.Add(sk);
                }
                if (u.skills.Count == 0) report.Error($"{where}: 스킬이 하나도 없다.");
                db.units.Add(u);
            }

            // ---- 상성표 (행 = 공격 타입, 열 = 방어 타입, 값 = 배율)
            if (typeT != null)
            {
                var defenseTypes = typeT.Columns.Skip(1).Where(c => c.Length > 0).ToList();
                foreach (var row in typeT.Rows)
                {
                    var r = new TypeRow { attackType = row[typeT.Columns[0]] };
                    foreach (var d in defenseTypes)
                    {
                        string cell = row[d];
                        if (string.IsNullOrEmpty(cell)) continue;
                        if (!float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float m))
                        {
                            report.Error($"Battle_TypeChart.csv {row.LineNumber}행: '{cell}'은 숫자가 아니다.");
                            continue;
                        }
                        r.multipliersPercent.Add(new StatValue { stat = d, value = Mathf.RoundToInt(m * 100f) });
                    }
                    db.typeChart.Add(r);
                }
            }

            // ---- 조우
            ids.Clear();
            foreach (var row in encT.Rows)
            {
                string where = $"Battle_Encounter.csv {row.LineNumber}행";
                var enc = new EncounterDef
                {
                    id = row["encounter_id"],
                    enemies = SplitList(row["enemies"]),
                    allies = SplitList(row["allies"]),
                    background = row["background"],
                    bgm = row["bgm"],
                    winNode = row["win_node"],
                    loseNode = row["lose_node"],
                };
                if (!CheckId(enc.id, ids, where, report)) continue;
                enc.loseRule = ParseEnum(row["lose_rule"], where, report, LoseRule.Retry,
                    ("gameover", LoseRule.GameOver), ("retry", LoseRule.Retry), ("continue", LoseRule.Continue));
                if (enc.enemies.Count == 0) report.Error($"{where}: 적이 없다.");
                foreach (var id in enc.enemies.Concat(enc.allies))
                    if (db.Unit(id) == null) report.Error($"{where}: 유닛 '{id}'가 Battle_Unit.csv에 없다.");
                if (string.IsNullOrEmpty(enc.winNode)) report.Warn($"{where}: win_node가 비어 있다. 이기면 에피소드 처음으로 돌아간다.");
                CheckNode(enc.winNode, nodes, where, report);
                if (enc.loseRule == LoseRule.Continue && string.IsNullOrEmpty(enc.loseNode)) report.Warn($"{where}: lose_rule이 continue인데 lose_node가 비어 있다.");
                CheckNode(enc.loseNode, nodes, where, report);
                db.encounters.Add(enc);
            }

            EditorUtility.SetDirty(db);
            report.Info($"전투: 스탯 {db.stats.Count}, 유닛 {db.units.Count}, 스킬 {db.skills.Count}, 상태이상 {db.statuses.Count}, 조우 {db.encounters.Count}");
            return db;
        }

        static List<EffectSpec> ParseEffects(string text, string where, Report report)
        {
            var errors = new List<string>();
            var list = EffectSpec.ParseList(text, errors);
            foreach (var e in errors) report.Error($"{where}: effects {e}");
            foreach (var e in list)
                if (!KnownEffects.Contains(Bare(e.name)))
                    report.Error($"{where}: 모르는 효과 '{e.name}'. 쓸 수 있는 효과: {string.Join(", ", KnownEffects)} (앞에 self: other: 가능)");
            return list;
        }

        static string Bare(string effectName)
        {
            int colon = effectName.IndexOf(':');
            return (colon >= 0 ? effectName.Substring(colon + 1) : effectName).ToLowerInvariant();
        }

        static List<string> SplitList(string text) =>
            text.Split(new[] { ' ', '|', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        // ================================================================ 공통

        static CsvTable Load(string name, Report report)
        {
            string path = $"{DataDir}/{name}.csv";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
            {
                report.Error($"{path}가 없다.");
                return null;
            }
            return CsvTable.Parse(name, asset.text);
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static HashSet<string> LoadNodeNames(Report report)
        {
            var project = AssetDatabase.LoadAssetAtPath<YarnProject>(YarnProjectPath);
            try
            {
                return project == null ? null : new HashSet<string>(project.NodeNames);
            }
            catch (Exception)
            {
                report.Warn("Yarn Project가 컴파일되지 않아서 노드 이름을 검사하지 못했다.");
                return null;
            }
        }

        static bool CheckId(string id, HashSet<string> ids, string where, Report report)
        {
            if (string.IsNullOrEmpty(id))
            {
                report.Warn($"{where}: id가 비어 있어서 건너뛴다.");
                return false;
            }
            if (!ids.Add(id))
            {
                report.Error($"{where}: id '{id}'가 중복이다. 앞의 행을 쓴다.");
                return false;
            }
            foreach (char c in id)
                if (!(char.IsLetterOrDigit(c) && c < 128) && c != '_')
                {
                    report.Warn($"{where}: id '{id}'에 영어, 숫자, _ 말고 다른 글자가 있다.");
                    break;
                }
            return true;
        }

        static void CheckNode(string node, HashSet<string> nodes, string where, Report report)
        {
            if (string.IsNullOrEmpty(node) || nodes == null) return;
            if (!nodes.Contains(node)) report.Error($"{where}: Yarn 노드 '{node}'가 없다.");
        }

        static void CheckCondition(string condition, string where, Report report)
        {
            if (!YarnCondition.TryCheckSyntax(condition, out var error)) report.Error($"{where}: 조건 '{condition}' 오류: {error}");
        }

        static int Int(string text, int fallback) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        static bool ParseBool(string text) =>
            text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("o", StringComparison.OrdinalIgnoreCase);

        static T ParseEnum<T>(string text, string where, Report report, T fallback, params (string key, T value)[] options)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            foreach (var (key, value) in options)
                if (string.Equals(key, text, StringComparison.OrdinalIgnoreCase)) return value;
            report.Error($"{where}: '{text}'를 모른다. 쓸 수 있는 값: {string.Join(", ", options.Select(o => o.key))}");
            return fallback;
        }

        sealed class Report
        {
            readonly List<string> infos = new List<string>();
            readonly List<string> warnings = new List<string>();
            readonly List<string> errors = new List<string>();

            public void Info(string s) => infos.Add(s);
            public void Warn(string s) => warnings.Add(s);
            public void Error(string s) => errors.Add(s);

            public void Print()
            {
                foreach (var w in warnings) Debug.LogWarning("[CSV] " + w);
                foreach (var e in errors) Debug.LogError("[CSV] " + e);
                string summary = $"[CSV] 임포트 완료. {string.Join(" / ", infos)}. 경고 {warnings.Count}, 오류 {errors.Count}";
                if (errors.Count > 0) Debug.LogError(summary);
                else Debug.Log(summary);
            }
        }
    }
}
