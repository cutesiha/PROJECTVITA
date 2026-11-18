using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Battle
{
    /// <summary>위쪽 행동 순서 줄의 칩 하나: › [육각형] 이름. 지금 행동하는 유닛은 아군 시안, 적 앰버로 밝아진다.</summary>
    public sealed class BattleOrderChip : MonoBehaviour
    {
        [SerializeField] Image fill;
        [SerializeField] Image line;
        [SerializeField] Image hex;
        [SerializeField] TMP_Text nameText;
        [SerializeField] GameObject arrow;

        public void Set(string name, bool ally, bool now, bool first)
        {
            var accent = ally ? UITheme.Cyan : UITheme.Amber;
            nameText.text = name;
            nameText.color = now ? accent : UITheme.TextDim;
            hex.color = accent.WithAlpha(now ? 0.9f : 0.3f);
            fill.color = now ? accent.WithAlpha(0.1f) : new Color32(20, 23, 32, 200);
            line.color = now ? accent.WithAlpha(0.5f) : new Color(0, 0, 0, 0);
            arrow.SetActive(!first);
        }
    }
}
