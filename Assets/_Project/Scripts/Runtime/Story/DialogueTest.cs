using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectVita.Story
{
    /// <summary>
    /// 개발용 대사 테스트 (문서 4-7).
    /// - 노드부터 바로 플레이: 에디터의 Tools > Project Vita > 대사 테스트 러너가 부른다. 변수는 새 게임처럼 비우고 "시작 변수"를 넣는다.
    /// - 핫리로드: 플레이 중에 .yarn이 바뀌면(에디터가 알려 줌) 또는 F5를 누르면, 마지막 저장 시점(노드 시작 또는 탐색 장소)부터 다시 연다.
    ///   씬을 다시 로드하므로 새로 컴파일된 Yarn이 그대로 들어온다.
    /// </summary>
    public static class DialogueTest
    {
        // 에디터가 플레이를 시작하기 전에 SessionState에 넣는 값
        public const string EditorNodeKey = "ProjectVita.TestNode";
        public const string EditorVarsKey = "ProjectVita.TestVars";

        const string TempEpisodeId = "_NodeTest";

        static string lastNode, lastVariables;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => lastNode = lastVariables = null;

        /// <summary>노드부터 테스트 플레이한다. 그 노드가 속한 에피소드(이름 규칙으로 추측)의 씬에서 실행한다.</summary>
        public static bool Run(string node, string variables)
        {
            var table = EpisodeFlow.Table;
            if (table == null)
            {
                Debug.LogError("[테스트] 에피소드 표를 아직 읽지 않았다. Title이나 Story 씬에서 플레이 중이어야 한다.");
                return false;
            }
            if (EpisodeFlow.IsLoading) return false;

            lastNode = node;
            lastVariables = variables;
            GameVariables.Clear();
            ApplyVariables(variables);
            DialogueModes.SetSkip(false);
            return EpisodeFlow.StartAtNode(GuessEpisode(table, node), node);
        }

        /// <summary>에디터에서 노드를 골라 플레이를 시작했으면 그 노드로 연다. 처리했으면 true (호출한 씬은 아무것도 하지 않는다).</summary>
        public static bool TryRunEditorRequest()
        {
#if UNITY_EDITOR
            string node = UnityEditor.SessionState.GetString(EditorNodeKey, "");
            if (string.IsNullOrEmpty(node)) return false;
            string vars = UnityEditor.SessionState.GetString(EditorVarsKey, "");
            UnityEditor.SessionState.EraseString(EditorNodeKey);
            UnityEditor.SessionState.EraseString(EditorVarsKey);
            return Run(node, vars);
#else
            return false;
#endif
        }

        /// <summary>
        /// 지금 장면을 마지막 저장 시점부터 다시 연다 (F5, .yarn 변경). 타이틀과 전투에서는 하지 않는다.
        /// </summary>
        public static bool ReloadCurrent(string reason)
        {
            if (EpisodeFlow.IsLoading) return false;
            string scene = SceneManager.GetActiveScene().name;
            if (scene == EpisodeFlow.TitleSceneName || scene == EpisodeFlow.BattleSceneName)
            {
                Debug.Log($"[핫리로드] {reason}. {scene} 화면이라 다시 시작하지 않는다. 다음 대사부터 새 내용이 나온다.");
                return false;
            }

            var snapshot = SaveSystem.LastSnapshot;
            if (snapshot != null)
            {
                string where = !string.IsNullOrEmpty(snapshot.node) ? snapshot.node
                    : !string.IsNullOrEmpty(snapshot.location) ? $"장소 {snapshot.location}" : "탐색 지도";
                Debug.Log($"[핫리로드] {reason} → {where} 처음부터 다시 시작");
                DevOverlay.SetStatus($"{reason}: {where} 다시 시작");
                return EpisodeFlow.Resume(snapshot);
            }
            if (!string.IsNullOrEmpty(lastNode))
            {
                Debug.Log($"[핫리로드] {reason} → 테스트 노드 {lastNode} 다시 시작");
                return Run(lastNode, lastVariables);
            }
            Debug.Log($"[핫리로드] {reason}. 다시 시작할 저장 시점이 없다.");
            return false;
        }

        /// <summary>
        /// "$이름 = 값"을 줄마다 (또는 ;로 구분) 읽어 변수에 넣는다. 값: true/false, 숫자, 그 밖은 문자열 ("따옴표" 생략 가능).
        /// </summary>
        public static void ApplyVariables(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var storage = GameVariables.Storage;
            foreach (var raw in text.Split('\n', ';'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    Debug.LogWarning($"[테스트] 시작 변수 '{line}'를 읽지 못했다. '$이름 = 값' 형식으로 쓴다.");
                    continue;
                }
                string name = line.Substring(0, eq).Trim();
                if (!name.StartsWith("$", StringComparison.Ordinal)) name = "$" + name;
                string value = line.Substring(eq + 1).Trim();
                try
                {
                    if (bool.TryParse(value, out bool b)) storage.SetValue(name, b);
                    else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) storage.SetValue(name, f);
                    else storage.SetValue(name, value.Trim('"'));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[테스트] 시작 변수 {name} = {value}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 노드가 속한 에피소드를 고른다: start_node가 같은 에피소드 → 노드 이름이 "에피소드id_"로 시작하는 에피소드(가장 긴 것) → 임시 에피소드.
        /// 임시 에피소드는 Story 씬에서 실행하고, 대사가 끝나면 타이틀로 간다.
        /// </summary>
        public static Episode GuessEpisode(EpisodeTable table, string node)
        {
            foreach (var ep in table.Episodes)
                if (ep.StartNode == node) return ep;

            Episode best = null;
            foreach (var ep in table.Episodes)
                if (node.StartsWith(ep.Id + "_", StringComparison.Ordinal) && (best == null || ep.Id.Length > best.Id.Length))
                    best = ep;
            return best ?? table.RegisterTemporary(TempEpisodeId, "노드 테스트", "");
        }
    }
}
