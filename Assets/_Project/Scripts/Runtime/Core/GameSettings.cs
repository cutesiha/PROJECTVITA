using System;
using UnityEngine;

namespace ProjectVita.Core
{
    /// <summary>
    /// 설정 (음량, 텍스트 속도, 창 모드). PlayerPrefs에 저장한다.
    /// </summary>
    public static class GameSettings
    {
        const string KeyBgm = "vita.bgmVolume";
        const string KeySfx = "vita.sfxVolume";
        const string KeyTextSpeed = "vita.textSpeed";
        const string KeyFullscreen = "vita.fullscreen";

        public const int MinTextSpeed = 1;
        public const int MaxTextSpeed = 10;

        public static float BgmVolume { get; private set; } = 0.8f;
        public static float SfxVolume { get; private set; } = 0.8f;
        /// <summary>1~10. 10이면 글자를 한 번에 보여 준다.</summary>
        public static int TextSpeed { get; private set; } = 5;
        public static bool Fullscreen { get; private set; }

        /// <summary>초당 글자 수. 최고 속도면 무한대(즉시 표시).</summary>
        public static float CharsPerSecond => TextSpeed >= MaxTextSpeed ? float.PositiveInfinity : 10f + TextSpeed * 8f;

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
            Changed = null;
            BgmVolume = PlayerPrefs.GetFloat(KeyBgm, 0.8f);
            SfxVolume = PlayerPrefs.GetFloat(KeySfx, 0.8f);
            TextSpeed = Mathf.Clamp(PlayerPrefs.GetInt(KeyTextSpeed, 5), MinTextSpeed, MaxTextSpeed);
            Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, 0) == 1;
            ApplyWindowMode();
        }

        public static void SetBgmVolume(float v)
        {
            BgmVolume = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat(KeyBgm, BgmVolume);
            Changed?.Invoke();
        }

        public static void SetSfxVolume(float v)
        {
            SfxVolume = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat(KeySfx, SfxVolume);
            Changed?.Invoke();
        }

        public static void SetTextSpeed(int v)
        {
            TextSpeed = Mathf.Clamp(v, MinTextSpeed, MaxTextSpeed);
            PlayerPrefs.SetInt(KeyTextSpeed, TextSpeed);
            Changed?.Invoke();
        }

        public static void SetFullscreen(bool on)
        {
            Fullscreen = on;
            PlayerPrefs.SetInt(KeyFullscreen, on ? 1 : 0);
            ApplyWindowMode();
            Changed?.Invoke();
        }

        public static void Save() => PlayerPrefs.Save();

        static void ApplyWindowMode()
        {
#if !UNITY_EDITOR
            if (Fullscreen)
            {
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            }
            else
            {
                // 1080p 모니터에서 1920x1080 창은 제목 표시줄 때문에 화면을 넘는다. 그때는 1600x900으로 연다.
                bool small = Display.main.systemHeight <= 1080;
                Screen.SetResolution(small ? 1600 : 1920, small ? 900 : 1080, FullScreenMode.Windowed);
            }
#endif
        }
    }

    /// <summary>일시정지 상태. 열린 메뉴가 있으면 대사 넘기기 입력을 막고 시간을 멈춘다.</summary>
    public static class GamePause
    {
        public static bool IsPaused { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsPaused = false;
            Time.timeScale = 1f;
        }

        public static void Set(bool paused)
        {
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
        }
    }
}
