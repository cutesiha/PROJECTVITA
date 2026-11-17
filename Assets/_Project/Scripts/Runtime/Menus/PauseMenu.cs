using ProjectVita.Core;
using ProjectVita.Story;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// Story 씬의 일시정지 메뉴. Esc 또는 오른쪽 위 메뉴 버튼으로 연다.
    /// 열려 있는 동안 시간이 멈추고(timeScale 0) 대사 넘기기 입력을 받지 않는다. BGM은 계속 나온다.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Button openButton;
        [SerializeField] Button resumeButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button titleButton;
        [SerializeField] Button quitButton;
        [SerializeField] SettingsPanel settings;

        void Awake()
        {
            panel.SetActive(false);
            openButton.onClick.AddListener(Open);
            resumeButton.onClick.AddListener(Close);
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
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame || EpisodeFlow.IsLoading) return;

            if (settings.IsOpen) settings.Close();
            else if (panel.activeSelf) Close();
            else Open();
        }

        void Open()
        {
            if (EpisodeFlow.IsLoading) return;
            panel.SetActive(true);
            GamePause.Set(true);
        }

        void Close()
        {
            if (settings.IsOpen) settings.Close();
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
