using ProjectVita.Core;
using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Story
{
    /// <summary>
    /// 오른쪽 위 AUTO / SKIP / LOG 버튼과 단축키 (MENU 버튼은 PauseMenu가 맡는다).
    /// L 또는 휠 위로: 대사 기록, A: 오토, Tab: 스킵, Ctrl 누르는 동안: 빨리감기 (StoryPresenter가 처리).
    /// 대사창이 떠 있을 때만 버튼이 보이고 단축키가 동작한다. 켜진 버튼은 시안색이 된다.
    /// </summary>
    public sealed class DialogueQuickMenu : MonoBehaviour
    {
        [SerializeField] Button logButton;
        [SerializeField] Button autoButton;
        [SerializeField] Button skipButton;
        [SerializeField] BacklogPanel backlog;
        [Tooltip("대사창. 떠 있을 때만 버튼을 보여 준다")]
        [SerializeField] GameObject dialogueBox;
        [Tooltip("AUTO/SKIP/LOG 버튼 묶음")]
        [SerializeField] CanvasGroup dialogueGroup;

        void Awake()
        {
            logButton.onClick.AddListener(() => { Deselect(); OpenLog(); });
            autoButton.onClick.AddListener(() => { Deselect(); DialogueModes.SetAuto(!DialogueModes.Auto); });
            skipButton.onClick.AddListener(() => { Deselect(); DialogueModes.SetSkip(!DialogueModes.Skip); });
            if (dialogueGroup != null) dialogueGroup.alpha = 0f;
        }

        void OnEnable()
        {
            DialogueModes.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => DialogueModes.Changed -= Refresh;

        bool DialogueActive => dialogueBox == null || dialogueBox.activeSelf;

        void Update()
        {
            bool active = DialogueActive;
            if (dialogueGroup != null)
            {
                dialogueGroup.alpha = Mathf.MoveTowards(dialogueGroup.alpha, active ? 1f : 0f, Time.unscaledDeltaTime / 0.25f);
                dialogueGroup.interactable = dialogueGroup.blocksRaycasts = active;
            }

            if (!active || GamePause.IsPaused || EpisodeFlow.IsLoading) return;

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
            Paint(autoButton, DialogueModes.Auto);
            Paint(skipButton, DialogueModes.Skip);
        }

        /// <summary>켜진 버튼: 바탕은 옅은 시안, 테두리와 아이콘, 글자는 시안.</summary>
        static void Paint(Button button, bool on)
        {
            // 바탕 색은 버튼의 상태 색(ColorBlock)이 정한다. 바탕 그림은 흰색이다.
            var colors = button.colors;
            colors.normalColor = colors.selectedColor = on ? new Color32(18, 44, 66, 244) : UITheme.Panel;
            colors.highlightedColor = on ? new Color32(24, 58, 86, 248) : UITheme.PanelHover;
            button.colors = colors;
            var line = button.transform.Find("Content/Line")?.GetComponent<Image>();
            if (line != null) line.color = on ? UITheme.CyanLine : UITheme.LineDim;
            var hover = button.GetComponent<UIHoverFx>();
            var icon = button.transform.Find("Content/Icon")?.GetComponent<Image>();
            var label = button.transform.Find("Content/Label")?.GetComponent<TMP_Text>();
            var iconColor = on ? UITheme.Cyan : UITheme.Periwinkle;
            var labelColor = on ? UITheme.Cyan : UITheme.TextDim;
            if (icon != null)
            {
                icon.color = iconColor;
                hover?.SetTintBase(icon, iconColor);
            }
            if (label != null)
            {
                label.color = labelColor;
                hover?.SetTintBase(label, labelColor);
            }
        }

        /// <summary>
        /// 버튼을 누른 뒤 선택 상태로 남으면 Space/Enter(대사 넘기기)가 그 버튼까지 눌러 버린다. 선택을 푼다.
        /// </summary>
        static void Deselect() => EventSystem.current?.SetSelectedGameObject(null);
    }
}
