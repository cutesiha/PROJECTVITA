using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectVita.UI
{
    /// <summary>
    /// 마우스를 올렸을 때의 연출: 숨어 있던 것(왼쪽 막대, 강조 띠, 화살표)이 나타나고, 글자가 옆으로 밀리고, 색이 바뀌고, 자간이 넓어진다.
    /// 일시정지 중에도 돌도록 실제 시간으로 움직인다. 비활성화되면 원래대로 돌아간다.
    /// 같은 오브젝트의 Button이 interactable이 아니면 반응하지 않는다.
    /// </summary>
    public sealed class UIHoverFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("호버하면 나타나는 것. 알파 0에서 지금 알파로")]
        [SerializeField] Graphic[] reveal;
        [Tooltip("호버하면 움직이는 것")]
        [SerializeField] RectTransform[] shift;
        [SerializeField] Vector2 shiftBy = new Vector2(10f, 0f);
        [Tooltip("호버하면 hoverColor로 바뀌는 것")]
        [SerializeField] Graphic[] tint;
        [SerializeField] Color hoverColor = new Color(0.30f, 0.72f, 1f);
        [Tooltip("호버하면 lineHoverColor(알파 포함)로 바뀌는 것. 테두리처럼 진해져야 하는 것")]
        [SerializeField] Graphic[] lineTint;
        [SerializeField] Color lineHoverColor = new Color(0.48f, 0.51f, 0.65f, 0.4f);
        [Tooltip("호버하면 자간이 넓어지는 글자")]
        [SerializeField] TMP_Text spacing;
        [SerializeField] float spacingAdd = 8f;
        [SerializeField] float time = 0.16f;

        Selectable selectable;
        float[] revealAlpha;
        Vector2[] shiftBase;
        Color[] tintBase;
        Color[] lineBase;
        float spacingBase;
        float t, target;
        bool ready;

        void Awake() => Capture();

        void Capture()
        {
            if (ready) return;
            ready = true;
            selectable = GetComponent<Selectable>();
            revealAlpha = new float[reveal?.Length ?? 0];
            for (int i = 0; i < revealAlpha.Length; i++) revealAlpha[i] = reveal[i] != null ? reveal[i].color.a : 0f;
            shiftBase = new Vector2[shift?.Length ?? 0];
            for (int i = 0; i < shiftBase.Length; i++) shiftBase[i] = shift[i] != null ? shift[i].anchoredPosition : Vector2.zero;
            tintBase = new Color[tint?.Length ?? 0];
            for (int i = 0; i < tintBase.Length; i++) tintBase[i] = tint[i] != null ? tint[i].color : Color.white;
            lineBase = new Color[lineTint?.Length ?? 0];
            for (int i = 0; i < lineBase.Length; i++) lineBase[i] = lineTint[i] != null ? lineTint[i].color : Color.white;
            if (spacing != null) spacingBase = spacing.characterSpacing;
            Apply(0f);
        }

        /// <summary>코드가 바꾼 색(켜짐 상태 등)을 새 기본 색으로 삼는다.</summary>
        public void SetTintBase(Graphic g, Color c)
        {
            Capture();
            for (int i = 0; i < tintBase.Length; i++)
                if (tint[i] == g) tintBase[i] = c;
            for (int i = 0; i < lineBase.Length; i++)
                if (lineTint[i] == g) lineBase[i] = c;
            Apply(t);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (selectable != null && !selectable.IsInteractable()) return;
            target = 1f;
        }

        public void OnPointerExit(PointerEventData e) => target = 0f;

        void OnDisable()
        {
            target = 0f;
            t = 0f;
            if (ready) Apply(0f);
        }

        void Update()
        {
            if (selectable != null && !selectable.IsInteractable()) target = 0f;
            if (Mathf.Approximately(t, target)) return;
            t = Mathf.MoveTowards(t, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, time));
            Apply(UIAnim.EaseOut(t));
        }

        void Apply(float k)
        {
            for (int i = 0; i < revealAlpha.Length; i++)
                if (reveal[i] != null) reveal[i].color = reveal[i].color.WithAlpha(revealAlpha[i] * k);
            for (int i = 0; i < shiftBase.Length; i++)
                if (shift[i] != null) shift[i].anchoredPosition = shiftBase[i] + shiftBy * k;
            for (int i = 0; i < tintBase.Length; i++)
                if (tint[i] != null) tint[i].color = Color.Lerp(tintBase[i], hoverColor.WithAlpha(tintBase[i].a), k);
            for (int i = 0; i < lineBase.Length; i++)
                if (lineTint[i] != null) lineTint[i].color = Color.Lerp(lineBase[i], lineHoverColor, k);
            if (spacing != null) spacing.characterSpacing = spacingBase + spacingAdd * k;
        }
    }
}
