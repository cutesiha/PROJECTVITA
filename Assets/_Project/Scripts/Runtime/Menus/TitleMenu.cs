using ProjectVita.Story;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// 4-2단계 최소 타이틀: 처음부터, 에피소드 선택(개발용), 종료.
    /// 에피소드 버튼은 Episodes.csv의 행마다 실행 중에 만든다. CSV에 행을 추가하면 버튼도 늘어난다.
    /// TODO(4-4): 이어하기, 설정, 디자인.
    /// </summary>
    public sealed class TitleMenu : MonoBehaviour
    {
        [SerializeField] TextAsset episodesCsv;
        [SerializeField] Button startButton;
        [SerializeField] Button quitButton;

        [Header("에피소드 선택 (Development Build에서만 보인다)")]
        [SerializeField] GameObject episodePanel;
        [SerializeField] RectTransform episodeListRoot;
        [SerializeField] Button episodeButtonTemplate;

        void Start()
        {
            var table = EpisodeFlow.EnsureTable(episodesCsv);

            var first = table.First;
            startButton.interactable = first != null;
            startButton.onClick.AddListener(() => EpisodeFlow.StartNewGame(first.Id));
            quitButton.onClick.AddListener(Quit);

            episodeButtonTemplate.gameObject.SetActive(false);
            episodePanel.SetActive(Debug.isDebugBuild);
            if (!Debug.isDebugBuild) return;

            foreach (var ep in table.Episodes)
            {
                var button = Instantiate(episodeButtonTemplate, episodeListRoot);
                button.gameObject.SetActive(true);
                button.name = "Episode_" + ep.Id;
                var label = button.GetComponentInChildren<TMP_Text>(true);
                label.text = $"{ep.DisplayName}\n<size=65%><color=#9AA0A6>{ep.Id} · {ep.StartNode}</color></size>";
                string id = ep.Id;
                button.onClick.AddListener(() => EpisodeFlow.StartNewGame(id));
            }
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
