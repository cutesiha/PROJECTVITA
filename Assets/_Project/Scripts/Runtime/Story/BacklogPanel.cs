using System.Collections.Generic;
using ProjectVita.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Story
{
    /// <summary>
    /// 대사 기록 화면. 대사 중에 L, 마우스 휠 위로, 로그 버튼, 또는 일시정지 메뉴에서 연다.
    /// 오른쪽 클릭, L, Esc, 맨 아래에서 휠 아래로, 닫기 버튼으로 닫는다.
    /// 열려 있는 동안은 일시정지 상태라 대사가 넘어가지 않는다.
    /// </summary>
    public sealed class BacklogPanel : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] ScrollRect scroll;
        [SerializeField] RectTransform content;
        [SerializeField] TMP_Text entryTemplate;
        [SerializeField] Button closeButton;

        [SerializeField] Color narrationColor = new Color(0.72f, 0.76f, 0.82f);
        [SerializeField] Color choiceColor = new Color(0.90f, 0.42f, 0.36f);

        public bool IsOpen => root != null && root.activeSelf;

        readonly List<TMP_Text> pool = new List<TMP_Text>();
        bool pausedBefore;
        float openedAt;

        void Awake()
        {
            entryTemplate.gameObject.SetActive(false);
            root.SetActive(false);
            closeButton.onClick.AddListener(Close);
        }

        public void Open()
        {
            if (IsOpen || EpisodeFlow.IsLoading) return;
            pausedBefore = GamePause.IsPaused;
            Build();
            root.SetActive(true);
            GamePause.Set(true);
            // 레이아웃을 바로 계산해야 맨 아래(최근 대사)로 스크롤할 수 있다.
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
            openedAt = Time.unscaledTime;
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            GamePause.Set(pausedBefore);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (!IsOpen) return;
            // 연 입력(L, 휠)이 같은 프레임에 닫지 않게 조금 기다린다.
            if (Time.unscaledTime - openedAt < 0.2f) return;

            var kb = Keyboard.current;
            if (kb != null && kb.lKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.wasPressedThisFrame)
            {
                Close();
                return;
            }
            bool atBottom = scroll.verticalNormalizedPosition <= 0.001f || content.rect.height <= scroll.viewport.rect.height;
            if (mouse.scroll.ReadValue().y < 0f && atBottom) Close();
        }

        void Build()
        {
            var list = Backlog.Entries;
            while (pool.Count < list.Count)
            {
                var label = Instantiate(entryTemplate, content);
                label.name = "Entry";
                pool.Add(label);
            }
            for (int i = 0; i < pool.Count; i++)
            {
                bool used = i < list.Count;
                pool[i].gameObject.SetActive(used);
                if (used) pool[i].text = Format(list[i]);
            }
        }

        string Format(Backlog.Entry e)
        {
            if (e.choice) return $"<color=#{ColorUtility.ToHtmlStringRGB(choiceColor)}><b>선택</b>   {e.text}</color>";
            if (string.IsNullOrEmpty(e.speaker)) return $"<color=#{ColorUtility.ToHtmlStringRGB(narrationColor)}>{e.text}</color>";
            // 이름표 색이 어두우면 글자로는 안 보이니 밝게 올린다.
            Color.RGBToHSV(e.nameColor, out float h, out float s, out float v);
            var nameColor = Color.HSVToRGB(h, Mathf.Min(s, 0.75f), Mathf.Max(v, 0.85f));
            return $"<b><color=#{ColorUtility.ToHtmlStringRGB(nameColor)}>{e.speaker}</color></b>\n{e.text}";
        }
    }
}
