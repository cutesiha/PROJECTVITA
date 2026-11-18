using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Battle
{
    /// <summary>능력치 창의 한 줄: 이름(영문 id), 막대, 기본값 → 지금 값, 단계(+1/-1).</summary>
    public sealed class BattleStatRow : MonoBehaviour
    {
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text idText;
        [SerializeField] RectTransform barFill;
        [SerializeField] Image barImage;
        [SerializeField] TMP_Text valueText;
        [SerializeField] TMP_Text stageText;

        public void Set(string displayName, string id, int baseValue, int current, int stage, float ratio)
        {
            nameText.text = displayName;
            idText.text = id.ToUpperInvariant();
            barFill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            barFill.offsetMax = Vector2.zero;
            var color = stage > 0 ? UITheme.Cyan : stage < 0 ? UITheme.Danger : UITheme.Periwinkle;
            barImage.color = color;
            valueText.text = current == baseValue ? baseValue.ToString() : $"<color={UITheme.ToHex(UITheme.TextDim)}>{baseValue} →</color> {current}";
            stageText.text = stage == 0 ? "" : (stage > 0 ? "+" : "") + stage;
            stageText.color = color;
        }
    }
}
