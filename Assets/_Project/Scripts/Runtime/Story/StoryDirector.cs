using System;
using ProjectVita.Core;
using UnityEngine;
using Yarn.Unity;

namespace ProjectVita.Story
{
    /// <summary>
    /// Story 씬(또는 unity_scene으로 지정한 전용 씬)에서 현재 에피소드의 start_node를 실행한다.
    /// 대사가 끝나면 next_episode로, 비어 있으면 타이틀로 간다.
    /// 이어하기로 들어왔으면 저장한 노드부터, 저장한 무대와 BGM으로 시작한다.
    /// 노드에 들어갈 때마다 자동 저장한다.
    /// 에피소드 전용 씬을 만들 때는 Story 씬을 복제하고, fallbackEpisodeId만 바꾼다.
    /// </summary>
    public sealed class StoryDirector : MonoBehaviour
    {
        [SerializeField] DialogueRunner runner;
        [SerializeField] StoryStage stage;
        [SerializeField] TextAsset episodesCsv;

        [Tooltip("타이틀을 거치지 않고 이 씬을 바로 플레이했을 때 시작할 episode_id. 비우면 Episodes.csv의 첫 에피소드.")]
        [SerializeField] string fallbackEpisodeId = "";

        Episode episode;

        void OnEnable() => StoryPresenter.NodeEntered += OnNodeEntered;
        void OnDisable() => StoryPresenter.NodeEntered -= OnNodeEntered;

        async void Start()
        {
            var table = EpisodeFlow.EnsureTable(episodesCsv);

            // 에디터의 대사 테스트 러너가 노드를 골라 플레이를 시작했으면 그 노드로 다시 연다.
            if (DialogueTest.TryRunEditorRequest()) return;

            episode = EpisodeFlow.Current;
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

            if (!CanRun()) return;

            string startNode = episode.StartNode;
            var resume = EpisodeFlow.TakePendingResume(episode.Id);
            if (resume != null)
            {
                if (HasNode(resume.node)) startNode = resume.node;
                else Debug.LogWarning($"[Story] 세이브의 노드 '{resume.node}'가 Yarn에 없어서 에피소드 처음부터 시작한다.");
                if (stage != null) stage.ApplyState(resume.stage);
                AudioManager.Instance.PlayBgm(resume.bgm, 0.5f);
                Debug.Log($"[Story] 이어하기: {episode.Id} / {startNode} (저장 {resume.savedAt})");
            }

            // 전투에서 돌아왔으면 승리/패배 노드부터, 대사 테스트면 고른 노드부터 시작한다.
            string pendingNode = EpisodeFlow.TakePendingNode();
            if (!string.IsNullOrEmpty(pendingNode))
            {
                if (HasNode(pendingNode)) startNode = pendingNode;
                else Fail($"시작할 노드 '{pendingNode}'가 Yarn에 없다 (전투 결과면 Battle_Encounter.csv 확인).");
            }

            if (!HasNode(startNode))
            {
                Fail($"start_node '{startNode}' 노드가 Yarn에 없다 (Episodes.csv {episode.LineNumber}행, 에피소드 {episode.Id}).");
                return;
            }

            // 변수는 씬 밖 저장소에 둔다. Dialogue가 처음 만들어지기(RunDialogue) 전에 바꿔야 한다.
            var storage = GameVariables.Storage;
            storage.Program = runner.YarnProject.Program;
            runner.VariableStorage = storage;

            bool finished = false;
            try
            {
                await runner.RunDialogue(startNode);
                finished = true;
            }
            catch (OperationCanceledException)
            {
                // <<episode>>로 씬이 바뀌면서 취소됐다. 정상이다.
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                Fail($"{startNode} 실행 중 오류. Console 확인");
                return;
            }

            // 이 씬이 이미 사라졌거나, <<episode>>로 다른 씬을 로드하는 중이면 여기서 끝낸다.
            if (!finished || this == null || EpisodeFlow.IsLoading) return;

            if (string.IsNullOrEmpty(episode.NextEpisode))
                EpisodeFlow.GoTitle();
            else if (!EpisodeFlow.TryGo(episode.NextEpisode))
                Fail($"next_episode '{episode.NextEpisode}'로 이동 실패 (Episodes.csv {episode.LineNumber}행)");
        }

        void OnNodeEntered(string nodeName)
        {
            if (episode == null) return;
            SaveSystem.AutoSave(episode.Id, nodeName, stage != null ? stage.GetState() : null, AudioManager.Instance.CurrentBgm);
        }

        bool CanRun()
        {
            if (runner == null)
                return Fail("StoryDirector에 DialogueRunner가 연결되지 않았다. 씬 생성 메뉴를 다시 실행한다.");

            var project = runner.YarnProject;
            if (project == null)
                return Fail("DialogueRunner에 Yarn Project가 연결되지 않았다. 씬 생성 메뉴를 다시 실행한다.");

            if (project.Program == null)
                return Fail("Yarn 스크립트에 오류가 있어서 컴파일되지 않았다. Console에서 .yarn 오류를 확인한다.");

            return true;
        }

        bool HasNode(string node) =>
            !string.IsNullOrEmpty(node) && Array.IndexOf(runner.YarnProject.NodeNames, node) >= 0;

        bool Fail(string message)
        {
            Debug.LogError("[Story] " + message, this);
            DevOverlay.SetStatus(message);
            return false;
        }
    }
}
