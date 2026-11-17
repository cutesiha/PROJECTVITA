using ProjectVita.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectVita.Story
{
    /// <summary>
    /// 에피소드 진행 상태와 씬 전환을 맡는다. 씬이 바뀌어도 유지되는 정적 상태다.
    /// 에피소드를 시작할 때는 항상 씬을 새로 로드한다 (Story → Story도 다시 로드).
    /// 그러면 DialogueRunner를 중간에 멈추는 처리가 필요 없고, 화면 상태도 에피소드마다 깨끗해진다.
    /// 씬 전환은 ScreenFader로 검게 덮었다가 걷어 낸다.
    /// </summary>
    public static class EpisodeFlow
    {
        public const string TitleSceneName = "Title";
        public const string StorySceneName = "Story";

        public static EpisodeTable Table { get; private set; }

        /// <summary>지금 진행 중인(또는 로드 중인) 에피소드. 타이틀에서는 null.</summary>
        public static Episode Current { get; private set; }

        /// <summary>씬 로드를 요청한 뒤 새 씬이 뜨기 전까지 true. 이 동안 끝난 대사는 다음 에피소드로 넘기지 않는다.</summary>
        public static bool IsLoading { get; private set; }

        /// <summary>이어하기로 불러온 세이브. 다음 씬의 StoryDirector가 한 번 꺼내 쓴다.</summary>
        static SaveSystem.SaveData pendingResume;

        // Enter Play Mode 옵션으로 도메인 리로드를 꺼도 정적 상태가 남지 않게 한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Table = null;
            Current = null;
            IsLoading = false;
            pendingResume = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            IsLoading = false;
            GamePause.Set(false);
            Tween.Forget(ScreenFader.Instance.FadeIn());
        }

        /// <summary>CSV를 아직 읽지 않았으면 읽는다. 씬마다 TextAsset을 들고 있다가 호출한다.</summary>
        public static EpisodeTable EnsureTable(TextAsset csv)
        {
            if (Table == null) Table = EpisodeTable.Parse(csv);
            return Table;
        }

        /// <summary>씬을 거치지 않고 플레이를 시작했을 때(에디터에서 Story 씬을 바로 실행 등) 현재 에피소드를 정한다.</summary>
        public static void SetCurrentWithoutLoading(Episode episode) => Current = episode;

        /// <summary>타이틀에서 새 게임으로 시작한다. Yarn 변수를 모두 지운 뒤 이동한다.</summary>
        public static bool StartNewGame(string episodeId)
        {
            if (IsLoading) return false;
            GameVariables.Clear();
            pendingResume = null;
            return TryGo(episodeId);
        }

        /// <summary>자동 저장에서 이어한다. 변수를 되돌리고, 저장한 노드부터 시작한다.</summary>
        public static bool Continue()
        {
            if (IsLoading) return false;
            var data = SaveSystem.LoadAutoSave();
            if (data == null) return false;
            if (Table?.Find(data.episodeId) == null)
            {
                Debug.LogError($"[Episode] 세이브의 에피소드 '{data.episodeId}'가 Episodes.csv에 없다.");
                return false;
            }
            GameVariables.Clear();
            SaveSystem.RestoreVariables(data);
            pendingResume = data;
            return TryGo(data.episodeId);
        }

        /// <summary>이 에피소드를 이어하기로 시작해야 하면 세이브를 돌려준다 (한 번만).</summary>
        public static SaveSystem.SaveData TakePendingResume(string episodeId)
        {
            var data = pendingResume;
            pendingResume = null;
            return data != null && data.episodeId == episodeId ? data : null;
        }

        /// <summary>에피소드로 이동한다. Yarn 변수는 유지된다. 실패하면 false (에러 로그를 남긴다).</summary>
        public static bool TryGo(string episodeId)
        {
            if (Table == null)
            {
                Debug.LogError("[Episode] 에피소드 표를 아직 읽지 않았다.");
                return false;
            }

            var episode = Table.Find(episodeId);
            if (episode == null)
            {
                Debug.LogError($"[Episode] Episodes.csv에 '{episodeId}' 에피소드가 없다.");
                return false;
            }

            if (!LoadScene(episode.SceneName)) return false;
            Current = episode;
            Debug.Log($"[Episode] → {episode} / 씬 {episode.SceneName} / 시작 노드 {episode.StartNode}");
            return true;
        }

        public static void GoTitle()
        {
            if (LoadScene(TitleSceneName))
            {
                Current = null;
                Debug.Log("[Episode] → 타이틀");
            }
        }

        static bool LoadScene(string sceneName)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"[Episode] 이미 씬을 로드하는 중이라 '{sceneName}' 요청을 무시한다.");
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[Episode] '{sceneName}' 씬이 Build Settings에 없다. unity_scene 열의 씬 이름을 확인한다.");
                return false;
            }
            IsLoading = true;
            Tween.Forget(FadeAndLoad(sceneName));
            return true;
        }

        static async Awaitable FadeAndLoad(string sceneName)
        {
            await ScreenFader.Instance.FadeOut();
            SceneManager.LoadScene(sceneName);
        }
    }
}
