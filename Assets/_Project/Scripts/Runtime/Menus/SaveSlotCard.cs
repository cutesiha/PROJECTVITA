using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>세이브 슬롯 화면의 카드 하나: 썸네일, 슬롯 이름, 정보.</summary>
    public sealed class SaveSlotCard : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] RawImage thumbnail;
        [SerializeField] TMP_Text thumbnailLabel;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text info;

        static readonly Color EmptyThumb = new Color(0.04f, 0.045f, 0.055f, 1f);

        public Button Button => button;

        public void Set(string titleText, string infoText, Texture texture, string placeholder, bool interactable)
        {
            title.text = titleText;
            info.text = infoText;
            thumbnail.texture = texture;
            thumbnail.color = texture != null ? Color.white : EmptyThumb;
            thumbnailLabel.text = texture != null ? string.Empty : placeholder;
            button.interactable = interactable;
        }
    }
}
