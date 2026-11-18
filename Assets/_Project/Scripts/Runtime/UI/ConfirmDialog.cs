using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectVita.UI
{
    /// <summary>
    /// 확인창 ("타이틀로 돌아갑니다. 진행 중인 대화는 저장되지 않습니다." 예 / 아니오).
    /// 씬마다 하나 있고, ConfirmDialog.Current로 부른다. Esc는 '아니오'다 (메뉴 쪽에서 Cancel을 부른다).
    /// </summary>
    public sealed class ConfirmDialog : MonoBehaviour
    {
        [SerializeField] UIPanelAnim panel;
        [SerializeField] TMP_Text eyebrowText;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text messageText;
        [SerializeField] Button yesButton;
        [SerializeField] TMP_Text yesLabel;
        [SerializeField] Image yesFill;
        [SerializeField] Button noButton;

        public static ConfirmDialog Current { get; private set; }

        Action onYes, onNo;

        public bool IsOpen => panel != null && panel.IsShown;

        void Awake()
        {
            Current = this;
            panel.HideInstant();
            yesButton.onClick.AddListener(() => Answer(true));
            noButton.onClick.AddListener(() => Answer(false));
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <param name="titleEn">영문 제목 (Michroma 글꼴)</param>
        /// <param name="titleKr">한글 제목</param>
        /// <param name="danger">예 버튼을 빨갛게</param>
        public void Show(string eyebrow, string titleEn, string titleKr, string message, string yes, bool danger, Action onYes, Action onNo = null)
        {
            this.onYes = onYes;
            this.onNo = onNo;
            eyebrowText.text = eyebrow;
            titleText.text = WindowTitle(titleEn, titleKr);
            messageText.text = message;
            yesLabel.text = yes;
            if (yesFill != null) yesFill.color = danger ? UITheme.Danger : UITheme.Periwinkle;
            panel.transform.SetAsLastSibling();
            panel.Show();
        }

        public void Cancel() => Answer(false);

        /// <summary>창 제목: 영문(Michroma) 뒤에 한글. 한글은 Michroma에 없어서 대체 폰트(Pretendard)로 그려진다.</summary>
        public static string WindowTitle(string en, string kr) =>
            $"{en}<space=0.7em><size=108%><color={UITheme.ToHex(UITheme.PeriwinkleBright)}><cspace=0.3em>{kr}</cspace></color></size>";

        void Answer(bool yes)
        {
            if (!IsOpen) return;
            panel.Hide();
            EventSystem.current?.SetSelectedGameObject(null);
            var callback = yes ? onYes : onNo;
            onYes = onNo = null;
            callback?.Invoke();
        }
    }
}
