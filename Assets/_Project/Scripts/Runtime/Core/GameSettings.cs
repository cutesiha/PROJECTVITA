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
        const string KeyAutoSpeed = "vita.autoSpeed";
        const string KeySkipReadOnly = "vita.skipReadOnly";
        const string KeyScanlines = "vita.scanlines";
        const string KeyFilmGrain = "vita.filmGrain";
        const string KeyScreenShake = "vita.screenShake";

        public const int MinTextSpeed = 1;
        public const int MaxTextSpeed = 10;
        public const int MinAutoSpeed = 1;
        public const int MaxAutoSpeed = 10;

        public static float BgmVolume { get; private set; } = 0.8f;
        public static float SfxVolume { get; private set; } = 0.8f;
        /// <summary>1~10. 10이면 글자를 한 번에 보여 준다.</summary>
        public static int TextSpeed { get; private set; } = 5;
        /// <summary>1~10. 오토 모드에서 다음 줄로 넘어가기까지 기다리는 시간. 클수록 빠르다.</summary>
        public static int AutoSpeed { get; private set; } = 5;
        public static bool Fullscreen { get; private set; }
        /// <summary>스킵(Tab)이 읽은 대사에서만 넘기는지. 끄면 처음 보는 대사도 넘긴다.</summary>
        public static bool SkipReadOnly { get; private set; } = true;
        /// <summary>CRT 스캔라인 효과</summary>
        public static bool Scanlines { get; private set; } = true;
        /// <summary>필름 노이즈 효과</summary>
        public static bool FilmGrain { get; private set; } = true;
        /// <summary>화면 흔들림 (&lt;&lt;shake&gt;&gt;, 전투 피격). 끄면 흔들지 않는다 (멀미 방지).</summary>
        public static bool ScreenShake { get; private set; } = true;

        /// <summary>초당 글자 수. 최고 속도면 무한대(즉시 표시).</summary>
        public static float CharsPerSecond => TextSpeed >= MaxTextSpeed ? float.PositiveInfinity : 10f + TextSpeed * 8f;

        /// <summary>오토 모드: 글자가 다 나온 뒤 기다리는 시간(초). 글자가 많을수록 길다.</summary>
        public static float AutoDelay(int characters)
        {
            float t = (AutoSpeed - MinAutoSpeed) / (float)(MaxAutoSpeed - MinAutoSpeed);
            return Mathf.Lerp(1.6f, 0.4f, t) + characters * Mathf.Lerp(0.08f, 0.02f, t);
        }

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
            Changed = null;
            BgmVolume = PlayerPrefs.GetFloat(KeyBgm, 0.8f);
            SfxVolume = PlayerPrefs.GetFloat(KeySfx, 0.8f);
            TextSpeed = Mathf.Clamp(PlayerPrefs.GetInt(KeyTextSpeed, 5), MinTextSpeed, MaxTextSpeed);
            AutoSpeed = Mathf.Clamp(PlayerPrefs.GetInt(KeyAutoSpeed, 5), MinAutoSpeed, MaxAutoSpeed);
            SkipReadOnly = PlayerPrefs.GetInt(KeySkipReadOnly, 1) == 1;
            Scanlines = PlayerPrefs.GetInt(KeyScanlines, 1) == 1;
            FilmGrain = PlayerPrefs.GetInt(KeyFilmGrain, 1) == 1;
            ScreenShake = PlayerPrefs.GetInt(KeyScreenShake, 1) == 1;
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

        public static void SetAutoSpeed(int v)
        {
            AutoSpeed = Mathf.Clamp(v, MinAutoSpeed, MaxAutoSpeed);
            PlayerPrefs.SetInt(KeyAutoSpeed, AutoSpeed);
            Changed?.Invoke();
        }

        public static void SetSkipReadOnly(bool on) => SetFlag(KeySkipReadOnly, on, v => SkipReadOnly = v);
        public static void SetScanlines(bool on) => SetFlag(KeyScanlines, on, v => Scanlines = v);
        public static void SetFilmGrain(bool on) => SetFlag(KeyFilmGrain, on, v => FilmGrain = v);
        public static void SetScreenShake(bool on) => SetFlag(KeyScreenShake, on, v => ScreenShake = v);

        static void SetFlag(string key, bool on, Action<bool> assign)
        {
            assign(on);
            PlayerPrefs.SetInt(key, on ? 1 : 0);
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

    /// <summary>
    /// 일시정지 상태와 시간 배속. 열린 메뉴가 있으면 대사 넘기기 입력을 막고 시간을 멈춘다.
    /// 스킵과 빨리감기 중에는 시간을 빠르게 돌려서 배경 전환, &lt;&lt;wait&gt;&gt; 같은 연출도 빨리 지나가게 한다.
    /// </summary>
    public static class GamePause
    {
        public static bool IsPaused { get; private set; }
        static float speed = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsPaused = false;
            speed = 1f;
            Time.timeScale = 1f;
        }

        public static void Set(bool paused)
        {
            IsPaused = paused;
            Apply();
        }

        /// <summary>일시정지가 아닐 때의 시간 배속 (1 = 보통).</summary>
        public static void SetSpeed(float value)
        {
            if (Mathf.Approximately(speed, value)) return;
            speed = value;
            Apply();
        }

        /// <summary>씬이 바뀌면 일시정지와 배속을 모두 푼다.</summary>
        public static void ResetAll()
        {
            IsPaused = false;
            speed = 1f;
            Apply();
        }

        static void Apply() => Time.timeScale = IsPaused ? 0f : speed;
    }
}
