using System;
using System.Collections.Generic;
using System.Threading;
using ProjectVita.Core;
using ProjectVita.UI;
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
    /// - 대사창: 대화가 시작되면 아래에서 부드럽게 올라오고, 끝나면 내려가며 사라진다.
    /// - 이름표: [식별 칩] + 이름 (캐릭터 색). 칩은 Characters.csv의 name_tag가 있을 때만. 나레이션이면 이름표를 숨기고 본문 색을 바꾼다.
    /// - 타이핑 효과와 깜빡이는 커서: 진행 중에 클릭하면 끝까지 바로 보이고, 한 번 더 누르면 다음 줄
    /// - 대사 끝의 #face:표정 태그를 화자 스탠딩에 적용, 말하는 사람만 밝게
    /// - 선택지: 클릭 또는 숫자 키
    /// - 4-7: 대사 기록(Backlog), 읽은 대사 기록(ReadLines), 오토/스킵/빨리감기(DialogueModes)
    /// 일시정지(메뉴) 중에는 입력을 받지 않는다.
    /// </summary>
    public sealed class StoryPresenter : DialoguePresenterBase
    {
        [Header("대사창")]
        [SerializeField] GameObject dialogueBox;
        [SerializeField] CanvasGroup boxGroup;
        [SerializeField] GameObject namePlate;
        [Tooltip("식별 칩 바탕 (캐릭터 색)")]
        [SerializeField] Image namePlateImage;
        [SerializeField] GameObject nameTagChip;
        [SerializeField] TMP_Text nameTagText;
        [SerializeField] Image nameDot;
        [SerializeField] TMP_Text nameText;
        [SerializeField] Image accentBar;
        [SerializeField] TMP_Text metaText;
        [SerializeField] TMP_Text bodyText;
        [SerializeField] RectTransform typingCursor;
        [SerializeField] CanvasGroup nextIndicator;
        [SerializeField] RectTransform nextArrow;

        [Header("선택지")]
        [SerializeField] RectTransform optionsRoot;
        [SerializeField] Button optionTemplate;

        [Header("색")]
        [SerializeField] Color speechColor = new Color(0.79f, 0.80f, 0.84f);
        [SerializeField] Color narrationColor = new Color(0.61f, 0.63f, 0.71f);
        [SerializeField] Color defaultNameColor = new Color(0.48f, 0.51f, 0.65f);

        /// <summary>노드에 들어갈 때마다 불린다. 자동 저장에 쓴다.</summary>
        public static event Action<string> NodeEntered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => NodeEntered = null;

        readonly List<Button> optionButtons = new List<Button>();
        RectTransform boxRect;
        Vector2 boxBase, arrowBase;
        bool nextVisible, typing;
        CancellationTokenSource boxCts;

        void Awake()
        {
            if (optionTemplate != null) optionTemplate.gameObject.SetActive(false);
            if (dialogueBox != null)
            {
                boxRect = (RectTransform)dialogueBox.transform;
                boxBase = boxRect.anchoredPosition;
                dialogueBox.SetActive(false);
            }
            if (nextArrow != null) arrowBase = nextArrow.anchoredPosition;
            if (nextIndicator != null) nextIndicator.alpha = 0f;
            if (typingCursor != null) typingCursor.gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // CLICK ▸ 표시: 부드럽게 나타나고, 화살표가 위아래로 살짝 움직인다.
            if (nextIndicator != null)
                nextIndicator.alpha = Mathf.MoveTowards(nextIndicator.alpha, nextVisible ? 1f : 0f, dt / 0.2f);
            if (nextArrow != null)
                nextArrow.anchoredPosition = arrowBase + new Vector2(0f, -3f * (0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / 1.1f)));

            // 타이핑 커서는 0.9초마다 깜빡인다.
            if (typingCursor != null && typing)
                typingCursor.gameObject.SetActive(Mathf.Repeat(Time.unscaledTime, 0.9f) < 0.45f);

            // 스킵, 빨리감기 중에는 연출(트윈, <<wait>>)도 빠르게 돌린다. 대사창이 떠 있을 때만.
            bool fast = dialogueBox != null && dialogueBox.activeSelf && DialogueModes.Skipping;
            GamePause.SetSpeed(fast ? DialogueModes.SkipTimeScale : 1f);
        }

        void OnDisable() => GamePause.SetSpeed(1f);

        // ------------------------------------------------------------ 대화 시작과 끝

        public override YarnTask OnDialogueStartedAsync(CancellationToken token)
        {
            namePlate.SetActive(false);
            nameText.text = string.Empty;
            bodyText.text = string.Empty;
            if (metaText != null) metaText.text = string.Empty;
            SetNext(false, instant: true);
            ClearOptions();
            ShowBox();
            return YarnTask.CompletedTask;
        }

        public override YarnTask OnDialogueCompleteAsync(CancellationToken token)
        {
            // 대화가 끝나면 스킵을 멈춘다 (탐색으로 돌아갔을 때 다음 대화까지 스킵이 이어지지 않게).
            DialogueModes.SetSkip(false);
            // 씬 전환으로 이미 파괴됐을 수 있다.
            if (this != null)
            {
                ClearOptions();
                HideBox();
            }
            return YarnTask.CompletedTask;
        }

        /// <summary>대사창이 아래에서 올라온다.</summary>
        void ShowBox()
        {
            dialogueBox.SetActive(true);
            var token = UIAnim.Restart(ref boxCts, destroyCancellationToken);
            float from = boxGroup != null ? boxGroup.alpha : 1f;
            if (from > 0.99f) from = 0f;
            Tween.Forget(UIAnim.Run(0.42f, t =>
            {
                float e = UIAnim.EaseOut(t);
                if (boxGroup != null) boxGroup.alpha = Mathf.Lerp(from, 1f, e);
                boxRect.anchoredPosition = boxBase + new Vector2(0f, -80f * (1f - e));
            }, token));
        }

        /// <summary>대사창이 내려가며 사라진다. 다음 대화가 바로 시작되면 ShowBox가 취소한다.</summary>
        void HideBox()
        {
            if (!dialogueBox.activeSelf) return;
            var token = UIAnim.Restart(ref boxCts, destroyCancellationToken);
            float from = boxGroup != null ? boxGroup.alpha : 1f;
            Tween.Forget(HideRoutine(from, token));
        }

        async Awaitable HideRoutine(float from, CancellationToken token)
        {
            await UIAnim.Run(0.24f, t =>
            {
                float e = UIAnim.EaseIn(t);
                if (boxGroup != null) boxGroup.alpha = Mathf.Lerp(from, 0f, t);
                boxRect.anchoredPosition = boxBase + new Vector2(0f, -50f * e);
            }, token);
            if (token.IsCancellationRequested || this == null) return;
            dialogueBox.SetActive(false);
            boxRect.anchoredPosition = boxBase;
        }

        void SetNext(bool visible, bool instant = false)
        {
            nextVisible = visible;
            if (instant && nextIndicator != null) nextIndicator.alpha = visible ? 1f : 0f;
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

            var nameColor = character != null ? character.nameColor : defaultNameColor;
            string text = line.TextWithoutCharacterName.Text;
            string tag = character?.nameTag;
            namePlate.SetActive(!narration);
            nameText.text = narration ? string.Empty : speaker;
            if (nameTagChip != null) nameTagChip.SetActive(!narration && !string.IsNullOrEmpty(tag));
            if (nameTagText != null) nameTagText.text = tag ?? string.Empty;
            if (namePlateImage != null) namePlateImage.color = nameColor;
            if (nameDot != null) nameDot.color = nameColor;
            if (accentBar != null) accentBar.color = narration ? UITheme.Periwinkle : nameColor;
            bodyText.color = narration ? narrationColor : speechColor;
            bodyText.text = text;
            SetNext(false, instant: true);

            // 읽은 대사 기록과 대사 기록
            string key = ReadLines.KeyOf(line);
            bool wasRead = ReadLines.IsRead(key);
            ReadLines.Mark(key);
            Backlog.AddLine(narration ? null : speaker, text, nameColor, tag);
            if (metaText != null) metaText.text = $"LOG.{Backlog.LineCount:000}";

            // 스킵은 읽은 대사만 넘긴다 (설정에서 바꿀 수 있음). 처음 보는 대사에서 멈춘다. Ctrl 빨리감기는 계속 넘긴다.
            if (DialogueModes.Skip && !wasRead && GameSettings.SkipReadOnly && !DialogueModes.FastForwardHeld) DialogueModes.SetSkip(false);

            bodyText.ForceMeshUpdate();
            int total = bodyText.textInfo.characterCount;

            if (DialogueModes.Skipping) bodyText.maxVisibleCharacters = int.MaxValue;
            else await TypeText(total, token);
            if (this == null || token.IsNextContentRequested) return;

            SetNext(true);
            await WaitForAdvance(token.NextContentToken, total);
            if (this != null) SetNext(false);
            // 본문은 지우지 않는다. 다음이 선택지면 질문 대사가 화면에 남아 있어야 한다.
        }

        /// <summary>글자를 하나씩 보여 준다. 진행 중에 넘기기 입력이 오거나 스킵이 켜지면 끝까지 바로 보여 준다.</summary>
        async YarnTask TypeText(int total, LineCancellationToken token)
        {
            float cps = GameSettings.CharsPerSecond;

            if (float.IsInfinity(cps) || total == 0)
            {
                bodyText.maxVisibleCharacters = int.MaxValue;
                return;
            }

            bodyText.maxVisibleCharacters = 0;
            float shown = 0f;
            typing = true;
            PlaceCursor(0);
            // 이 줄을 띄운 입력이 같은 프레임에 타이핑까지 건너뛰지 않게 한 프레임 쉰다.
            await YarnTask.Yield();
            while (this != null && !token.IsNextContentRequested)
            {
                if (token.IsHurryUpRequested || AdvancePressed() || DialogueModes.Skipping) break;
                shown += cps * Time.deltaTime; // 일시정지 중에는 deltaTime이 0이라 멈춘다
                int visible = Mathf.Min(total, (int)shown);
                bodyText.maxVisibleCharacters = visible;
                PlaceCursor(visible);
                if (shown >= total) break;
                await YarnTask.Yield();
            }
            typing = false;
            if (this == null) return;
            bodyText.maxVisibleCharacters = int.MaxValue;
            if (typingCursor != null) typingCursor.gameObject.SetActive(false);
        }

        /// <summary>타이핑 커서를 마지막으로 보이는 글자 뒤에 둔다. 글자 정보는 본문 기준 좌표라 커서도 본문의 자식이다.</summary>
        void PlaceCursor(int visible)
        {
            if (typingCursor == null) return;
            var info = bodyText.textInfo;
            if (info.characterCount == 0) return;
            float size = bodyText.fontSize;
            Vector2 pos;
            if (visible <= 0)
            {
                var first = info.characterInfo[0];
                pos = new Vector2(first.origin, first.baseLine);
            }
            else
            {
                var c = info.characterInfo[Mathf.Min(visible, info.characterCount) - 1];
                pos = new Vector2(c.xAdvance + 3f, c.baseLine); // xAdvance = 다음 글자가 시작할 x
                if (c.character == '\n') pos = new Vector2(info.characterInfo[0].origin, c.baseLine - info.lineInfo[0].lineHeight);
            }
            typingCursor.localPosition = new Vector3(pos.x, pos.y - size * 0.18f, 0f);
            typingCursor.sizeDelta = new Vector2(size * 0.42f, size * 1.05f);
        }

        /// <summary>
        /// 다음 줄로 넘어갈 때까지 기다린다: 넘기기 입력, 스킵/빨리감기(짧게 보여 주고), 오토(글자 수에 맞춰 기다리고).
        /// 시간은 실제 시간으로 세고, 메뉴가 열려 있는 동안은 세지 않는다.
        /// </summary>
        async YarnTask WaitForAdvance(CancellationToken token, int characters)
        {
            // 타이핑을 끝낸 입력이 그대로 다음 줄까지 넘기지 않게 한 프레임 쉰다.
            await YarnTask.Yield();
            float skipTimer = 0f, autoTimer = 0f;
            float autoDelay = GameSettings.AutoDelay(characters);
            while (this != null && !token.IsCancellationRequested)
            {
                if (AdvancePressed())
                {
                    // 스킵 중에 누르면 스킵을 멈춘다.
                    DialogueModes.SetSkip(false);
                    return;
                }
                if (!GamePause.IsPaused)
                {
                    float dt = Time.unscaledDeltaTime;
                    if (DialogueModes.Skipping)
                    {
                        skipTimer += dt;
                        if (skipTimer >= DialogueModes.SkipLineSeconds) return;
                    }
                    else if (DialogueModes.Auto)
                    {
                        autoTimer += dt;
                        if (autoTimer >= autoDelay) return;
                    }
                }
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
            SetNext(false);
            // 스킵은 선택지에서 멈춘다.
            DialogueModes.SetSkip(false);

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
                label.text = $"<color={UITheme.ToHex(UITheme.Cyan)}>{shown.Count + 1:00}</color>   {option.Line.TextWithoutCharacterName.Text}";

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

            // 선택지가 위에서부터 차례로 나타난다.
            UIPanelAnim.Stagger(optionsRoot, destroyCancellationToken, 0f, 0.06f, 0.25f);

            await YarnTask.Yield();
            while (selected == null && this != null && !token.NextContentToken.IsCancellationRequested)
            {
                selected = NumberKeyOption(shown);
                if (selected == null) await YarnTask.Yield();
            }

            if (this != null) ClearOptions();
            if (selected != null) Backlog.AddChoice(selected.Line.TextWithoutCharacterName.Text);
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
