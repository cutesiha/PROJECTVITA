using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.UI
{
    /// <summary>천천히 깜빡인다 (시안의 blink 애니메이션). 실제 시간 기준.</summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class UIBlink : MonoBehaviour
    {
        [SerializeField] float period = 2f;
        [SerializeField] float minAlpha = 0.25f;

        Graphic graphic;
        float baseAlpha;

        void Awake()
        {
            graphic = GetComponent<Graphic>();
            baseAlpha = graphic.color.a;
        }

        void Update()
        {
            float k = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / period);
            graphic.color = graphic.color.WithAlpha(baseAlpha * Mathf.Lerp(minAlpha, 1f, k));
        }
    }
}
