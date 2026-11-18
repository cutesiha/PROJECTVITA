using ProjectVita.Core;
using ProjectVita.Story;
using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// 타이틀: 처음부터, 이어하기(자동 저장), 불러오기(슬롯), 설정, 종료, 에피소드 선택(개발용).
    /// 화면이 천천히 밝아지고 메뉴가 위에서부터 차례로 나타난다.
    /// 에피소드 버튼은 Episodes.csv의 행마다 실행 중에 만든다. CSV에 행을 추가하면 버튼도 늘어난다.
    /// </summary>
    public sealed class TitleMenu : MonoBehaviour
    {
        [SerializeField] TextAsset episodesCsv;
        [SerializeField] CanvasGroup intro;
        [Tooltip("로고와 메뉴 묶음. 개발 빌드에서는 오른쪽 에피소드 목록과 겹치지 않게 왼쪽으로 민다")]
        [SerializeField] RectTransform centerGroup;
        [SerializeField] RectTransform menuRoot;
        [SerializeField] Button startButton;
        [SerializeField] Button continueButton;
        [SerializeField] Button loadButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button quitButton;
        [SerializeField] SettingsPanel settings;
        [SerializeField] SaveSlotPanel slots;
        [SerializeField] ConfirmDialog confirm;

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

            // 누를 수 없는 메뉴(이어하기, 불러오기)는 글자를 흐리게
            foreach (var b in new[] { startButton, continueButton, loadButton })
            {
                var label = b.GetComponentInChildren<TMP_Text>(true);
                if (!b.interactable && label != null) label.color = label.color.WithAlpha(0.3f);
            }

            episodeButtonTemplate.gameObject.SetActive(false);
            episodePanel.SetActive(Debug.isDebugBuild);
            if (Debug.isDebugBuild && centerGroup != null) centerGroup.anchoredPosition += new Vector2(-230f, 0f);
            if (Debug.isDebugBuild)
            {
                foreach (var ep in table.Episodes)
                {
                    var button = Instantiate(episodeButtonTemplate, episodeListRoot);
                    button.gameObject.SetActive(true);
                    button.name = "Episode_" + ep.Id;
                    var label = button.GetComponentInChildren<TMP_Text>(true);
                    label.text = $"{ep.DisplayName}\n<size=62%><color={UITheme.ToHex(UITheme.TextDim)}>{ep.Id} · {ep.StartNode}</color></size>";
                    string id = ep.Id;
                    button.onClick.AddListener(() => EpisodeFlow.StartNewGame(id));
                }
            }

            // 등장: 전체가 천천히 밝아지고, 메뉴가 차례로 나타난다.
            if (intro != null)
            {
                intro.alpha = 0f;
                Tween.Forget(UIAnim.Run(0.6f, t => intro.alpha = UIAnim.EaseOut(t), destroyCancellationToken));
            }
            if (menuRoot != null) UIPanelAnim.Stagger(menuRoot, destroyCancellationToken, 0.35f, 0.07f, 0.35f);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
            if (confirm != null && confirm.IsOpen) confirm.Cancel();
            else if (slots.IsOpen) slots.Close();
            else if (settings.IsOpen) settings.Close();
        }
    }
}
