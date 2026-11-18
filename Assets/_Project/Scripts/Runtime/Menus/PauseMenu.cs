using ProjectVita.Core;
using ProjectVita.Story;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// 일시정지 메뉴 (Story, Exploration, Battle 씬). Esc 또는 오른쪽 위 메뉴 버튼으로 연다.
    /// 열려 있는 동안 시간이 멈추고(timeScale 0) 대사 넘기기 입력을 받지 않는다. BGM은 계속 나온다.
    /// 열기 직전에 화면을 찍어 두고, 저장할 때 슬롯 썸네일로 쓴다.
    /// Esc는 위에 열린 것부터 닫는다: 슬롯 → 설정 → 대사 기록 → 메뉴.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Button openButton;
        [SerializeField] Button resumeButton;
        [SerializeField] Button backlogButton;
        [SerializeField] Button saveButton;
        [SerializeField] Button loadButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button titleButton;
        [SerializeField] Button quitButton;
        [SerializeField] SettingsPanel settings;
        [SerializeField] SaveSlotPanel slots;
        [Tooltip("대사 기록. 전투 씬에는 없다.")]
        [SerializeField] BacklogPanel backlog;

        Texture2D thumbnail;
        bool opening;

        void Awake()
        {
            panel.SetActive(false);
            openButton.onClick.AddListener(Open);
            resumeButton.onClick.AddListener(Close);
            if (backlogButton != null)
            {
                backlogButton.gameObject.SetActive(backlog != null);
                backlogButton.onClick.AddListener(() => backlog?.Open());
            }
            saveButton.onClick.AddListener(() => slots.Open(SaveSlotPanel.Mode.Save, true, thumbnail));
            loadButton.onClick.AddListener(() => slots.Open(SaveSlotPanel.Mode.Load, true, null));
            slots.Loaded += () => panel.SetActive(false); // 씬이 바뀔 때까지 일시정지는 그대로 둔다
            settingsButton.onClick.AddListener(() => settings.Open());
            titleButton.onClick.AddListener(() =>
            {
                Close();
                EpisodeFlow.GoTitle();
            });
            quitButton.onClick.AddListener(QuitGame.Quit);
        }

        void OnDestroy()
        {
            // 일시정지한 채로 씬이 바뀌어도 시간이 멈춰 있지 않게 한다.
            if (panel != null && panel.activeSelf) GamePause.Set(false);
            if (thumbnail != null) Destroy(thumbnail);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame || EpisodeFlow.IsLoading || opening) return;

            if (slots.IsOpen) slots.Close();
            else if (settings.IsOpen) settings.Close();
            else if (backlog != null && backlog.IsOpen) backlog.Close();
            else if (panel.activeSelf) Close();
            else Open();
        }

        async void Open()
        {
            if (EpisodeFlow.IsLoading || opening || panel.activeSelf) return;
            // 대사 기록을 보다가 메뉴 버튼을 누른 경우: 기록을 닫고 연다 (기록이 일시정지를 풀어 버리지 않게).
            if (backlog != null && backlog.IsOpen) backlog.Close();
            opening = true;
            GamePause.Set(true);

            // 메뉴가 뜨기 전의 화면을 썸네일로 찍는다. 에디터에서 Game 뷰가 안 그려지면 끝나지 않을 수 있어서 오래 기다리지 않는다.
            bool done = false, accept = true;
            Texture2D captured = null;
            Tween.Forget(Capture());
            float waited = 0f;
            while (!done && waited < 0.3f && this != null)
            {
                await Awaitable.NextFrameAsync();
                waited += Time.unscaledDeltaTime;
            }
            accept = false;
            if (this == null) return;

            if (thumbnail != null) Destroy(thumbnail);
            thumbnail = captured;
            opening = false;
            saveButton.interactable = SaveSystem.CanSaveNow;
            loadButton.interactable = SaveSystem.HasAnySave;
            panel.SetActive(true);

            async Awaitable Capture()
            {
                var tex = await ScreenThumbnail.CaptureAsync();
                if (accept && this != null) captured = tex;
                else if (tex != null) Destroy(tex);
                done = true;
            }
        }

        void Close()
        {
            if (slots.IsOpen) slots.Close();
            if (settings.IsOpen) settings.Close();
            if (backlog != null && backlog.IsOpen) backlog.Close();
            panel.SetActive(false);
            GamePause.Set(false);
        }
    }

    public static class QuitGame
    {
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
