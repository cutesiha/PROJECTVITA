using System;
using System.Collections.Generic;
using System.Threading;
using ProjectVita.Core;
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
    /// - 이름표 (캐릭터 색), 나레이션 모드 (이름표 숨김, 본문 색 바꿈)
    /// - 타이핑 효과: 진행 중에 클릭하면 끝까지 바로 보이고, 한 번 더 누르면 다음 줄
    /// - 대사 끝의 #face:표정 태그를 화자 스탠딩에 적용, 말하는 사람만 밝게
    /// - 선택지: 클릭 또는 숫자 키
    /// 일시정지(메뉴) 중에는 입력을 받지 않는다.
    /// </summary>
    public sealed class StoryPresenter : DialoguePresenterBase
    {
        [Header("대사창")]
        [SerializeField] GameObject dialogueBox;
        [SerializeField] GameObject namePlate;
        [SerializeField] Image namePlateImage;
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text bodyText;
        [SerializeField] RectTransform nextIndicator;

        [Header("선택지")]
        [SerializeField] RectTransform optionsRoot;
        [SerializeField] Button optionTemplate;

        [Header("색")]
        [SerializeField] Color speechColor = new Color(0.93f, 0.93f, 0.94f);
        [SerializeField] Color narrationColor = new Color(0.72f, 0.76f, 0.82f);
        [SerializeField] Color defaultNameColor = new Color(0.70f, 0.15f, 0.12f);

        /// <summary>노드에 들어갈 때마다 불린다. 자동 저장에 쓴다.</summary>
        public static event Action<string> NodeEntered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => NodeEntered = null;

        readonly List<Button> optionButtons = new List<Button>();
        Vector2 indicatorBase;

        void Awake()
        {
            if (optionTemplate != null) optionTemplate.gameObject.SetActive(false);
            if (dialogueBox != null) dialogueBox.SetActive(false);
            if (nextIndicator != null)
            {
                indicatorBase = nextIndicator.anchoredPosition;
                nextIndicator.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            // 다음 표시가 위아래로 살짝 움직인다.
            if (nextIndicator != null && nextIndicator.gameObject.activeSelf)
                nextIndicator.anchoredPosition = indicatorBase + new Vector2(0f, Mathf.Sin(Time.unscaledTime * 6f) * 5f);
        }

        // ------------------------------------------------------------ 대화 시작과 끝

        public override YarnTask OnDialogueStartedAsync(CancellationToken token)
        {
            dialogueBox.SetActive(true);
            namePlate.SetActive(false);
            nameText.text = string.Empty;
            bodyText.text = string.Empty;
            nextIndicator.gameObject.SetActive(false);
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
            NodeEntered?.Invoke(nodeName);
            return YarnTask.CompletedTask;
        }

        // ------------------------------------------------------------ 대사

        public override async YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
        {
            // "화자: 대사" 형식이면 CharacterName에 화자가 들어온다. 없으면 나레이션이다.
            string speaker = line.CharacterName;
            bool narration = string.IsNullOrEmpty(speaker);
            var character = narration ? null : AssetCatalog.Instance.FindCharacter(speaker);

            var stage = StoryStage.Current;
            if (stage != null)
            {
                string face = FindTag(line.Metadata, "face");
                if (face != null)
                {
                    if (character != null) stage.Face(character.id, face);
                    else Debug.LogWarning($"[대사] #face:{face}: 화자 '{speaker}'가 Data/Characters.csv에 없어서 표정을 바꿀 수 없다.");
                }
                stage.Highlight(narration ? null : speaker);
            }

            namePlate.SetActive(!narration);
            nameText.text = narration ? string.Empty : speaker;
            if (namePlateImage != null) namePlateImage.color = character != null ? character.nameColor : defaultNameColor;
            bodyText.color = narration ? narrationColor : speechColor;
            bodyText.text = line.TextWithoutCharacterName.Text;
            nextIndicator.gameObject.SetActive(false);

            await TypeText(token);
            if (this == null || token.IsNextContentRequested) return;

            nextIndicator.gameObject.SetActive(true);
            await WaitForAdvance(token.NextContentToken);
            if (this != null) nextIndicator.gameObject.SetActive(false);
            // 본문은 지우지 않는다. 다음이 선택지면 질문 대사가 화면에 남아 있어야 한다.
        }

        /// <summary>글자를 하나씩 보여 준다. 진행 중에 넘기기 입력이 오면 끝까지 바로 보여 준다.</summary>
        async YarnTask TypeText(LineCancellationToken token)
        {
            bodyText.ForceMeshUpdate();
            int total = bodyText.textInfo.characterCount;
            float cps = GameSettings.CharsPerSecond;

            if (float.IsInfinity(cps) || total == 0)
            {
                bodyText.maxVisibleCharacters = int.MaxValue;
                return;
            }

            bodyText.maxVisibleCharacters = 0;
            float shown = 0f;
            // 이 줄을 띄운 입력이 같은 프레임에 타이핑까지 건너뛰지 않게 한 프레임 쉰다.
            await YarnTask.Yield();
            while (this != null && !token.IsNextContentRequested)
            {
                if (token.IsHurryUpRequested || AdvancePressed()) break;
                shown += cps * Time.deltaTime; // 일시정지 중에는 deltaTime이 0이라 멈춘다
                bodyText.maxVisibleCharacters = Mathf.Min(total, (int)shown);
                if (shown >= total) break;
                await YarnTask.Yield();
            }
            if (this != null) bodyText.maxVisibleCharacters = int.MaxValue;
        }

        async YarnTask WaitForAdvance(CancellationToken token)
        {
            // 타이핑을 끝낸 입력이 그대로 다음 줄까지 넘기지 않게 한 프레임 쉰다.
            await YarnTask.Yield();
            while (this != null && !token.IsCancellationRequested)
            {
                if (AdvancePressed()) return;
                await YarnTask.Yield();
            }
        }

        static bool AdvancePressed()
        {
            if (GamePause.IsPaused) return false;

            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                return true;

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                // 버튼 위의 클릭은 그 버튼의 것이다. 대사창과 무대는 raycastTarget을 꺼 두어서 여기에 걸리지 않는다.
                var es = EventSystem.current;
                return es == null || !es.IsPointerOverGameObject();
            }
            return false;
        }

        /// <summary>줄 태그에서 "key:값"을 찾는다. Yarn은 #을 뗀 채로 넘겨준다.</summary>
        static string FindTag(string[] metadata, string key)
        {
            if (metadata == null) return null;
            string prefix = key + ":";
            foreach (var raw in metadata)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var tag = raw.TrimStart('#');
                if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return tag.Substring(prefix.Length);
            }
            return null;
        }

        // ------------------------------------------------------------ 선택지

        public override async YarnTask<DialogueOption> RunOptionsAsync(DialogueOption[] dialogueOptions, LineCancellationToken token)
        {
            ClearOptions();
            nextIndicator.gameObject.SetActive(false);

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
                button.onClick.AddListener(() =>
                {
                    if (!GamePause.IsPaused) selected = captured;
                });
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
            if (kb == null || GamePause.IsPaused) return null;
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

            string name = null;
            foreach (var p in DialogueRunner.SplitCommandText(text))
            {
                name = p;
                break;
            }

            if (!string.IsNullOrEmpty(name) && !StoryCommands.IsKnown(name))
            {
                Debug.LogWarning($"[명령] 등록되지 않은 명령 <<{text}>>. 오타인지 확인한다. (CLAUDE.md의 명령 목록 참고)");
                DevOverlay.SetStatus($"등록되지 않은 명령: <<{text}>>");
            }
            return YarnTask.CompletedTask;
        }
    }
}
