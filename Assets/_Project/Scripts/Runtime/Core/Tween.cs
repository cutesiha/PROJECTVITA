using System;
using System.Threading;
using UnityEngine;

namespace ProjectVita.Core
{
    /// <summary>
    /// 아주 작은 트윈 도우미. DOTween 없이 Awaitable로 값을 0→1로 진행시킨다.
    /// 기본은 Time.deltaTime 기준이라 일시정지(timeScale 0) 중에는 멈춘다. 화면 전환처럼 멈추면 안 되는 것은 unscaled를 켠다.
    /// token이 취소되면(오브젝트 파괴, 새 트윈으로 교체) 예외 없이 조용히 끝난다.
    /// </summary>
    public static class Tween
    {
        public static async Awaitable Run(float duration, Action<float> step, CancellationToken token, bool unscaled = false)
        {
            if (token.IsCancellationRequested) return;
            if (duration <= 0f)
            {
                step(1f);
                return;
            }

            float elapsed = 0f;
            step(0f);
            while (elapsed < duration)
            {
                try
                {
                    await Awaitable.NextFrameAsync(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                elapsed += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                step(Mathf.Clamp01(elapsed / duration));
            }
        }

        /// <summary>이전 트윈을 취소하고 새 토큰을 만든다. 같은 대상에 트윈이 겹치지 않게 할 때 쓴다.</summary>
        public static CancellationToken Restart(ref CancellationTokenSource cts, CancellationToken owner)
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(owner);
            return cts.Token;
        }

        public static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
        public static float EaseInOut(float x) => x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f;

        /// <summary>기다리지 않는 Awaitable을 실행하고, 예외가 나면 콘솔에 남긴다.</summary>
        public static async void Forget(Awaitable awaitable)
        {
            try
            {
                await awaitable;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
