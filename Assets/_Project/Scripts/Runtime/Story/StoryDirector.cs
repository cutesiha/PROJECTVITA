using System;
using UnityEngine;
using Yarn.Unity;

namespace ProjectVita.Story
{
    /// <summary>
    /// Story 씬(또는 unity_scene으로 지정한 전용 씬)에서 현재 에피소드의 start_node를 실행한다.
    /// 대사가 끝나면 next_episode로, 비어 있으면 타이틀로 간다.
    /// 에피소드 전용 씬을 만들 때는 Story 씬을 복제하고, fallbackEpisodeId만 바꾼다.
    /// </summary>
    public sealed class StoryDirector : MonoBehaviour
    {
        [SerializeField] DialogueRunner runner;
        [SerializeField] TextAsset episodesCsv;

        [Tooltip("타이틀을 거치지 않고 이 씬을 바로 플레이했을 때 시작할 episode_id. 비우면 Episodes.csv의 첫 에피소드.")]
        [SerializeField] string fallbackEpisodeId = "";

        async void Start()
        {
            var table = EpisodeFlow.EnsureTable(episodesCsv);

            var episode = EpisodeFlow.Current;
            if (episode == null)
            {
                // 에디터에서 이 씬을 바로 플레이한 경우
                episode = table.Find(fallbackEpisodeId) ?? table.First;
                if (episode == null)
                {
                    Fail("Episodes.csv에 에피소드가 하나도 없다.");
                    return;
                }
                EpisodeFlow.SetCurrentWithoutLoading(episode);
            }
            DevOverlay.SetEpisode(episode);

            if (!CanRun(episode)) return;

            // 변수는 씬 밖 저장소에 둔다. Dialogue가 처음 만들어지기(RunDialogue) 전에 바꿔야 한다.
            var storage = GameVariables.Storage;
            storage.Program = runner.YarnProject.Program;
            runner.VariableStorage = storage;

            bool finished = false;
            try
            {
                await runner.RunDialogue(episode.StartNode);
                finished = true;
            }
            catch (OperationCanceledException)
            {
                // <<episode>>로 씬이 바뀌면서 취소됐다. 정상이다.
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                Fail($"{episode.StartNode} 실행 중 오류. Console 확인");
                return;
            }

            // 이 씬이 이미 사라졌거나, <<episode>>로 다른 씬을 로드하는 중이면 여기서 끝낸다.
            if (!finished || this == null || EpisodeFlow.IsLoading) return;

            if (string.IsNullOrEmpty(episode.NextEpisode))
                EpisodeFlow.GoTitle();
            else if (!EpisodeFlow.TryGo(episode.NextEpisode))
                Fail($"next_episode '{episode.NextEpisode}'로 이동 실패 (Episodes.csv {episode.LineNumber}행)");
        }

        bool CanRun(Episode episode)
        {
            if (runner == null)
                return Fail("StoryDirector에 DialogueRunner가 연결되지 않았다. 씬 생성 메뉴를 다시 실행한다.");

            var project = runner.YarnProject;
            if (project == null)
                return Fail("DialogueRunner에 Yarn Project가 연결되지 않았다. 씬 생성 메뉴를 다시 실행한다.");

            if (project.Program == null)
                return Fail("Yarn 스크립트에 오류가 있어서 컴파일되지 않았다. Console에서 .yarn 오류를 확인한다.");

            if (string.IsNullOrEmpty(episode.StartNode) || Array.IndexOf(project.NodeNames, episode.StartNode) < 0)
                return Fail($"start_node '{episode.StartNode}' 노드가 Yarn에 없다 (Episodes.csv {episode.LineNumber}행, 에피소드 {episode.Id}).");

            return true;
        }

        bool Fail(string message)
        {
            Debug.LogError("[Story] " + message, this);
            DevOverlay.SetStatus(message);
            return false;
        }
    }
}
