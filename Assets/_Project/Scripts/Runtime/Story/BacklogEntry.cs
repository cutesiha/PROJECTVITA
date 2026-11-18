using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Story
{
    /// <summary>대사 기록 한 줄: ▸ 화자 // 칩, 그 아래 대사. 나레이션은 화자 줄 없이 흐린 색.</summary>
    public sealed class BacklogEntry : MonoBehaviour
    {
        [SerializeField] GameObject nameRow;
        [SerializeField] Image marker;
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text bodyText;

        public void Set(string name, Color nameColor, string body, Color bodyColor)
        {
            bool hasName = !string.IsNullOrEmpty(name);
            nameRow.SetActive(hasName);
            nameText.text = name ?? string.Empty;
            nameText.color = nameColor;
            marker.color = nameColor;
            bodyText.text = body;
            bodyText.color = bodyColor;
        }
    }
}
