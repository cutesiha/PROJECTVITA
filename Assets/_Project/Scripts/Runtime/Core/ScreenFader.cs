using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Core
{
    /// <summary>
    /// 씬 전환용 검은 화면. 씬이 바뀌어도 살아 있다(DontDestroyOnLoad).
    /// 페이드 중에는 클릭을 막는다. 일시정지와 관계없이 실제 시간으로 움직인다.
    /// 대사 안의 &lt;&lt;fade&gt;&gt;는 이것이 아니라 Story 씬의 StoryStage가 처리한다.
    /// </summary>
    public sealed class ScreenFader : MonoBehaviour
    {
        public const float DefaultTime = 0.4f;

        static ScreenFader instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static ScreenFader Instance
        {
            get
            {
                if (instance == null) instance = Create();
                return instance;
            }
        }

        Image image;
        CancellationTokenSource cts;

        static ScreenFader Create()
        {
            var go = new GameObject("[ScreenFader]", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900; // 모든 씬 UI 위

            var imgGo = new GameObject("Black", typeof(RectTransform), typeof(Image));
            imgGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var fader = go.AddComponent<ScreenFader>();
            fader.image = imgGo.GetComponent<Image>();
            fader.image.color = new Color(0, 0, 0, 0);
            fader.image.raycastTarget = false;
            return fader;
        }

        public Awaitable FadeOut(float time = DefaultTime) => FadeTo(1f, time);
        public Awaitable FadeIn(float time = DefaultTime) => FadeTo(0f, time);

        async Awaitable FadeTo(float target, float time)
        {
            var token = Tween.Restart(ref cts, destroyCancellationToken);
            float start = image.color.a;
            image.raycastTarget = true;
            await Tween.Run(time, t => image.color = new Color(0, 0, 0, Mathf.Lerp(start, target, t)), token, unscaled: true);
            if (!token.IsCancellationRequested) image.raycastTarget = target > 0.01f;
        }
    }
}
