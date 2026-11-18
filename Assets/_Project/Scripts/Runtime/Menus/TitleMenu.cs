using ProjectVita.Core;
using ProjectVita.Story;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// 타이틀: 처음부터, 이어하기(자동 저장), 불러오기(슬롯), 설정, 종료, 에피소드 선택(개발용).
    /// 에피소드 버튼은 Episodes.csv의 행마다 실행 중에 만든다. CSV에 행을 추가하면 버튼도 늘어난다.
    /// </summary>
    public sealed class TitleMenu : MonoBehaviour
    {
        [SerializeField] TextAsset episodesCsv;
        [SerializeField] Button startButton;
        [SerializeField] Button continueButton;
        [SerializeField] Button loadButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button quitButton;
        [SerializeField] SettingsPanel settings;
        [SerializeField] SaveSlotPanel slots;

        [Header("에피소드 선택 (Development Build에서만 보인다)")]
        [SerializeField] GameObject episodePanel;
        [SerializeField] RectTransform episodeListRoot;
        [SerializeField] Button episodeButtonTemplate;

        void Start()
        {
            var table = EpisodeFlow.EnsureTable(episodesCsv);

            // 타이틀에서는 이야기의 BGM을 끈다.
            AudioManager.Instance.StopBgm(1f);

            var first = table.First;
            startButton.interactable = first != null;
            startButton.onClick.AddListener(() => EpisodeFlow.StartNewGame(first.Id));
            continueButton.interactable = SaveSystem.HasAutoSave;
            continueButton.onClick.AddListener(() => EpisodeFlow.Continue());
            loadButton.interactable = SaveSystem.HasAnySave;
            loadButton.onClick.AddListener(() => slots.Open(SaveSlotPanel.Mode.Load, false, null));
            settingsButton.onClick.AddListener(() => settings.Open());
            quitButton.onClick.AddListener(QuitGame.Quit);

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

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
            if (slots.IsOpen) slots.Close();
            else if (settings.IsOpen) settings.Close();
        }
    }
}
