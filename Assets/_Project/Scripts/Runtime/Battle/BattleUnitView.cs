using System;
using System.Collections.Generic;
using System.Text;
using ProjectVita.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectVita.Battle
{
    /// <summary>
    /// 전투 유닛 하나의 화면: 그림, 이름, HP 바, 게이지와 상태이상, 데미지 숫자, 흔들림.
    /// 대상 고르기 중에는 클릭할 수 있다.
    /// </summary>
    public sealed class BattleUnitView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] RectTransform body;
        [SerializeField] Image portrait;
        [SerializeField] TMP_Text portraitLabel;
        [SerializeField] TMP_Text nameText;
        [SerializeField] RectTransform hpFill;
        [SerializeField] TMP_Text hpText;
        [SerializeField] TMP_Text infoText;
        [SerializeField] Outline frame; // 카드 테두리 (UI Outline 효과)
        [SerializeField] TMP_Text popupTemplate;

        static readonly Color FrameIdle = new Color(1f, 1f, 1f, 0f);
        static readonly Color FrameActing = new Color(0.95f, 0.85f, 0.3f, 0.9f);
        static readonly Color FrameSelectable = new Color(0.70f, 0.15f, 0.12f, 0.6f);
        static readonly Color FrameHover = new Color(1f, 0.3f, 0.25f, 1f);

        public Unit Unit { get; private set; }

        Action<BattleUnitView> onClick;
        bool selectable, acting, hover;
        float shownHp;
        Vector2 bodyBase;
        System.Threading.CancellationTokenSource hpCts, shakeCts;

        public void Bind(Unit unit)
        {
            Unit = unit;
            name = $"Unit_{unit.Side}_{unit.Slot}";
            nameText.text = unit.Name;
            var sprite = AssetCatalog.Instance.FindBattlePortrait(unit.Def.portraitId);
            portrait.sprite = sprite;
            portrait.preserveAspect = true;
            portrait.color = sprite != null ? Color.white : new Color(0.25f, 0.27f, 0.32f);
            portraitLabel.text = sprite != null ? string.Empty : unit.Def.id;
            popupTemplate.gameObject.SetActive(false);
            bodyBase = body.anchoredPosition;
            shownHp = unit.Hp;
            ApplyHp(shownHp);
            RefreshInfo();
            RefreshFrame();
        }

        public void RefreshInfo()
        {
            if (Unit == null) return;
            var sb = new StringBuilder();
            foreach (var g in Unit.Gauges) sb.Append(g.Key.ToUpperInvariant()).Append(' ').Append(g.Value).Append("  ");
            foreach (var s in Unit.Statuses) sb.Append("<color=#E0B040>").Append(s.Def.displayName).Append('(').Append(s.TurnsLeft).Append(")</color> ");
            foreach (var st in Unit.Stages)
                if (st.Value != 0) sb.Append(st.Key.ToUpperInvariant()).Append(st.Value > 0 ? "+" : "").Append(st.Value).Append(' ');
            infoText.text = sb.ToString();
            portrait.canvasRenderer.SetAlpha(Unit.IsDown ? 0.25f : 1f);
        }

        public async Awaitable AnimateHp(int hp, float time)
        {
            var token = Tween.Restart(ref hpCts, destroyCancellationToken);
            float from = shownHp;
            await Tween.Run(time, t => ApplyHp(Mathf.Lerp(from, hp, Tween.EaseOut(t))), token);
        }

        void ApplyHp(float hp)
        {
            shownHp = hp;
            float ratio = Unit.MaxHp <= 0 ? 0f : Mathf.Clamp01(hp / Unit.MaxHp);
            hpFill.anchorMax = new Vector2(ratio, 1f);
            hpFill.offsetMax = Vector2.zero;
            var img = hpFill.GetComponent<Image>();
            if (img != null) img.color = ratio > 0.5f ? new Color(0.35f, 0.8f, 0.4f) : ratio > 0.2f ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.85f, 0.2f, 0.15f);
            hpText.text = $"{Mathf.RoundToInt(hp)} / {Unit.MaxHp}";
        }

        public void SetActing(bool on)
        {
            acting = on;
            RefreshFrame();
        }

        public void SetSelectable(bool on, Action<BattleUnitView> clicked)
        {
            selectable = on;
            onClick = on ? clicked : null;
            if (!on) hover = false;
            RefreshFrame();
        }

        void RefreshFrame()
        {
            if (frame == null) return;
            frame.effectColor = selectable ? (hover ? FrameHover : FrameSelectable) : acting ? FrameActing : FrameIdle;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (selectable && eventData.button == PointerEventData.InputButton.Left) onClick?.Invoke(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hover = true;
            RefreshFrame();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hover = false;
            RefreshFrame();
        }

        public void Shake(float strength, float time)
        {
            var token = Tween.Restart(ref shakeCts, destroyCancellationToken);
            Tween.Forget(Tween.Run(time, t =>
            {
                body.anchoredPosition = t >= 1f ? bodyBase : bodyBase + new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-0.5f, 0.5f)) * strength * (1f - t);
            }, token));
        }

        /// <summary>앞으로 살짝 나왔다가 돌아온다 (행동할 때).</summary>
        public void Lunge(float distance)
        {
            var token = Tween.Restart(ref shakeCts, destroyCancellationToken);
            Tween.Forget(Tween.Run(0.25f, t =>
            {
                body.anchoredPosition = bodyBase + new Vector2(0f, distance * Mathf.Sin(t * Mathf.PI));
            }, token));
        }

        /// <summary>숫자나 글자가 떠올랐다가 사라진다.</summary>
        public void Popup(string text, Color color, float size = 56f)
        {
            var label = Instantiate(popupTemplate, popupTemplate.transform.parent);
            label.gameObject.SetActive(true);
            label.text = text;
            label.color = color;
            label.fontSize = size;
            var rt = label.rectTransform;
            var start = rt.anchoredPosition + new Vector2(UnityEngine.Random.Range(-30f, 30f), 0f);
            var token = destroyCancellationToken;
            Tween.Forget(PopupRoutine(label, rt, start, color, token));
        }

        static async Awaitable PopupRoutine(TMP_Text label, RectTransform rt, Vector2 start, Color color, System.Threading.CancellationToken token)
        {
            await Tween.Run(0.9f, t =>
            {
                if (label == null) return;
                rt.anchoredPosition = start + new Vector2(0f, 90f * Tween.EaseOut(t));
                label.color = new Color(color.r, color.g, color.b, t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f);
            }, token);
            if (label != null) Destroy(label.gameObject);
        }
    }
}
