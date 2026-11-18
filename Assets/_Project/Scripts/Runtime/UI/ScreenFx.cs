using System.Collections.Generic;
using ProjectVita.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.UI
{
    /// <summary>
    /// 화면 분위기 효과 (UI 시안의 디스토피아 연출): CRT 스캔라인, 필름 노이즈, 비네트, 떠다니는 육각형, 먼지 입자.
    /// 스캔라인과 노이즈는 설정에서 끌 수 있다. 일시정지 중에도 실제 시간으로 움직인다.
    /// </summary>
    public sealed class ScreenFx : MonoBehaviour
    {
        [SerializeField] RawImage scanlines;
        [SerializeField] RawImage noise;
        [SerializeField] RectTransform[] hexes;
        [SerializeField] RectTransform dustRoot;
        [SerializeField] Image dustTemplate;
        [SerializeField] int dustCount = 18;

        sealed class Dust
        {
            public RectTransform rect;
            public Image image;
            public Vector2 start;
            public float duration, time;
        }

        readonly List<Dust> dust = new List<Dust>();
        Vector2[] hexBase;
        float[] hexPhase;
        float noiseTimer;
        System.Random rnd = new System.Random();

        void Awake()
        {
            if (hexes != null)
            {
                hexBase = new Vector2[hexes.Length];
                hexPhase = new float[hexes.Length];
                for (int i = 0; i < hexes.Length; i++)
                {
                    hexBase[i] = hexes[i].anchoredPosition;
                    hexPhase[i] = i * 2.3f + 1f;
                }
            }
            if (dustTemplate != null)
            {
                dustTemplate.gameObject.SetActive(false);
                for (int i = 0; i < dustCount; i++)
                {
                    var img = Instantiate(dustTemplate, dustRoot);
                    img.gameObject.SetActive(true);
                    var d = new Dust { rect = img.rectTransform, image = img };
                    Respawn(d, true);
                    dust.Add(d);
                }
            }
        }

        void OnEnable()
        {
            GameSettings.Changed += ApplySettings;
            ApplySettings();
        }

        void OnDisable() => GameSettings.Changed -= ApplySettings;

        void ApplySettings()
        {
            if (scanlines != null) scanlines.enabled = GameSettings.Scanlines;
            if (noise != null) noise.enabled = GameSettings.FilmGrain;
            Resize();
        }

        void OnRectTransformDimensionsChange() => Resize();

        /// <summary>스캔라인은 화면 픽셀 3줄마다 1줄, 노이즈는 128px마다 반복되게 uv를 맞춘다.</summary>
        void Resize()
        {
            if (scanlines != null)
            {
                var size = scanlines.rectTransform.rect.size;
                scanlines.uvRect = new Rect(0f, 0f, size.x / 4f, size.y / 3f);
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            // 노이즈: 0.2초마다 위치를 바꿔서 지글거리게
            if (noise != null && noise.enabled)
            {
                noiseTimer -= dt;
                if (noiseTimer <= 0f)
                {
                    noiseTimer = 0.2f;
                    var size = noise.rectTransform.rect.size;
                    noise.uvRect = new Rect((float)rnd.NextDouble(), (float)rnd.NextDouble(), size.x / 128f, size.y / 128f);
                }
            }

            // 육각형: 14초 주기로 위아래로 떠다니며 살짝 돈다
            if (hexes != null)
            {
                for (int i = 0; i < hexes.Length; i++)
                {
                    float s = Mathf.Sin((now + hexPhase[i]) * Mathf.PI * 2f / 14f);
                    hexes[i].anchoredPosition = hexBase[i] + new Vector2(0f, 18f * (s * 0.5f + 0.5f));
                    hexes[i].localRotation = Quaternion.Euler(0f, 0f, -4f * (s * 0.5f + 0.5f));
                }
            }

            // 먼지: 오른쪽 위로 천천히 떠오르며 나타났다 사라진다
            foreach (var d in dust)
            {
                d.time += dt;
                if (d.time >= d.duration)
                {
                    Respawn(d, false);
                    continue;
                }
                float k = d.time / d.duration;
                d.rect.anchoredPosition = d.start + new Vector2(60f, 145f) * k;
                float a = k < 0.12f ? k / 0.12f * 0.35f : k < 0.88f ? Mathf.Lerp(0.35f, 0.2f, (k - 0.12f) / 0.76f) : Mathf.Lerp(0.2f, 0f, (k - 0.88f) / 0.12f);
                d.image.color = d.image.color.WithAlpha(a);
            }
        }

        void Respawn(Dust d, bool randomStart)
        {
            var area = dustRoot.rect.size;
            float size = 2f + (float)rnd.NextDouble() * 3f;
            d.rect.sizeDelta = new Vector2(size, size) * 1.4f;
            d.start = new Vector2((float)rnd.NextDouble() * area.x, (float)rnd.NextDouble() * area.y * 0.7f);
            d.duration = 9f + (float)rnd.NextDouble() * 10f;
            d.time = randomStart ? (float)rnd.NextDouble() * d.duration : 0f;
            d.image.color = d.image.color.WithAlpha(0f);
        }
    }
}
