using ProjectVita.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Story
{
    /// <summary>
    /// 대사창 오른쪽 위의 로그 / 오토 / 스킵 버튼과 단축키.
    /// L 또는 휠 위로: 대사 기록, A: 오토, Tab: 스킵 (읽은 대사만), Ctrl 누르는 동안: 빨리감기 (StoryPresenter가 처리).
    /// 대사창의 자식이라 대사창이 보일 때만 동작한다.
    /// </summary>
    public sealed class DialogueQuickMenu : MonoBehaviour
    {
        [SerializeField] Button logButton;
        [SerializeField] Button autoButton;
        [SerializeField] Button skipButton;
        [SerializeField] BacklogPanel backlog;
        [SerializeField] Color onColor = new Color(0.70f, 0.15f, 0.12f);

        ColorBlock autoColors, skipColors;

        void Awake()
        {
            autoColors = autoButton.colors;
            skipColors = skipButton.colors;
            logButton.onClick.AddListener(() => { Deselect(); OpenLog(); });
            autoButton.onClick.AddListener(() => { Deselect(); DialogueModes.SetAuto(!DialogueModes.Auto); });
            skipButton.onClick.AddListener(() => { Deselect(); DialogueModes.SetSkip(!DialogueModes.Skip); });
        }

        void OnEnable()
        {
            DialogueModes.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => DialogueModes.Changed -= Refresh;

        void Update()
        {
            if (GamePause.IsPaused || EpisodeFlow.IsLoading) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.lKey.wasPressedThisFrame) OpenLog();
                else if (kb.aKey.wasPressedThisFrame) DialogueModes.SetAuto(!DialogueModes.Auto);
                else if (kb.tabKey.wasPressedThisFrame) DialogueModes.SetSkip(!DialogueModes.Skip);
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.scroll.ReadValue().y > 0f) OpenLog();
        }

        void OpenLog()
        {
            if (backlog != null) backlog.Open();
        }

        void Refresh()
        {
            Paint(autoButton, autoColors, DialogueModes.Auto);
            Paint(skipButton, skipColors, DialogueModes.Skip);
        }

        void Paint(Button button, ColorBlock original, bool on)
        {
            var c = original;
            if (on)
            {
                c.normalColor = onColor;
                c.selectedColor = onColor;
            }
            button.colors = c;
        }

        /// <summary>
        /// 버튼을 누른 뒤 선택 상태로 남으면 Space/Enter(대사 넘기기)가 그 버튼까지 눌러 버린다. 선택을 푼다.
        /// </summary>
        static void Deselect() => EventSystem.current?.SetSelectedGameObject(null);
    }
}
