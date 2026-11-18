using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Battle
{
    /// <summary>
    /// 스킬 버튼 (UI 시안 battle.html의 .skill): [타입 칩] 이름, 설명과 위력, 오른쪽 위 비용, 오른쪽 아래 숫자 키.
    /// 능력치 창의 스킬 목록도 같은 모양을 쓴다 (누를 수 없게).
    /// 타입 색은 임시다: 위력 0(보조) = 페리윙클, 무속성 = 시안, 속성 = 앰버. 전투 기획이 오면 타입별 색을 데이터로 뺀다.
    /// </summary>
    public sealed class BattleSkillButton : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] CanvasGroup content;
        [SerializeField] Image typeChip;
        [SerializeField] TMP_Text typeText;
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text descText;
        [SerializeField] TMP_Text costText;
        [SerializeField] TMP_Text keyText;

        public Button Button => button;

        public void Set(SkillDef s, int index, bool usable)
        {
            var color = TypeColor(s);
            typeText.text = TypeLabel(s);
            typeText.color = color;
            typeChip.color = color.WithAlpha(0.14f);
            nameText.text = s.displayName;
            string desc = s.description;
            if (s.power > 0) desc += $"  ·  위력 {s.power}";
            if (s.accuracy < 100) desc += $"  ·  명중 {s.accuracy}%";
            descText.text = desc;
            costText.text = string.IsNullOrEmpty(s.costKind) || s.costValue <= 0 ? "" : $"{s.costKind.ToUpperInvariant()} {s.costValue}";
            if (keyText != null) keyText.text = index >= 0 ? (index + 1).ToString() : "";
            if (button != null) button.interactable = usable;
            content.alpha = usable ? 1f : 0.38f;
        }

        public static string TypeLabel(SkillDef s) =>
            !string.IsNullOrEmpty(s.type) ? s.type.ToUpperInvariant() : s.power <= 0 ? "SUPPORT" : "NORMAL";

        public static Color TypeColor(SkillDef s) =>
            s.power <= 0 ? UITheme.PeriwinkleBright : string.IsNullOrEmpty(s.type) ? UITheme.Cyan : UITheme.Amber;
    }
}
