using ProjectVita.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// 설정 화면: BGM 음량, 효과음 음량, 텍스트 속도, 창 모드. 바꾸면 바로 적용하고, 닫을 때 저장한다.
    /// 타이틀과 일시정지 메뉴가 같이 쓴다.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] Slider bgmSlider;
        [SerializeField] TMP_Text bgmValue;
        [SerializeField] Slider sfxSlider;
        [SerializeField] TMP_Text sfxValue;
        [SerializeField] Slider textSpeedSlider;
        [SerializeField] TMP_Text textSpeedValue;
        [SerializeField] Button windowModeButton;
        [SerializeField] TMP_Text windowModeLabel;
        [SerializeField] Button closeButton;

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            bgmSlider.minValue = 0;
            bgmSlider.maxValue = 10;
            bgmSlider.wholeNumbers = true;
            sfxSlider.minValue = 0;
            sfxSlider.maxValue = 10;
            sfxSlider.wholeNumbers = true;
            textSpeedSlider.minValue = GameSettings.MinTextSpeed;
            textSpeedSlider.maxValue = GameSettings.MaxTextSpeed;
            textSpeedSlider.wholeNumbers = true;

            bgmSlider.onValueChanged.AddListener(v => { GameSettings.SetBgmVolume(v / 10f); Refresh(); });
            sfxSlider.onValueChanged.AddListener(v => { GameSettings.SetSfxVolume(v / 10f); Refresh(); });
            textSpeedSlider.onValueChanged.AddListener(v => { GameSettings.SetTextSpeed((int)v); Refresh(); });
            windowModeButton.onClick.AddListener(() => { GameSettings.SetFullscreen(!GameSettings.Fullscreen); Refresh(); });
            closeButton.onClick.AddListener(Close);
            root.SetActive(false);
        }

        public void Open()
        {
            bgmSlider.SetValueWithoutNotify(Mathf.Round(GameSettings.BgmVolume * 10f));
            sfxSlider.SetValueWithoutNotify(Mathf.Round(GameSettings.SfxVolume * 10f));
            textSpeedSlider.SetValueWithoutNotify(GameSettings.TextSpeed);
            Refresh();
            root.SetActive(true);
        }

        public void Close()
        {
            GameSettings.Save();
            root.SetActive(false);
        }

        void Refresh()
        {
            bgmValue.text = Mathf.RoundToInt(GameSettings.BgmVolume * 10f).ToString();
            sfxValue.text = Mathf.RoundToInt(GameSettings.SfxVolume * 10f).ToString();
            textSpeedValue.text = GameSettings.TextSpeed >= GameSettings.MaxTextSpeed ? "즉시" : GameSettings.TextSpeed.ToString();
            windowModeLabel.text = GameSettings.Fullscreen ? "화면: 전체 화면" : "화면: 창 모드";
        }
    }
}
