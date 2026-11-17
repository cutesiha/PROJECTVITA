using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Yarn.Unity;

namespace ProjectVita.Story
{
    /// <summary>
    /// 대사창과 선택지를 보여 주는 Yarn Presenter.
    /// 4-2단계 최소 화면: 이름표, 본문, 다음 표시, 클릭/Space/Enter로 넘기기, 선택지 (클릭 또는 숫자 키).
    /// TODO(4-4): 타이핑 효과, 스탠딩 강조, #face 태그, 디자인.
    /// </summary>
    public sealed class StoryPresenter : DialoguePresenterBase
    {
        [Header("대사창")]
        [SerializeField] GameObject dialogueBox;
        [SerializeField] GameObject namePlate;
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text bodyText;
        [SerializeField] GameObject nextIndicator;

        [Header("선택지")]
        [SerializeField] RectTransform optionsRoot;
        [SerializeField] Button optionTemplate;

        readonly List<Button> optionButtons = new List<Button>();

        void Awake()
        {
            if (optionTemplate != null) optionTemplate.gameObject.SetActive(false);
            if (dialogueBox != null) dialogueBox.SetActive(false);
            if (nextIndicator != null) nextIndicator.SetActive(false);
        }

        // ------------------------------------------------------------ 대화 시작과 끝

        public override YarnTask OnDialogueStartedAsync(CancellationToken token)
        {
            dialogueBox.SetActive(true);
            namePlate.SetActive(false);
            nameText.text = string.Empty;
            bodyText.text = string.Empty;
            nextIndicator.SetActive(false);
            ClearOptions();
            return YarnTask.CompletedTask;
        }

        public override YarnTask OnDialogueCompleteAsync(CancellationToken token)
        {
            // 씬 전환으로 이미 파괴됐을 수 있다.
            if (this != null)
            {
                dialogueBox.SetActive(false);
                ClearOptions();
            }
            return YarnTask.CompletedTask;
        }

        public override YarnTask OnNodeEnter(string nodeName, CancellationToken token = default)
        {
            DevOverlay.SetNode(nodeName);
            return YarnTask.CompletedTask;
        }

        // ------------------------------------------------------------ 대사

        public override async YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
        {
            // "화자: 대사" 형식이면 CharacterName에 화자가 들어온다. 없으면 나레이션이고 이름표를 숨긴다.
            string speaker = line.CharacterName;
            bool narration = string.IsNullOrEmpty(speaker);

            namePlate.SetActive(!narration);
            nameText.text = narration ? string.Empty : speaker;
            bodyText.text = line.TextWithoutCharacterName.Text;
            nextIndicator.SetActive(true);

            await WaitForAdvance(token.NextContentToken);

            if (this != null) nextIndicator.SetActive(false);
            // 본문은 지우지 않는다. 다음이 선택지면 질문 대사가 화면에 남아 있어야 한다.
        }

        async YarnTask WaitForAdvance(CancellationToken token)
        {
            // 이 줄을 띄운 입력(선택지 클릭 등)이 같은 프레임에 다음 줄까지 넘기지 않게 한 프레임 쉰다.
            await YarnTask.Yield();
            while (this != null && !token.IsCancellationRequested)
            {
                if (AdvancePressed()) return;
                await YarnTask.Yield();
            }
        }

        static bool AdvancePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                return true;

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                // 버튼 위의 클릭은 그 버튼의 것이다. 대사창과 배경은 raycastTarget을 꺼 두어서 여기에 걸리지 않는다.
                var es = EventSystem.current;
                return es == null || !es.IsPointerOverGameObject();
            }
            return false;
        }

        // ------------------------------------------------------------ 선택지

        public override async YarnTask<DialogueOption> RunOptionsAsync(DialogueOption[] dialogueOptions, LineCancellationToken token)
        {
            ClearOptions();
            nextIndicator.SetActive(false);

            var shown = new List<DialogueOption>();
            DialogueOption selected = null;

            foreach (var option in dialogueOptions)
            {
                // 조건(<<if>>)이 맞지 않는 선택지는 숨긴다.
                if (!option.IsAvailable) continue;

                var button = Instantiate(optionTemplate, optionsRoot);
                button.gameObject.SetActive(true);
                button.name = $"Option_{shown.Count + 1}";
                var label = button.GetComponentInChildren<TMP_Text>(true);
                label.text = $"{shown.Count + 1}. {option.Line.TextWithoutCharacterName.Text}";

                var captured = option;
                button.onClick.AddListener(() => selected = captured);
                optionButtons.Add(button);
                shown.Add(option);
            }

            // 고를 수 있는 선택지가 없으면 null을 돌려준다. DialogueRunner가 선택지 다음으로 그냥 넘어간다.
            if (shown.Count == 0) return null;

            await YarnTask.Yield();
            while (selected == null && this != null && !token.NextContentToken.IsCancellationRequested)
            {
                selected = NumberKeyOption(shown);
                if (selected == null) await YarnTask.Yield();
            }

            if (this != null) ClearOptions();
            return selected;
        }

        /// <summary>숫자 키 1~9 (키패드 포함)로 선택지를 고른다.</summary>
        static DialogueOption NumberKeyOption(List<DialogueOption> shown)
        {
            var kb = Keyboard.current;
            if (kb == null) return null;
            for (int i = 0; i < shown.Count && i < 9; i++)
            {
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame || kb[(Key)((int)Key.Numpad1 + i)].wasPressedThisFrame)
                    return shown[i];
            }
            return null;
        }

        void ClearOptions()
        {
            foreach (var b in optionButtons)
                if (b != null) Destroy(b.gameObject);
            optionButtons.Clear();
        }

        // ------------------------------------------------------------ 명령

        /// <summary>
        /// 모든 명령은 실제 처리 전에 여기를 먼저 지난다. 개발용 화면에 기록하고, 등록되지 않은 명령(오타)을 경고한다.
        /// Yarn Spinner는 등록되지 않은 명령을 일반 로그로만 남기고 넘어가기 때문이다.
        /// </summary>
        public override YarnTask RunCommand(Yarn.Command command, LineCancellationToken cancellationToken)
        {
            string text = command.Text;
            DevOverlay.PushCommand(text);

            var parts = DialogueRunner.SplitCommandText(text);
            string name = null;
            foreach (var p in parts) { name = p; break; }

            if (!string.IsNullOrEmpty(name) && !StoryCommands.IsKnown(name))
            {
                Debug.LogWarning($"[명령] 등록되지 않은 명령 <<{text}>>. 오타인지 확인한다. (CLAUDE.md의 명령 목록 참고)");
                DevOverlay.SetStatus($"등록되지 않은 명령: <<{text}>>");
            }
            return YarnTask.CompletedTask;
        }
    }
}
