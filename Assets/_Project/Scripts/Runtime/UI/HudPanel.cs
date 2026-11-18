using ProjectVita.Story;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.UI
{
    /// <summary>
    /// 왼쪽 위 HUD: 깜빡이는 REC 표시와 지금 에피소드 ("PROLOGUE // 프롤로그").
    /// 세계관 수치(신호, 배터리 등)는 비디님 설정이 정해지면 넣는다.
    /// </summary>
    public sealed class HudPanel : MonoBehaviour
    {
        [SerializeField] Image recDot;
        [SerializeField] TMP_Text episodeText;

        Episode shown;

        void Update()
        {
            // 1.6초 주기로 깜빡인다
            if (recDot != null)
            {
                float k = Mathf.Repeat(Time.unscaledTime, 1.6f) / 1.6f;
                float a = k < 0.5f ? Mathf.Lerp(1f, 0.15f, k * 2f) : Mathf.Lerp(0.15f, 1f, (k - 0.5f) * 2f);
                recDot.color = recDot.color.WithAlpha(a);
            }

            var ep = EpisodeFlow.Current;
            if (ep != shown && episodeText != null)
            {
                shown = ep;
                episodeText.text = ep == null ? "" : $"{ep.Id.ToUpperInvariant()} <color={UITheme.ToHex(UITheme.Amber)}>//</color> {ep.DisplayName}";
            }
        }
    }
}
