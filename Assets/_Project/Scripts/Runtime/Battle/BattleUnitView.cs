using System;
using System.Collections.Generic;
using System.Threading;
using ProjectVita.Core;
using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectVita.Battle
{
    /// <summary>
    /// 전투 유닛 카드 (UI 시안 battle.html의 유닛 정보창): 그림, [육각형] 이름 / id / 타입, HP 바(눈금 + 늦게 따라오는 붉은 잔상), 게이지, 상태 칩.
    /// 연출: 등장, 돌진, 피격(흰 번쩍임 + 붉은 기 + 밀려남), 회복(초록 번쩍임), 쓰러짐(흐려지며 가라앉음), 데미지 숫자.
    /// 대상 고르기 중에는 누르면 대상, 그 밖에는 누르면(또는 오른쪽 클릭) 능력치 창이 열린다.
    /// </summary>
    public sealed class BattleUnitView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] RectTransform body;
        [SerializeField] Image portrait;
        [SerializeField] TMP_Text portraitLabel;
        [SerializeField] Image hitFlash;
        [SerializeField] Image damageTint;
        [SerializeField] Image accentBar;
        [SerializeField] Image hexIcon;
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text idText;
        [SerializeField] TMP_Text typeText;
        [SerializeField] RectTransform hpFill;
        [SerializeField] RectTransform hpGhost;
        [SerializeField] TMP_Text hpText;
        [SerializeField] RectTransform gaugeFill;
        [SerializeField] TMP_Text gaugeText;
        [SerializeField] RectTransform statusRoot;
        [SerializeField] TMP_Text statusTemplate;
        [SerializeField] Graphic frame; // 카드 바깥 테두리 그림 (색만 바꾼다)
        [SerializeField] TMP_Text popupTemplate;
        [SerializeField] TMP_FontAsset numberFont;
        [SerializeField] TMP_FontAsset labelFont;

        // 테두리: 행동 중 = 앰버, 고를 수 있음 = 시안, 마우스 올림 = 밝은 시안, 그냥 마우스 올림 = 옅은 페리윙클
        static readonly Color FrameIdle = new Color(0, 0, 0, 0);
        static readonly Color FrameActing = new Color(0.91f, 0.53f, 0.23f, 0.95f);
        static readonly Color FrameSelectable = new Color(0.30f, 0.72f, 1f, 0.5f);
        static readonly Color FrameHover = new Color(0.55f, 0.85f, 1f, 1f);
        static readonly Color FrameInspect = new Color(0.60f, 0.64f, 0.78f, 0.45f);

        public Unit Unit { get; private set; }

        Action<BattleUnitView> onSelect, onInspect;
        bool selectable, acting, hover;
        float shownHp, ghostHp;
        Vector2 bodyBase;
        Color portraitBase = Color.white;
        CancellationTokenSource hpCts, moveCts, flashCts, introCts;
        readonly List<TMP_Text> chips = new List<TMP_Text>();

        public static Color HpColor(float ratio) => ratio > 0.5f ? UITheme.Cyan : ratio > 0.25f ? UITheme.Amber : UITheme.Danger;

        public void Bind(Unit unit, Action<BattleUnitView> inspect)
        {
            Unit = unit;
            onInspect = inspect;
            name = $"Unit_{unit.Side}_{unit.Slot}";
            bool ally = unit.Side == Side.Ally;
            var accent = ally ? UITheme.Cyan : UITheme.Amber;

            nameText.text = unit.Name;
            idText.text = unit.Def.id.ToUpperInvariant();
            typeText.text = string.IsNullOrEmpty(unit.Def.type) ? "—" : unit.Def.type.ToUpperInvariant();
            accentBar.color = accent;
            hexIcon.color = accent.WithAlpha(0.85f);

            var sprite = AssetCatalog.Instance.FindBattlePortrait(unit.Def.portraitId);
            portrait.sprite = sprite;
            portrait.preserveAspect = true;
            portraitBase = sprite != null ? Color.white : new Color(0.12f, 0.13f, 0.17f);
            portrait.color = portraitBase;
            portraitLabel.text = sprite != null ? string.Empty : unit.Def.id;
            popupTemplate.gameObject.SetActive(false);
            statusTemplate.gameObject.SetActive(false);
            hitFlash.color = new Color(1, 1, 1, 0);
            damageTint.color = UITheme.Danger.WithAlpha(0f);
            bodyBase = body.anchoredPosition;
            shownHp = ghostHp = unit.Hp;
            ApplyHp(shownHp, ghostHp);
            RefreshInfo();
            RefreshFrame();
        }

        // ================================================================ 정보

        public void RefreshInfo()
        {
            if (Unit == null) return;

            // 게이지: 첫 번째 게이지 (기력 등)
            string gaugeLabel = "";
            float gaugeRatio = 0f;
            foreach (var g in Unit.Gauges)
            {
                int max = Unit.GaugeMax.TryGetValue(g.Key, out int m) ? m : g.Value;
                gaugeLabel = $"{g.Key.ToUpperInvariant()}  {g.Value} / {max}";
                gaugeRatio = max <= 0 ? 0f : Mathf.Clamp01((float)g.Value / max);
                break;
            }
            gaugeText.text = gaugeLabel;
            gaugeFill.anchorMax = new Vector2(gaugeRatio, 1f);
            gaugeFill.offsetMax = Vector2.zero;
            gaugeFill.parent.gameObject.SetActive(gaugeLabel.Length > 0);

            // 상태 칩: 상태이상(앰버), 능력 단계(시안 +, 빨강 -)
            var items = new List<(string, Color)>();
            foreach (var s in Unit.Statuses) items.Add(($"{s.Def.displayName} {s.TurnsLeft}", UITheme.Amber));
            foreach (var st in Unit.Stages)
                if (st.Value != 0) items.Add(($"{st.Key.ToUpperInvariant()} {(st.Value > 0 ? "+" : "")}{st.Value}", st.Value > 0 ? UITheme.Cyan : UITheme.Danger));
            while (chips.Count < items.Count)
            {
                var c = Instantiate(statusTemplate, statusRoot);
                chips.Add(c);
            }
            for (int i = 0; i < chips.Count; i++)
            {
                bool used = i < items.Count;
                chips[i].gameObject.SetActive(used);
                if (!used) continue;
                chips[i].text = items[i].Item1;
                chips[i].color = items[i].Item2;
                var border = chips[i].GetComponentInChildren<Image>(true);
                if (border != null) border.color = items[i].Item2.WithAlpha(0.5f);
            }
        }

        public async Awaitable AnimateHp(int hp, float time)
        {
            var token = UIAnim.Restart(ref hpCts, destroyCancellationToken);
            float from = shownHp, ghostFrom = ghostHp;
            if (hp >= shownHp)
            {
                // 회복: 잔상은 바로 맞추고 막대가 차오른다.
                ghostHp = hp;
                await UIAnim.Run(time, t => ApplyHp(Mathf.Lerp(from, hp, UIAnim.EaseOut(t)), hp), token);
                return;
            }
            // 피해: 막대가 먼저 줄고, 붉은 잔상이 잠시 뒤 천천히 따라온다.
            Tween.Forget(UIAnim.Run(0.9f, t => { ghostHp = Mathf.Lerp(ghostFrom, hp, UIAnim.EaseInOut(t)); ApplyHp(shownHp, ghostHp); }, token, 0.3f));
            await UIAnim.Run(time, t => ApplyHp(Mathf.Lerp(from, hp, UIAnim.EaseOut(t)), ghostHp), token);
        }

        void ApplyHp(float hp, float ghost)
        {
            shownHp = hp;
            float ratio = Unit.MaxHp <= 0 ? 0f : Mathf.Clamp01(hp / Unit.MaxHp);
            float ghostRatio = Unit.MaxHp <= 0 ? 0f : Mathf.Clamp01(Mathf.Max(ghost, hp) / Unit.MaxHp);
            hpFill.anchorMax = new Vector2(ratio, 1f);
            hpFill.offsetMax = Vector2.zero;
            hpGhost.anchorMax = new Vector2(ghostRatio, 1f);
            hpGhost.offsetMax = Vector2.zero;
            var img = hpFill.GetComponent<Image>();
            if (img != null) img.color = HpColor(ratio);
            hpText.text = $"{Mathf.Max(0, Mathf.RoundToInt(hp))} <color={UITheme.ToHex(UITheme.TextDim)}>/</color> {Unit.MaxHp}";
        }

        // ================================================================ 테두리, 입력

        public void SetActing(bool on)
        {
            acting = on;
            RefreshFrame();
        }

        public void SetSelectable(bool on, Action<BattleUnitView> clicked)
        {
            selectable = on;
            onSelect = on ? clicked : null;
            RefreshFrame();
        }

        void RefreshFrame()
        {
            if (frame == null) return;
            frame.color = selectable ? (hover ? FrameHover : FrameSelectable)
                : acting ? FrameActing
                : hover && onInspect != null ? FrameInspect : FrameIdle;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (selectable && e.button == PointerEventData.InputButton.Left) onSelect?.Invoke(this);
            else if (!selectable && (e.button == PointerEventData.InputButton.Left || e.button == PointerEventData.InputButton.Right)) onInspect?.Invoke(this);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            hover = true;
            RefreshFrame();
        }

        public void OnPointerExit(PointerEventData e)
        {
            hover = false;
            RefreshFrame();
        }

        // ================================================================ 연출

        /// <summary>등장: from만큼 떨어진 곳에서 미끄러져 들어오며 나타난다.</summary>
        public void Intro(Vector2 from, float delay)
        {
            var token = UIAnim.Restart(ref introCts, destroyCancellationToken);
            group.alpha = 0f;
            body.anchoredPosition = bodyBase + from;
            Tween.Forget(UIAnim.Run(0.5f, t =>
            {
                float e = UIAnim.EaseOut(t);
                group.alpha = e;
                body.anchoredPosition = bodyBase + from * (1f - e);
            }, token, delay));
        }

        /// <summary>대상 쪽으로 튀어나갔다 돌아온다 (아군은 위로, 적은 아래로).</summary>
        public void Lunge()
        {
            var token = UIAnim.Restart(ref moveCts, destroyCancellationToken);
            var dir = Unit.Side == Side.Ally ? new Vector2(18f, 46f) : new Vector2(-18f, -46f);
            Tween.Forget(UIAnim.Run(0.36f, t =>
            {
                float k = t < 0.45f ? UIAnim.EaseOut(t / 0.45f) : 1f - UIAnim.EaseInOut((t - 0.45f) / 0.55f);
                body.anchoredPosition = bodyBase + dir * k;
            }, token));
        }

        /// <summary>피격: 그림이 하얗게 번쩍이고, 카드가 붉어지고, 반대쪽으로 밀렸다가 흔들리며 돌아온다.</summary>
        public void Hit(bool critical)
        {
            var flashToken = UIAnim.Restart(ref flashCts, destroyCancellationToken);
            float tint = critical ? 0.55f : 0.38f;
            Tween.Forget(UIAnim.Run(critical ? 0.55f : 0.42f, t =>
            {
                hitFlash.color = new Color(1f, 1f, 1f, 0.9f * (1f - UIAnim.EaseOut(t)));
                damageTint.color = UITheme.Danger.WithAlpha(tint * (1f - t));
            }, flashToken));

            if (!GameSettings.ScreenShake) return;
            var token = UIAnim.Restart(ref moveCts, destroyCancellationToken);
            float push = critical ? 26f : 16f;
            var away = Unit.Side == Side.Ally ? new Vector2(0f, -1f) : new Vector2(0f, 1f);
            Tween.Forget(UIAnim.Run(critical ? 0.5f : 0.4f, t =>
            {
                float decay = 1f - t;
                float x = Mathf.Sin(t * 38f) * push * decay;
                float y = (t < 0.15f ? t / 0.15f : decay) * push * 0.6f;
                body.anchoredPosition = bodyBase + new Vector2(x, 0f) + away * y;
            }, token));
        }

        public void HealFx()
        {
            var token = UIAnim.Restart(ref flashCts, destroyCancellationToken);
            Tween.Forget(UIAnim.Run(0.5f, t =>
            {
                hitFlash.color = new Color(0.45f, 1f, 0.65f, 0.55f * (1f - t));
            }, token));
        }

        /// <summary>쓰러짐: 그림이 회색으로 바래며 아래로 가라앉고 작아진다.</summary>
        public void Down()
        {
            var token = UIAnim.Restart(ref moveCts, destroyCancellationToken);
            Tween.Forget(UIAnim.Run(0.8f, t =>
            {
                float e = UIAnim.EaseInOut(t);
                group.alpha = Mathf.Lerp(1f, 0.35f, e);
                body.anchoredPosition = bodyBase + new Vector2(0f, -30f * e);
                body.localScale = Vector3.one * Mathf.Lerp(1f, 0.92f, e);
                portrait.color = Color.Lerp(portraitBase, new Color(0.3f, 0.3f, 0.33f), e);
            }, token));
        }

        /// <summary>다시 싸울 때 쓰러진 모습을 되돌린다.</summary>
        public void ResetPose()
        {
            group.alpha = 1f;
            body.anchoredPosition = bodyBase;
            body.localScale = Vector3.one;
            portrait.color = portraitBase;
        }

        public enum PopupKind { Damage, Critical, Heal, Miss, Label }

        /// <summary>숫자나 글자가 튀어나왔다가(커졌다 작아짐) 위로 떠오르며 사라진다.</summary>
        public void Popup(string text, Color color, PopupKind kind, float yOffset = 0f)
        {
            var label = Instantiate(popupTemplate, popupTemplate.transform.parent);
            label.gameObject.SetActive(true);
            label.text = text;
            label.color = color;
            bool number = kind == PopupKind.Damage || kind == PopupKind.Critical || kind == PopupKind.Heal;
            if (number && numberFont != null) label.font = numberFont;
            if (!number && labelFont != null) label.font = labelFont;
            label.fontSize = kind switch
            {
                PopupKind.Critical => 78f,
                PopupKind.Damage => 58f,
                PopupKind.Heal => 52f,
                PopupKind.Miss => 30f,
                _ => 26f,
            };
            label.characterSpacing = number ? 4f : 24f;
            var rt = label.rectTransform;
            var start = rt.anchoredPosition + new Vector2(UnityEngine.Random.Range(-34f, 34f), yOffset);
            Tween.Forget(PopupRoutine(label, rt, start, color, destroyCancellationToken));
        }

        static async Awaitable PopupRoutine(TMP_Text label, RectTransform rt, Vector2 start, Color color, CancellationToken token)
        {
            await UIAnim.Run(1.05f, t =>
            {
                if (label == null) return;
                float scale = t < 0.15f ? Mathf.Lerp(0.8f, 1.18f, t / 0.15f) : t < 0.3f ? Mathf.Lerp(1.18f, 1f, (t - 0.15f) / 0.15f) : 1f;
                rt.localScale = Vector3.one * scale;
                rt.anchoredPosition = start + new Vector2(0f, t < 0.3f ? 10f * (t / 0.3f) : 10f + 64f * UIAnim.EaseOut((t - 0.3f) / 0.7f));
                float a = t < 0.1f ? t / 0.1f : t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f;
                label.color = color.WithAlpha(a);
            }, token);
            if (label != null) Destroy(label.gameObject);
        }
    }
}
