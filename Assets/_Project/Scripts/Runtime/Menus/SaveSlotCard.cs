using ProjectVita.Story;
using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// 세이브 슬롯 카드 하나: 썸네일, SLOT 01 / USED, 에피소드(장소), 마지막 대사, 저장 시각, 저장 모드면 OVERWRITE 표시.
    /// 불러오기 모드에서 빈 슬롯은 흐리게 보인다.
    /// </summary>
    public sealed class SaveSlotCard : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] CanvasGroup group;
        [SerializeField] RawImage thumbnail;
        [SerializeField] TMP_Text thumbnailLabel;
        [SerializeField] TMP_Text slotText;
        [SerializeField] TMP_Text statusText;
        [SerializeField] TMP_Text chapterText;
        [SerializeField] TMP_Text previewText;
        [SerializeField] TMP_Text timeText;
        [SerializeField] TMP_Text badgeText;

        static readonly Color EmptyThumb = new Color(0.05f, 0.055f, 0.07f, 1f);

        public Button Button => button;

        public void Set(int slot, SaveSystem.SaveData data, Texture texture, bool saveMode, bool interactable)
        {
            bool auto = slot == SaveSystem.AutoSlot;
            slotText.text = auto ? "AUTO SAVE" : $"SLOT {slot:00}";
            statusText.text = data != null ? "USED" : "EMPTY";

            if (data != null)
            {
                string where = string.IsNullOrEmpty(data.locationName) ? "" : $"  ·  {data.locationName}";
                chapterText.text = Escape((data.episodeName ?? data.episodeId) + where);
                chapterText.color = UITheme.Text;
                previewText.text = string.IsNullOrEmpty(data.preview) ? "" : Escape(data.preview);
                previewText.color = UITheme.TextDim;
                timeText.text = data.savedAt;
            }
            else
            {
                chapterText.text = "—";
                chapterText.color = UITheme.TextDim;
                previewText.text = saveMode ? (auto ? "자동 저장은 장면이 바뀔 때마다 기록됩니다." : "이 슬롯에 기록합니다.") : "기록 없음";
                previewText.color = UITheme.TextFaint;
                timeText.text = saveMode ? "" : "NO DATA";
            }

            badgeText.gameObject.SetActive(saveMode && data != null && !auto);
            thumbnail.texture = texture;
            thumbnail.color = texture != null ? Color.white : EmptyThumb;
            thumbnailLabel.text = texture != null ? "" : auto ? "AUTO" : data != null ? "NO IMAGE" : "";
            button.interactable = interactable;
            // 불러오기 모드의 빈 슬롯, 저장 모드의 자동 저장은 흐리게
            group.alpha = interactable ? 1f : 0.42f;
        }

        static string Escape(string s) => string.IsNullOrEmpty(s) ? s : "<noparse>" + s + "</noparse>";
    }
}
