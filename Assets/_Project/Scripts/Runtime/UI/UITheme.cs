using UnityEngine;

namespace ProjectVita.UI
{
    /// <summary>
    /// UI 색 (임시 UI 시안 dystopia-vn-ui.html의 색 토큰). 짙은 검정 + 페리윙클 + 시안 강조 + 경고 앰버.
    /// 두부님의 UI 아트가 오면 이 값과 Art/UI 그림을 바꾼다. 씬 생성(Editor/DialogueSetup)과 실행 중 효과가 같이 쓴다.
    /// UI는 선형 색공간에서 섞인다. 어두운 반투명 판은 브라우저보다 훨씬 비쳐 보이고, 밝은 반투명 선은 더 밝게 보인다.
    /// 그래서 판은 시안보다 불투명하게, 선과 효과는 시안보다 옅게 잡았다.
    /// </summary>
    public static class UITheme
    {
        public static readonly Color Bg = Hex("#08090C");
        public static readonly Color Panel = new Color32(10, 13, 19, 250);
        public static readonly Color PanelHover = new Color32(20, 24, 34, 252);
        public static readonly Color Panel2 = new Color32(16, 19, 27, 253);
        public static readonly Color SlotFill = new Color32(14, 16, 23, 236);
        public static readonly Color SlotHover = new Color32(22, 26, 36, 244);
        public static readonly Color Line = new Color32(123, 130, 166, 60);
        public static readonly Color LineDim = new Color32(123, 130, 166, 24);
        public static readonly Color Periwinkle = Hex("#7B82A6");
        public static readonly Color PeriwinkleBright = Hex("#9AA3C7");
        public static readonly Color Cyan = Hex("#4DB8FF");
        public static readonly Color CyanDim = new Color32(77, 184, 255, 22);
        public static readonly Color CyanLine = new Color32(77, 184, 255, 100);
        public static readonly Color Amber = Hex("#E8863A");
        public static readonly Color Text = Hex("#C9CDD6");
        public static readonly Color TextDim = Hex("#6C7387");
        public static readonly Color TextBright = Hex("#EEF0F6");
        public static readonly Color TextFaint = Hex("#4A4F60");
        public static readonly Color Narration = Hex("#9BA1B4");
        public static readonly Color Danger = Hex("#E05252");
        public static readonly Color Overlay = new Color32(4, 5, 8, 222);
        public static readonly Color ToggleOff = Hex("#1A1D28");
        public static readonly Color Rail = Hex("#242736");

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static string ToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
