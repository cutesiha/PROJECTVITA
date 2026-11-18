using System;
using System.Collections.Generic;
using ProjectVita.Story;
using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// 저장/불러오기 화면 (문서 4-7). 카드 0번은 자동 저장(불러오기만), 1~7번은 수동 슬롯이다.
    /// 창이 페이드되며 올라오고 카드가 차례로 나타난다.
    /// 이미 있는 슬롯에 저장하거나 게임 중에 불러올 때는 확인창을 띄운다. 저장하면 아래에 알림이 뜬다.
    /// 타이틀과 일시정지 메뉴가 같이 쓴다.
    /// </summary>
    public sealed class SaveSlotPanel : MonoBehaviour
    {
        public enum Mode { Save, Load }

        [SerializeField] UIPanelAnim panel;
        [SerializeField] TMP_Text eyebrowText;
        [SerializeField] TMP_Text titleText;
        [SerializeField] RectTransform grid;
        [SerializeField] SaveSlotCard cardTemplate;
        [SerializeField] Button closeButton;
        [SerializeField] ConfirmDialog confirm;

        public bool IsOpen => panel != null && panel.IsShown;

        /// <summary>불러오기를 시작했다 (씬 로드 중). 일시정지 메뉴가 화면을 정리한다.</summary>
        public event Action Loaded;

        Mode mode;
        bool inGame;
        Texture2D saveThumbnail;
        readonly List<SaveSlotCard> cards = new List<SaveSlotCard>();
        readonly List<Texture2D> loadedThumbnails = new List<Texture2D>();

        void Awake()
        {
            cardTemplate.gameObject.SetActive(false);
            panel.HideInstant();
            closeButton.onClick.AddListener(Close);
        }

        void OnDestroy() => ReleaseThumbnails();

        /// <param name="inGame">게임 중(일시정지 메뉴)이면 true. 불러올 때 확인창을 띄운다.</param>
        /// <param name="thumbnail">저장할 때 슬롯에 넣을 화면 (일시정지 메뉴를 열기 직전에 찍은 것). 없어도 된다.</param>
        public void Open(Mode mode, bool inGame, Texture2D thumbnail)
        {
            this.mode = mode;
            this.inGame = inGame;
            saveThumbnail = thumbnail;
            eyebrowText.text = mode == Mode.Save ? "WRITE TO ARCHIVE" : "READ FROM ARCHIVE";
            titleText.text = mode == Mode.Save ? ConfirmDialog.WindowTitle("SAVE", "저장") : ConfirmDialog.WindowTitle("LOAD", "불러오기");
            Build();
            panel.Show();
        }

        public void Close()
        {
            if (!IsOpen) return;
            // 썸네일은 닫는 애니메이션 동안 보여야 하니 다음에 열 때 정리한다.
            panel.Hide();
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
            for (int slot = 0; slot <= SaveSystem.SlotCount; slot++) Refresh(slot);
        }

        void Refresh(int slot)
        {
            var data = SaveSystem.Load(slot);
            Texture2D thumb = data != null ? SaveSystem.LoadThumbnail(slot) : null;
            if (thumb != null) loadedThumbnails.Add(thumb);
            bool interactable = mode == Mode.Save
                ? slot != SaveSystem.AutoSlot && SaveSystem.CanSaveNow
                : data != null;
            cards[slot].Set(slot, data, thumb, mode == Mode.Save, interactable);
        }

        void OnClicked(int slot)
        {
            EventSystem.current?.SetSelectedGameObject(null);
            if (EpisodeFlow.IsLoading) return;

            if (mode == Mode.Save)
            {
                if (SaveSystem.Exists(slot) && confirm != null)
                    confirm.Show("CONFIRM", "OVERWRITE", "덮어쓰기", $"슬롯 {slot:00}의 기록을 <b>덮어씁니다</b>.", "덮어쓰기", false, () => DoSave(slot));
                else
                    DoSave(slot);
            }
            else
            {
                if (inGame && confirm != null)
                    confirm.Show("CONFIRM", "LOAD", "불러오기", "이 기록을 불러옵니다.\n지금 진행은 <b>저장되지 않습니다</b>.", "불러오기", false, () => DoLoad(slot));
                else
                    DoLoad(slot);
            }
        }

        void DoSave(int slot)
        {
            if (SaveSystem.SaveToSlot(slot, saveThumbnail))
            {
                UIToast.Show($"슬롯 {slot:00}에 기록했습니다");
                Refresh(slot);
            }
            else
            {
                UIToast.Show("저장하지 못했습니다. Console을 확인합니다");
            }
        }

        void DoLoad(int slot)
        {
            var data = SaveSystem.Load(slot);
            if (data != null && EpisodeFlow.Resume(data))
            {
                Close();
                Loaded?.Invoke();
            }
        }

        void ReleaseThumbnails()
        {
            foreach (var t in loadedThumbnails)
                if (t != null) Destroy(t);
            loadedThumbnails.Clear();
        }
    }
}
