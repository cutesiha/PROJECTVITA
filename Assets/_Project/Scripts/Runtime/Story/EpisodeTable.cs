using System.Collections.Generic;
using ProjectVita.Core;
using UnityEngine;

namespace ProjectVita.Story
{
    /// <summary>Episodes.csv의 한 행.</summary>
    public sealed class Episode
    {
        public string Id;
        public string DisplayName;
        /// <summary>비어 있으면 기본 Story 씬을 쓴다.</summary>
        public string UnityScene;
        public string StartNode;
        /// <summary>대사가 끝나면 이어질 episode_id. 비어 있으면 타이틀로 돌아간다.</summary>
        public string NextEpisode;
        public int LineNumber;

        public string SceneName => string.IsNullOrEmpty(UnityScene) ? EpisodeFlow.StorySceneName : UnityScene;

        public override string ToString() => $"{Id} ({DisplayName})";
    }

    /// <summary>
    /// Assets/_Project/Data/Episodes.csv를 읽는다.
    /// 컬럼: episode_id, display_name, unity_scene, start_node, next_episode
    /// </summary>
    public sealed class EpisodeTable
    {
        public const string ColId = "episode_id";
        public const string ColDisplayName = "display_name";
        public const string ColUnityScene = "unity_scene";
        public const string ColStartNode = "start_node";
        public const string ColNextEpisode = "next_episode";

        public readonly List<Episode> Episodes = new List<Episode>();
        readonly Dictionary<string, Episode> byId = new Dictionary<string, Episode>();

        /// <summary>표의 첫 에피소드. 타이틀의 "처음부터"가 여기서 시작한다.</summary>
        public Episode First => Episodes.Count > 0 ? Episodes[0] : null;

        public Episode Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            byId.TryGetValue(id.Trim(), out var ep);
            return ep;
        }

        /// <summary>
        /// 표에 없는 임시 에피소드를 이번 실행 동안만 등록한다 (대사 테스트용). 타이틀의 에피소드 목록에는 나오지 않는다.
        /// </summary>
        public Episode RegisterTemporary(string id, string displayName, string sceneName)
        {
            if (byId.TryGetValue(id, out var existing)) return existing;
            var ep = new Episode { Id = id, DisplayName = displayName, UnityScene = sceneName, StartNode = "", NextEpisode = "", LineNumber = 0 };
            byId.Add(id, ep);
            return ep;
        }

        public static EpisodeTable Parse(TextAsset csv)
        {
            var table = new EpisodeTable();
            if (csv == null)
            {
                Debug.LogError("[Episode] Episodes.csv가 연결되지 않았다. Tools > Project Vita > 2단계 대사 씬 생성을 다시 실행한다.");
                return table;
            }

            var csvTable = CsvTable.Parse(csv.name, csv.text);
            foreach (var col in new[] { ColId, ColStartNode })
                if (!csvTable.HasColumn(col))
                    Debug.LogError($"[Episode] {csv.name}.csv에 '{col}' 컬럼이 없다.");

            foreach (var row in csvTable.Rows)
            {
                var ep = new Episode
                {
                    Id = row[ColId],
                    DisplayName = row[ColDisplayName],
                    UnityScene = row[ColUnityScene],
                    StartNode = row[ColStartNode],
                    NextEpisode = row[ColNextEpisode],
                    LineNumber = row.LineNumber,
                };
                if (string.IsNullOrEmpty(ep.Id))
                {
                    Debug.LogWarning($"[Episode] {csv.name}.csv {row.LineNumber}행: episode_id가 비어 있어 건너뛴다.");
                    continue;
                }
                if (table.byId.ContainsKey(ep.Id))
                {
                    Debug.LogWarning($"[Episode] {csv.name}.csv {row.LineNumber}행: episode_id '{ep.Id}'가 중복이다. 앞의 행을 쓴다.");
                    continue;
                }
                if (string.IsNullOrEmpty(ep.DisplayName)) ep.DisplayName = ep.Id;
                table.Episodes.Add(ep);
                table.byId.Add(ep.Id, ep);
            }
            return table;
        }
    }
}
