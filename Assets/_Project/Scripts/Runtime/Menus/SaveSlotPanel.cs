using System;
using System.Collections.Generic;
using ProjectVita.Story;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// 세이브 슬롯 화면 (문서 4-7). 저장 모드와 불러오기 모드가 있다.
    /// 카드 0번은 자동 저장(불러오기만), 1~7번은 수동 슬롯이다.
    /// 이미 있는 슬롯에 저장하거나 게임 중에 불러올 때는 한 번 더 눌러야 실행한다.
    /// 타이틀과 일시정지 메뉴가 같이 쓴다.
    /// </summary>
    public sealed class SaveSlotPanel : MonoBehaviour
    {
        public enum Mode { Save, Load }

        [SerializeField] GameObject root;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text hintText;
        [SerializeField] RectTransform grid;
        [SerializeField] SaveSlotCard cardTemplate;
        [SerializeField] Button closeButton;

        const string Accent = "#E5534B";
        const string Sub = "#9AA0A6";

        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>불러오기를 시작했다 (씬 로드 중). 일시정지 메뉴가 화면을 정리한다.</summary>
        public event Action Loaded;

        Mode mode;
        bool inGame;
        Texture2D saveThumbnail;
        int confirmSlot = -1;
        readonly List<SaveSlotCard> cards = new List<SaveSlotCard>();
        readonly List<Texture2D> loadedThumbnails = new List<Texture2D>();

        void Awake()
        {
            cardTemplate.gameObject.SetActive(false);
            root.SetActive(false);
            closeButton.onClick.AddListener(Close);
        }

        void OnDestroy() => ReleaseThumbnails();

        /// <param name="inGame">게임 중(일시정지 메뉴)이면 true. 불러올 때 한 번 더 확인한다.</param>
        /// <param name="thumbnail">저장할 때 슬롯에 넣을 화면 (일시정지 메뉴를 열기 직전에 찍은 것). 없어도 된다.</param>
        public void Open(Mode mode, bool inGame, Texture2D thumbnail)
        {
            this.mode = mode;
            this.inGame = inGame;
            saveThumbnail = thumbnail;
            confirmSlot = -1;
            titleText.text = mode == Mode.Save ? "저장" : "불러오기";
            hintText.text = mode == Mode.Save
                ? "불러오면 저장한 장면의 처음부터 다시 시작한다."
                : "자동 저장은 장면이 바뀔 때마다 덮어쓴다.";
            Build();
            root.SetActive(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            ReleaseThumbnails();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void Build()
        {
            ReleaseThumbnails();
            while (cards.Count <= SaveSystem.SlotCount)
            {
                int slot = cards.Count;
                var card = Instantiate(cardTemplate, grid);
                card.gameObject.SetActive(true);
                card.name = slot == SaveSystem.AutoSlot ? "Slot_Auto" : $"Slot_{slot}";
                card.Button.onClick.AddListener(() => OnClicked(slot));
                cards.Add(card);
            }
            for (int slot = 0; slot <= SaveSystem.SlotCount; slot++) Refresh(slot, loadThumbnail: true);
        }

        void Refresh(int slot, bool loadThumbnail)
        {
            var card = cards[slot];
            var data = SaveSystem.Load(slot);
            string title = slot == SaveSystem.AutoSlot ? "자동 저장" : $"슬롯 {slot}";

            Texture2D thumb = null;
            if (loadThumbnail && data != null)
            {
                thumb = SaveSystem.LoadThumbnail(slot);
                if (thumb != null) loadedThumbnails.Add(thumb);
            }
            else if (!loadThumbnail)
            {
                thumb = card.GetComponentInChildren<RawImage>(true).texture as Texture2D;
            }

            string info;
            if (data == null)
            {
                info = $"<color={Sub}>비어 있음</color>";
            }
            else
            {
                string where = string.IsNullOrEmpty(data.locationName) ? "" : $" · {data.locationName}";
                info = $"{Escape(data.episodeName ?? data.episodeId)}{Escape(where)}\n<size=82%><color={Sub}>{data.savedAt}</color></size>";
                if (!string.IsNullOrEmpty(data.preview)) info += $"\n<size=82%>{Escape(data.preview)}</size>";
            }
            if (slot == confirmSlot)
            {
                info = mode == Mode.Save
                    ? $"<color={Accent}><b>한 번 더 누르면 덮어쓴다</b></color>\n" + info
                    : $"<color={Accent}><b>한 번 더 누르면 불러온다</b></color>\n<size=82%><color={Sub}>지금 진행은 저장되지 않는다</color></size>";
            }

            bool interactable = mode == Mode.Save
                ? slot != SaveSystem.AutoSlot && SaveSystem.CanSaveNow
                : data != null;
            card.Set(title, info, thumb, slot == SaveSystem.AutoSlot ? "AUTO" : (data == null ? "" : "NO IMAGE"), interactable);
        }

        void OnClicked(int slot)
        {
            EventSystem.current?.SetSelectedGameObject(null);
            if (EpisodeFlow.IsLoading) return;

            bool needConfirm = mode == Mode.Save ? SaveSystem.Exists(slot) : inGame;
            if (needConfirm && confirmSlot != slot)
            {
                int previous = confirmSlot;
                confirmSlot = slot;
                if (previous >= 0) Refresh(previous, loadThumbnail: false);
                Refresh(slot, loadThumbnail: false);
                return;
            }

            int was = confirmSlot;
            confirmSlot = -1;
            if (mode == Mode.Save)
            {
                if (SaveSystem.SaveToSlot(slot, saveThumbnail))
                {
                    hintText.text = $"<color={Accent}>슬롯 {slot}에 저장했다.</color>";
                    if (was >= 0 && was != slot) Refresh(was, loadThumbnail: false);
                    // 썸네일이 바뀌었으니 다시 읽는다.
                    Refresh(slot, loadThumbnail: true);
                }
                else
                {
                    hintText.text = $"<color={Accent}>저장하지 못했다. Console을 확인한다.</color>";
                }
            }
            else
            {
                var data = SaveSystem.Load(slot);
                if (data != null && EpisodeFlow.Resume(data))
                {
                    Close();
                    Loaded?.Invoke();
                }
            }
        }

        void ReleaseThumbnails()
        {
            foreach (var t in loadedThumbnails)
                if (t != null) Destroy(t);
            loadedThumbnails.Clear();
        }

        /// <summary>대사에 들어 있는 &lt;가 TMP 태그로 읽히지 않게 한다.</summary>
        static string Escape(string s) => string.IsNullOrEmpty(s) ? s : "<noparse>" + s + "</noparse>";
    }
}
