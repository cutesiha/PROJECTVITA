using System;
using ProjectVita.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectVita.Story
{
    /// <summary>
    /// 대사 진행 모드 (문서 4-7).
    /// - 오토: 글자가 다 나오고 잠시 뒤 다음 줄로 (설정의 오토 속도). 클릭하면 바로 넘어간다.
    /// - 스킵: 읽은 대사만 빠르게 넘긴다. 처음 보는 대사, 선택지, 대화 끝, 씬 전환에서 멈춘다.
    /// - 빨리감기: Ctrl을 누르는 동안 읽지 않은 대사도 넘긴다. 선택지에서는 멈춘다.
    /// 스킵과 빨리감기 중에는 시간을 빠르게 돌려서 배경 전환이나 &lt;&lt;wait&gt;&gt;도 빨리 지나간다.
    /// 오토는 씬이 바뀌어도 유지되고, 타이틀로 가면 꺼진다.
    /// </summary>
    public static class DialogueModes
    {
        /// <summary>스킵 중의 시간 배속.</summary>
        public const float SkipTimeScale = 6f;
        /// <summary>스킵 중에 한 줄을 보여 주는 시간 (실제 시간, 초).</summary>
        public const float SkipLineSeconds = 0.06f;

        public static bool Auto { get; private set; }
        public static bool Skip { get; private set; }

        /// <summary>오토나 스킵이 바뀌면 불린다 (버튼 색 갱신).</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Auto = false;
            Skip = false;
            Changed = null;
        }

        /// <summary>Ctrl을 누르고 있는지. 메뉴가 열려 있으면 false.</summary>
        public static bool FastForwardHeld
        {
            get
            {
                if (GamePause.IsPaused) return false;
                var kb = Keyboard.current;
                return kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            }
        }

        /// <summary>지금 대사를 빠르게 넘기는 중인지 (스킵 또는 빨리감기).</summary>
        public static bool Skipping => Skip || FastForwardHeld;

        public static void SetAuto(bool on)
        {
            if (Auto == on) return;
            Auto = on;
            Changed?.Invoke();
        }

        public static void SetSkip(bool on)
        {
            if (Skip == on) return;
            Skip = on;
            Changed?.Invoke();
        }

        public static void StopAll()
        {
            SetAuto(false);
            SetSkip(false);
        }
    }
}
