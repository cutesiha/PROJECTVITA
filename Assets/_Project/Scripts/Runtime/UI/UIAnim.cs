using System;
using System.Threading;
using UnityEngine;

namespace ProjectVita.UI
{
    /// <summary>
    /// UI 애니메이션 도우미. 메뉴는 일시정지(timeScale 0) 중에 열리므로 모두 실제 시간(unscaled)으로 돈다.
    /// </summary>
    public static class UIAnim
    {
        /// <summary>value를 0→1로 진행시킨다. token이 취소되면 조용히 끝난다.</summary>
        public static async Awaitable Run(float duration, Action<float> step, CancellationToken token, float delay = 0f)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                if (delay > 0f)
                {
                    float waited = 0f;
                    while (waited < delay)
                    {
                        await Awaitable.NextFrameAsync(token);
                        waited += Time.unscaledDeltaTime;
                    }
                }
                if (duration <= 0f)
                {
                    step(1f);
                    return;
                }
                float t = 0f;
                step(0f);
                while (t < duration)
                {
                    await Awaitable.NextFrameAsync(token);
                    t += Time.unscaledDeltaTime;
                    step(Mathf.Clamp01(t / duration));
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public static float EaseOut(float x) => 1f - Mathf.Pow(1f - x, 3f);
        public static float EaseIn(float x) => x * x * x;
        public static float EaseInOut(float x) => x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;

        /// <summary>살짝 넘쳤다가 돌아온다 ("뿅").</summary>
        public static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        /// <summary>이전 애니메이션을 취소하고 새 토큰을 만든다.</summary>
        public static CancellationToken Restart(ref CancellationTokenSource cts, CancellationToken owner)
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(owner);
            return cts.Token;
        }
    }
}
