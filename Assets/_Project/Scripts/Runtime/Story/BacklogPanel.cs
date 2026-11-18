using System.Collections.Generic;
using System.Threading;
using ProjectVita.Core;
using ProjectVita.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Story
{
    /// <summary>
    /// 대사 기록 화면. 대사 중에 L, 마우스 휠 위로, LOG 버튼, 또는 일시정지 메뉴에서 연다.
    /// 창이 페이드되며 올라오고, 화면에 보이는 기록은 위에서부터 한 줄씩 천천히 페이드 인한다.
    /// 오른쪽 클릭, L, Esc, 맨 아래에서 휠 아래로, 닫기 버튼으로 닫는다.
    /// 열려 있는 동안은 일시정지 상태라 대사가 넘어가지 않는다.
    /// </summary>
    public sealed class BacklogPanel : MonoBehaviour
    {
        [SerializeField] UIPanelAnim panel;
        [SerializeField] ScrollRect scroll;
        [SerializeField] RectTransform content;
        [SerializeField] BacklogEntry entryTemplate;
        [SerializeField] GameObject emptyLabel;
        [SerializeField] Button closeButton;
        [Tooltip("켜면 대사 기록 대신 전투 기록(BattleLog)을 보여 준다 (전투 씬)")]
        [SerializeField] bool battleSource;

        public bool IsOpen => panel != null && panel.IsShown;

        readonly List<BacklogEntry> pool = new List<BacklogEntry>();
        bool pausedBefore;
        float openedAt;
        CancellationTokenSource cts;

        void Awake()
        {
            entryTemplate.gameObject.SetActive(false);
            panel.HideInstant();
            closeButton.onClick.AddListener(Close);
        }

        public void Open()
        {
            if (IsOpen || EpisodeFlow.IsLoading) return;
            pausedBefore = GamePause.IsPaused;
            Build();
            panel.Show();
            GamePause.Set(true);
            // 레이아웃을 바로 계산해야 맨 아래(최근 대사)로 스크롤할 수 있다.
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            openedAt = Time.unscaledTime;
            RevealVisible();
        }

        public void Close()
        {
            if (!IsOpen) return;
            cts?.Cancel();
            panel.Hide();
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
            var list = battleSource ? Battle.BattleLog.Entries : Backlog.Entries;
            if (emptyLabel != null) emptyLabel.SetActive(list.Count == 0);
            while (pool.Count < list.Count)
            {
                var entry = Instantiate(entryTemplate, content);
                entry.name = "Entry";
                pool.Add(entry);
            }
            for (int i = 0; i < pool.Count; i++)
            {
                bool used = i < list.Count;
                pool[i].gameObject.SetActive(used);
                if (!used) continue;
                var e = list[i];
                var group = pool[i].GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;
                if (e.choice)
                    pool[i].Set("선택", UITheme.Amber, e.text, UITheme.Text);
                else if (string.IsNullOrEmpty(e.speaker))
                    pool[i].Set(null, UITheme.Periwinkle, e.text, UITheme.Narration);
                else
                    pool[i].Set(string.IsNullOrEmpty(e.tag) ? e.speaker : $"{e.speaker}  //  {e.tag}", Readable(e.nameColor), e.text, UITheme.Text);
            }
        }

        /// <summary>지금 화면에 보이는 기록을 위에서부터 한 줄씩 천천히 페이드 인한다. 안 보이는 기록은 바로 보인다.</summary>
        void RevealVisible()
        {
            var token = UIAnim.Restart(ref cts, destroyCancellationToken);
            var view = scroll.viewport;
            var corners = new Vector3[4];
            view.GetWorldCorners(corners);
            float viewBottom = corners[0].y, viewTop = corners[1].y;

            int order = 0;
            foreach (var entry in pool)
            {
                if (!entry.gameObject.activeSelf) continue;
                var group = entry.GetComponent<CanvasGroup>();
                if (group == null) group = entry.gameObject.AddComponent<CanvasGroup>();
                ((RectTransform)entry.transform).GetWorldCorners(corners);
                bool visible = corners[1].y > viewBottom && corners[0].y < viewTop;
                if (!visible)
                {
                    group.alpha = 1f;
                    continue;
                }
                group.alpha = 0f;
                var g = group;
                Tween.Forget(UIAnim.Run(0.42f, t => { if (g != null) g.alpha = UIAnim.EaseOut(t); }, token, 0.12f + 0.07f * order));
                order++;
            }
        }

        /// <summary>이름표 색이 어두우면 글자로는 안 보이니 밝게 올린다.</summary>
        static Color Readable(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Min(s, 0.7f), Mathf.Max(v, 0.85f));
        }
    }
}
