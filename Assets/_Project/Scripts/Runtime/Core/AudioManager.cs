using System.Threading;
using UnityEngine;

namespace ProjectVita.Core
{
    /// <summary>
    /// BGM과 효과음. 씬이 바뀌어도 살아 있어서(DontDestroyOnLoad) 에피소드가 바뀌어도 BGM이 끊기지 않는다.
    /// BGM은 두 AudioSource를 번갈아 써서 크로스페이드한다.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        static AudioManager instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static AudioManager Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[AudioManager]");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<AudioManager>();
                }
                return instance;
            }
        }

        AudioSource bgmA, bgmB, sfx;
        float levelA, levelB; // 페이드 진행값 (0~1). 실제 음량 = 진행값 × 설정 음량
        AudioSource current;
        CancellationTokenSource bgmCts;

        /// <summary>지금 재생 중인 BGM 이름. 없으면 빈 문자열. 세이브에 쓴다.</summary>
        public string CurrentBgm { get; private set; } = string.Empty;

        void Awake()
        {
            bgmA = CreateSource("BGM_A", true);
            bgmB = CreateSource("BGM_B", true);
            sfx = CreateSource("SFX", false);
            GameSettings.Changed += ApplyVolumes;
        }

        void OnDestroy() => GameSettings.Changed -= ApplyVolumes;

        AudioSource CreateSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            return src;
        }

        void ApplyVolumes()
        {
            bgmA.volume = levelA * GameSettings.BgmVolume;
            bgmB.volume = levelB * GameSettings.BgmVolume;
        }

        /// <summary>BGM을 바꾼다. 같은 곡이 이미 나오고 있으면 그대로 둔다.</summary>
        public void PlayBgm(string name, float fade)
        {
            if (string.IsNullOrEmpty(name) || name == "stop" || name == "off")
            {
                StopBgm(fade);
                return;
            }
            if (string.Equals(name, CurrentBgm, System.StringComparison.OrdinalIgnoreCase) && current != null && current.isPlaying) return;

            var clip = AssetCatalog.Instance.FindBgm(name);
            if (clip == null)
            {
                Debug.LogWarning($"[Audio] BGM '{name}'이 없다. Audio/BGM 폴더에 {name}.wav/.ogg/.mp3를 넣는다.");
                return;
            }

            var from = current;
            var to = current == bgmA ? bgmB : bgmA;
            to.clip = clip;
            SetLevel(to, 0f);
            to.Play();
            current = to;
            CurrentBgm = name;
            Tween.Forget(Crossfade(from, to, fade));
        }

        public void StopBgm(float fade)
        {
            var from = current;
            current = null;
            CurrentBgm = string.Empty;
            Tween.Forget(Crossfade(from, null, fade));
        }

        async Awaitable Crossfade(AudioSource from, AudioSource to, float fade)
        {
            var token = Tween.Restart(ref bgmCts, destroyCancellationToken);
            float fromStart = Level(from);
            float toStart = Level(to);
            // 화면 전환이나 일시정지와 관계없이 소리는 실제 시간으로 페이드한다.
            await Tween.Run(fade, t =>
            {
                if (from != null) SetLevel(from, Mathf.Lerp(fromStart, 0f, t));
                if (to != null) SetLevel(to, Mathf.Lerp(toStart, 1f, t));
            }, token, unscaled: true);

            if (!token.IsCancellationRequested && from != null && from != current) from.Stop();
        }

        float Level(AudioSource s) => s == bgmA ? levelA : s == bgmB ? levelB : 0f;

        void SetLevel(AudioSource s, float level)
        {
            if (s == bgmA) levelA = level;
            else if (s == bgmB) levelB = level;
            s.volume = level * GameSettings.BgmVolume;
        }

        public void PlaySfx(string name, float volume = 1f)
        {
            var clip = AssetCatalog.Instance.FindSfx(name);
            if (clip == null)
            {
                Debug.LogWarning($"[Audio] 효과음 '{name}'이 없다. Audio/SFX 폴더에 {name}.wav/.ogg/.mp3를 넣는다.");
                return;
            }
            sfx.PlayOneShot(clip, Mathf.Clamp01(volume) * GameSettings.SfxVolume);
        }
    }
}
