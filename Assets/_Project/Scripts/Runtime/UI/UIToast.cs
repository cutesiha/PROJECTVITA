using System.Threading;
using TMPro;
using UnityEngine;

namespace ProjectVita.UI
{
    /// <summary>화면 아래쪽에 잠깐 떴다가 사라지는 알림 ("슬롯 3에 기록했습니다"). UIToast.Show(메시지)로 부른다.</summary>
    public sealed class UIToast : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] RectTransform box;
        [SerializeField] TMP_Text text;
        [SerializeField] float holdTime = 2.2f;

        static UIToast instance;
        Vector2 basePos;
        CancellationTokenSource cts;

        void Awake()
        {
            instance = this;
            basePos = box.anchoredPosition;
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public static void Show(string message)
        {
            if (instance == null)
            {
                Debug.Log("[알림] " + message);
                return;
            }
            instance.Play(message);
        }

        void Play(string message)
        {
            text.text = message;
            transform.SetAsLastSibling();
            var token = UIAnim.Restart(ref cts, destroyCancellationToken);
            Core.Tween.Forget(Routine(token));
        }

        async Awaitable Routine(CancellationToken token)
        {
            float from = group.alpha;
            await UIAnim.Run(0.28f, t =>
            {
                group.alpha = Mathf.Lerp(from, 1f, t);
                box.anchoredPosition = basePos + new Vector2(0f, -16f * (1f - UIAnim.EaseOut(t)));
            }, token);
            await UIAnim.Run(0.28f, t => group.alpha = 1f - t, token, holdTime);
        }
    }
}
