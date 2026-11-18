using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ProjectVita.UI
{
    /// <summary>
    /// 창(오버레이)을 열고 닫는 애니메이션. 이 오브젝트(어두운 바탕)는 페이드, 창(window)은 아래에서 살짝 올라오며 커진다.
    /// staggerRoot의 자식들(메뉴 항목 등)은 위에서부터 차례로 나타난다.
    /// 닫는 애니메이션이 끝나면 비활성화한다. IsShown은 닫기 시작하는 순간 false가 된다.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UIPanelAnim : MonoBehaviour
    {
        [SerializeField] RectTransform window;
        [Tooltip("열 때 창이 시작하는 위치 (원래 위치 기준)")]
        [SerializeField] Vector2 showOffset = new Vector2(0f, -18f);
        [Tooltip("열 때 창이 시작하는 크기. 1보다 작으면 커지면서 나온다")]
        [SerializeField] float showScale = 0.97f;
        [SerializeField] float showTime = 0.26f;
        [SerializeField] float hideTime = 0.16f;
        [SerializeField] bool popEase;

        [Header("자식이 차례로 나타나기")]
        [SerializeField] RectTransform staggerRoot;
        [SerializeField] float staggerDelay = 0.08f;
        [SerializeField] float staggerStep = 0.035f;
        [SerializeField] float staggerTime = 0.22f;

        CanvasGroup group;
        Vector2 windowBase;
        bool initialized;
        CancellationTokenSource cts;

        public bool IsShown { get; private set; }

        void Init()
        {
            if (initialized) return;
            initialized = true;
            group = GetComponent<CanvasGroup>();
            if (window != null) windowBase = window.anchoredPosition;
        }

        void Awake() => Init();

        /// <summary>애니메이션 없이 바로 숨긴다 (씬 시작할 때).</summary>
        public void HideInstant()
        {
            Init();
            cts?.Cancel();
            IsShown = false;
            gameObject.SetActive(false);
        }

        public void Show()
        {
            Init();
            IsShown = true;
            gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;
            var token = UIAnim.Restart(ref cts, destroyCancellationToken);

            float startAlpha = group.alpha < 0.999f ? group.alpha : 0f;
            Core.Tween.Forget(UIAnim.Run(showTime, t =>
            {
                float e = popEase ? UIAnim.EaseOutBack(t) : UIAnim.EaseOut(t);
                group.alpha = Mathf.Lerp(startAlpha, 1f, UIAnim.EaseOut(t));
                if (window != null)
                {
                    window.anchoredPosition = windowBase + showOffset * (1f - e);
                    window.localScale = Vector3.one * Mathf.LerpUnclamped(showScale, 1f, e);
                }
            }, token));

            if (staggerRoot != null) Stagger(staggerRoot, token, staggerDelay, staggerStep, staggerTime);
        }

        public void Hide()
        {
            Init();
            if (!gameObject.activeSelf)
            {
                IsShown = false;
                return;
            }
            IsShown = false;
            group.blocksRaycasts = false;
            group.interactable = false;
            var token = UIAnim.Restart(ref cts, destroyCancellationToken);
            float startAlpha = group.alpha;
            Core.Tween.Forget(HideRoutine(startAlpha, token));
        }

        async Awaitable HideRoutine(float startAlpha, CancellationToken token)
        {
            await UIAnim.Run(hideTime, t =>
            {
                group.alpha = Mathf.Lerp(startAlpha, 0f, t);
                if (window != null) window.anchoredPosition = windowBase + showOffset * 0.5f * UIAnim.EaseIn(t);
            }, token);
            if (!token.IsCancellationRequested && this != null)
            {
                if (window != null)
                {
                    window.anchoredPosition = windowBase;
                    window.localScale = Vector3.one;
                }
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// parent의 켜진 자식들을 위에서부터 차례로 페이드 인한다 (CanvasGroup 알파).
        /// 레이아웃 그룹 안의 자식은 위치를 건드리면 레이아웃과 싸우므로 알파만 바꾼다.
        /// </summary>
        public static void Stagger(RectTransform parent, CancellationToken token, float delay, float step, float time, int maxAnimated = 40)
        {
            var items = new List<CanvasGroup>();
            foreach (RectTransform child in parent)
            {
                if (!child.gameObject.activeSelf) continue;
                var g = child.GetComponent<CanvasGroup>();
                if (g == null) g = child.gameObject.AddComponent<CanvasGroup>();
                items.Add(g);
            }
            for (int i = 0; i < items.Count; i++)
            {
                var g = items[i];
                if (i >= maxAnimated)
                {
                    g.alpha = 1f;
                    continue;
                }
                g.alpha = 0f;
                Core.Tween.Forget(UIAnim.Run(time, t => { if (g != null) g.alpha = UIAnim.EaseOut(t); }, token, delay + step * i));
            }
        }
    }
}
