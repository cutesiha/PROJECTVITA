using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectVita.UI
{
    /// <summary>켜고 끄는 스위치. 마름모 손잡이가 옆으로 미끄러지고, 켜지면 시안색으로 빛난다.</summary>
    public sealed class UIToggleSwitch : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] Image fill;
        [SerializeField] Image line;
        [SerializeField] RectTransform knob;
        [SerializeField] Image knobImage;
        [SerializeField] Image knobGlow;
        [SerializeField] float knobOffX = -20f;
        [SerializeField] float knobOnX = 20f;

        public bool IsOn { get; private set; }
        public event Action<bool> Changed;

        float t;
        bool ready;

        void Awake()
        {
            ready = true;
            Apply(IsOn ? 1f : 0f);
        }

        public void SetWithoutNotify(bool on)
        {
            IsOn = on;
            t = on ? 1f : 0f;
            if (ready) Apply(t);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            IsOn = !IsOn;
            Changed?.Invoke(IsOn);
        }

        void Update()
        {
            float target = IsOn ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;
            t = Mathf.MoveTowards(t, target, Time.unscaledDeltaTime / 0.2f);
            Apply(UIAnim.EaseInOut(t));
        }

        void Apply(float k)
        {
            if (knob != null) knob.anchoredPosition = new Vector2(Mathf.Lerp(knobOffX, knobOnX, k), knob.anchoredPosition.y);
            if (fill != null) fill.color = Color.Lerp(UITheme.ToggleOff, UITheme.CyanDim, k);
            if (line != null) line.color = Color.Lerp(UITheme.LineDim, UITheme.CyanLine, k);
            if (knobImage != null) knobImage.color = Color.Lerp(UITheme.TextDim, UITheme.Cyan, k);
            if (knobGlow != null) knobGlow.color = UITheme.Cyan.WithAlpha(0.45f * k);
        }
    }
}
