using ProjectVita.Core;
using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectVita.Menus
{
    /// <summary>
    /// 설정 화면. 창이 페이드되며 나타난다. 바꾸면 바로 적용하고, 닫을 때 저장한다. 타이틀과 일시정지 메뉴가 같이 쓴다.
    /// - 텍스트: 텍스트 속도, 오토 속도, 읽은 대사만 스킵
    /// - 화면: 스캔라인, 노이즈, 화면 흔들림, 전체 화면
    /// - 사운드: BGM, 효과음
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        [SerializeField] UIPanelAnim panel;
        [SerializeField] Slider textSpeedSlider;
        [SerializeField] TMP_Text textSpeedValue;
        [SerializeField] Slider autoSpeedSlider;
        [SerializeField] TMP_Text autoSpeedValue;
        [SerializeField] UIToggleSwitch skipReadOnlyToggle;
        [SerializeField] UIToggleSwitch scanlinesToggle;
        [SerializeField] UIToggleSwitch filmGrainToggle;
        [SerializeField] UIToggleSwitch screenShakeToggle;
        [SerializeField] UIToggleSwitch fullscreenToggle;
        [SerializeField] Slider bgmSlider;
        [SerializeField] TMP_Text bgmValue;
        [SerializeField] Slider sfxSlider;
        [SerializeField] TMP_Text sfxValue;
        [SerializeField] Button closeButton;

        public bool IsOpen => panel != null && panel.IsShown;

        void Awake()
        {
            Setup(textSpeedSlider, GameSettings.MinTextSpeed, GameSettings.MaxTextSpeed, v => GameSettings.SetTextSpeed((int)v));
            Setup(autoSpeedSlider, GameSettings.MinAutoSpeed, GameSettings.MaxAutoSpeed, v => GameSettings.SetAutoSpeed((int)v));
            Setup(bgmSlider, 0, 10, v => GameSettings.SetBgmVolume(v / 10f));
            Setup(sfxSlider, 0, 10, v => GameSettings.SetSfxVolume(v / 10f));
            skipReadOnlyToggle.Changed += GameSettings.SetSkipReadOnly;
            scanlinesToggle.Changed += GameSettings.SetScanlines;
            filmGrainToggle.Changed += GameSettings.SetFilmGrain;
            screenShakeToggle.Changed += GameSettings.SetScreenShake;
            fullscreenToggle.Changed += GameSettings.SetFullscreen;
            closeButton.onClick.AddListener(Close);
            panel.HideInstant();
        }

        void Setup(Slider slider, int min, int max, System.Action<float> apply)
        {
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(v =>
            {
                apply(v);
                Refresh();
            });
        }

        public void Open()
        {
            textSpeedSlider.SetValueWithoutNotify(GameSettings.TextSpeed);
            autoSpeedSlider.SetValueWithoutNotify(GameSettings.AutoSpeed);
            bgmSlider.SetValueWithoutNotify(Mathf.Round(GameSettings.BgmVolume * 10f));
            sfxSlider.SetValueWithoutNotify(Mathf.Round(GameSettings.SfxVolume * 10f));
            skipReadOnlyToggle.SetWithoutNotify(GameSettings.SkipReadOnly);
            scanlinesToggle.SetWithoutNotify(GameSettings.Scanlines);
            filmGrainToggle.SetWithoutNotify(GameSettings.FilmGrain);
            screenShakeToggle.SetWithoutNotify(GameSettings.ScreenShake);
            fullscreenToggle.SetWithoutNotify(GameSettings.Fullscreen);
            Refresh();
            panel.Show();
        }

        public void Close()
        {
            if (!IsOpen) return;
            GameSettings.Save();
            panel.Hide();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void Refresh()
        {
            textSpeedValue.text = GameSettings.TextSpeed >= GameSettings.MaxTextSpeed ? "즉시" : GameSettings.TextSpeed.ToString();
            autoSpeedValue.text = GameSettings.AutoSpeed.ToString();
            bgmValue.text = Mathf.RoundToInt(GameSettings.BgmVolume * 100f) + "%";
            sfxValue.text = Mathf.RoundToInt(GameSettings.SfxVolume * 100f) + "%";
        }
    }
}
