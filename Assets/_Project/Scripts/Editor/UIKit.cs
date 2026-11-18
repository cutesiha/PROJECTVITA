using ProjectVita.Core;
using ProjectVita.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// 씬 생성용 UI 부품 (임시 UI 시안 스타일: 모서리가 깎인 패널, 페리윙클 테두리, 모노 라벨, 시안 강조).
    /// DialogueSetup이 씬을 만들 때 쓴다. 그림은 Art/UI(UIArtGenerator), 색은 UITheme.
    /// 크기는 1920x1080 기준이다 (시안의 px × 약 1.4).
    /// </summary>
    static class UIKit
    {
        // ================================================================ 기본

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>단색 이미지. 클릭을 막지 않도록 raycastTarget은 기본으로 끈다 (대사 넘기기 클릭 판정 때문).</summary>
        public static Image Img(string name, Transform parent, Color color, bool raycast = false)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Art/UI 그림. sliced면 9-slice (패널).</summary>
        public static Image Sprite(string name, Transform parent, string sprite, Color color, bool sliced = false, bool raycast = false)
        {
            var img = Img(name, parent, color, raycast);
            img.sprite = UIArtGenerator.Load(sprite);
            if (sliced)
            {
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }
            return img;
        }

        /// <summary>글자. bold면 에이투지체 Bold (이름, 제목), 아니면 기본 폰트 (에이투지체 Medium).</summary>
        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, TMP_FontAsset font = null, float spacing = 0f, bool bold = false)
        {
            var tmp = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (font == null && bold) font = UIArtGenerator.KoreanBold;
            tmp.font = font != null ? font : TMP_Settings.defaultFontAsset;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.characterSpacing = spacing;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>영문 라벨 (IBM Plex Mono). 시안의 letter-spacing을 TMP 자간(em/100)으로 옮긴다.</summary>
        public static TextMeshProUGUI Mono(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, float spacing = 18f, bool medium = false)
        {
            var t = Text(name, parent, text, size, color, align, medium ? UIArtGenerator.MonoMedium : UIArtGenerator.Mono, spacing);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        /// <summary>제목 (Michroma). 한글은 대체 폰트로 그려진다.</summary>
        public static TextMeshProUGUI Display(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align, float spacing = 20f)
        {
            var t = Text(name, parent, text, size, color, align, UIArtGenerator.Display, spacing);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        public static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>한 점에 붙인다 (anchor = pivot = a).</summary>
        public static void Place(RectTransform rt, Vector2 a, Vector2 pos, Vector2 size) => SetRect(rt, a, a, a, pos, size);

        public static readonly Vector2 TopLeft = new Vector2(0, 1), TopRight = new Vector2(1, 1), BottomLeft = new Vector2(0, 0),
            BottomRight = new Vector2(1, 0), Center = new Vector2(0.5f, 0.5f), Top = new Vector2(0.5f, 1), Bottom = new Vector2(0.5f, 0),
            Left = new Vector2(0, 0.5f), Right = new Vector2(1, 0.5f);

        /// <summary>private [SerializeField]에 값을 넣는다. 필드 이름이 틀리면 바로 알 수 있게 에러를 낸다.</summary>
        public static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[Setup] {target.GetType().Name}에 '{field}' 필드가 없다.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[Setup] {target.GetType().Name}에 '{field}' 필드가 없다.");
                return;
            }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetValue(Object target, string field, System.Action<SerializedProperty> set)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[Setup] {target.GetType().Name}에 '{field}' 필드가 없다.");
                return;
            }
            set(prop);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ================================================================ 패널, 버튼

        /// <summary>모서리가 깎인 패널 (왼쪽 위, 오른쪽 아래). 채우기(이 오브젝트)와 테두리(자식 Line)를 따로 칠한다. cut = 16, 10, 6</summary>
        public static Image CutPanel(string name, Transform parent, int cut, Color fill, Color line, bool raycast = false)
        {
            var img = Sprite(name, parent, $"ui_cut{cut}_fill", fill, sliced: true, raycast: raycast);
            var l = Sprite("Line", img.transform, $"ui_cut{cut}_line", line, sliced: true);
            Stretch(l.rectTransform);
            return img;
        }

        /// <summary>버튼 바탕색: 그림은 흰색, 상태 색이 그대로 바탕색이 된다 (평소 → 마우스 올림 → 누름).</summary>
        public static void Colors(Button b, Color normal, Color hover, Color pressed)
        {
            var c = b.colors;
            c.normalColor = normal;
            c.highlightedColor = hover;
            c.pressedColor = pressed;
            c.selectedColor = normal; // 클릭 뒤에 강조가 남지 않게
            c.disabledColor = normal * new Color(1, 1, 1, 0.45f);
            c.colorMultiplier = 1f;
            c.fadeDuration = 0.12f;
            b.colors = c;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        /// <summary>
        /// 기본 버튼: 깎인 패널 + 테두리 + 글자. 마우스를 올리면 바탕이 밝아지고 테두리와 글자가 밝아진다.
        /// height &gt; 0이면 레이아웃 높이를 준다.
        /// </summary>
        public static Button Button(string name, Transform parent, string label, float fontSize, float height = 0f, int cut = 10,
            Color? accent = null)
        {
            var fill = CutPanel(name, parent, cut, Color.white, UITheme.LineDim, raycast: true);
            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            Colors(button, UITheme.Panel, UITheme.PanelHover, new Color32(30, 36, 50, 250));
            if (height > 0f) fill.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

            var text = Text("Label", fill.transform, label, fontSize, UITheme.Text, TextAlignmentOptions.Center, spacing: 6f);
            Stretch(text.rectTransform, 20, 4, 20, 4);

            var hover = fill.gameObject.AddComponent<UIHoverFx>();
            var line = fill.transform.Find("Line").GetComponent<Image>();
            SetArray(hover, "tint", new Object[] { text });
            SetValue(hover, "hoverColor", p => p.colorValue = UITheme.TextBright);
            SetArray(hover, "lineTint", new Object[] { line });
            SetValue(hover, "lineHoverColor", p => p.colorValue = (accent ?? UITheme.Periwinkle).WithAlpha(0.45f));
            return button;
        }

        /// <summary>
        /// 아이콘 버튼 (AUTO, SKIP, LOG, MENU): 아이콘 + 작은 모노 라벨. 마우스를 올리면 살짝 떠오르고 아이콘이 시안이 된다.
        /// 자식 Content 안에 Line, Icon, Label이 있다 (DialogueQuickMenu가 이름으로 찾는다).
        /// </summary>
        public static Button IconButton(string name, Transform parent, string icon, string label, Vector2 size)
        {
            var fill = Sprite(name, parent, "ui_cut6_fill", Color.white, sliced: true, raycast: true);
            var le = fill.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            Colors(button, UITheme.Panel, UITheme.PanelHover, new Color32(30, 36, 50, 250));

            var content = Rect("Content", fill.transform);
            Stretch(content);
            var line = Sprite("Line", content, "ui_cut6_line", UITheme.LineDim, sliced: true);
            Stretch(line.rectTransform);
            var iconImg = Sprite("Icon", content, icon, UITheme.Periwinkle);
            Place(iconImg.rectTransform, Top, new Vector2(0, -8), new Vector2(26, 26));
            var text = Mono("Label", content, label, 13, UITheme.TextDim, TextAlignmentOptions.Center, 12f);
            SetRect(text.rectTransform, BottomLeft, BottomRight, Bottom, new Vector2(0, 5), new Vector2(0, 18));

            var hover = fill.gameObject.AddComponent<UIHoverFx>();
            SetArray(hover, "shift", new Object[] { content });
            SetValue(hover, "shiftBy", p => p.vector2Value = new Vector2(0, 2));
            SetArray(hover, "tint", new Object[] { iconImg, text });
            SetValue(hover, "hoverColor", p => p.colorValue = UITheme.Cyan);
            SetArray(hover, "lineTint", new Object[] { line });
            SetValue(hover, "lineHoverColor", p => p.colorValue = UITheme.Periwinkle.WithAlpha(0.4f));
            return button;
        }

        /// <summary>창 오른쪽 위의 닫기(X) 버튼. 마우스를 올리면 빨개진다.</summary>
        public static Button CloseButton(Transform window)
        {
            var fill = Sprite("CloseButton", window, "ui_cut6_fill", Color.white, sliced: true, raycast: true);
            Place(fill.rectTransform, TopRight, new Vector2(-22, -20), new Vector2(48, 48));
            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            Colors(button, new Color(0, 0, 0, 0), new Color32(224, 82, 82, 22), new Color32(224, 82, 82, 50));
            var line = Sprite("Line", fill.transform, "ui_cut6_line", UITheme.LineDim, sliced: true);
            Stretch(line.rectTransform);
            var x = Sprite("Icon", fill.transform, "icon_close", UITheme.TextDim);
            Place(x.rectTransform, Center, Vector2.zero, new Vector2(18, 18));
            var hover = fill.gameObject.AddComponent<UIHoverFx>();
            SetArray(hover, "tint", new Object[] { x });
            SetValue(hover, "hoverColor", p => p.colorValue = UITheme.Danger);
            SetArray(hover, "lineTint", new Object[] { line });
            SetValue(hover, "lineHoverColor", p => p.colorValue = UITheme.Danger.WithAlpha(0.4f));
            return button;
        }

        // ================================================================ 창

        public sealed class Window
        {
            public UIPanelAnim anim;
            public RectTransform overlay;
            public RectTransform window;
            public RectTransform body;
            public TextMeshProUGUI eyebrow;
            public TextMeshProUGUI title;
            public Button close;
        }

        /// <summary>
        /// 오버레이 창: 화면을 어둡게 덮는 바탕(페이드) + 가운데 창(살짝 올라오며 커짐).
        /// 창 = 깎인 패널 + 위쪽 그라데이션 선 + 머리(앰버 모노 eyebrow, Michroma 제목 + 한글) + 구분선 + 본문(body).
        /// </summary>
        public static Window CreateWindow(Transform canvas, string name, Vector2 size, string eyebrow, string titleEn, string titleKr,
            bool closeButton, float headerHeight = 124f)
        {
            var w = new Window();
            var overlay = Img(name, canvas, UITheme.Overlay, raycast: true);
            Stretch(overlay.rectTransform);
            overlay.gameObject.AddComponent<CanvasGroup>();
            w.overlay = overlay.rectTransform;

            var win = CutPanel("Window", overlay.transform, 16, UITheme.Panel2, UITheme.Line, raycast: true);
            Place(win.rectTransform, Center, Vector2.zero, size);
            w.window = win.rectTransform;

            var topLine = Sprite("TopLine", win.transform, "ui_grad_h", UITheme.Periwinkle);
            SetRect(topLine.rectTransform, TopLeft, TopRight, Top, Vector2.zero, new Vector2(-16, 3));
            topLine.rectTransform.offsetMin = new Vector2(16, topLine.rectTransform.offsetMin.y);
            topLine.rectTransform.offsetMax = new Vector2(-size.x * 0.35f, 0);

            w.eyebrow = Mono("Eyebrow", win.transform, eyebrow, 15, UITheme.Amber, TextAlignmentOptions.Left, 32f);
            SetRect(w.eyebrow.rectTransform, TopLeft, TopRight, TopLeft, new Vector2(48, -32), new Vector2(-96, 24));
            w.title = Display("Title", win.transform, ConfirmDialog.WindowTitle(titleEn, titleKr), 25, Hex("#DFE2EC"), TextAlignmentOptions.Left, 20f);
            SetRect(w.title.rectTransform, TopLeft, TopRight, TopLeft, new Vector2(48, -62), new Vector2(-120, 42));

            var sep = Img("HeaderLine", win.transform, UITheme.LineDim);
            SetRect(sep.rectTransform, TopLeft, TopRight, TopLeft, new Vector2(0, -headerHeight), new Vector2(0, 2));

            w.body = Rect("Body", win.transform);
            Stretch(w.body, 0, 0, 0, headerHeight + 2);

            if (closeButton) w.close = CloseButton(win.transform);

            w.anim = overlay.gameObject.AddComponent<UIPanelAnim>();
            SetField(w.anim, "window", w.window);
            return w;
        }

        // ================================================================ 설정 부품

        /// <summary>설정 줄: 왼쪽에 한글 이름과 작은 영문, 오른쪽에 조작 부품을 둘 자리(돌려줌).</summary>
        public static RectTransform SettingRow(Transform parent, string label, string sub, float height = 62f)
        {
            var row = Rect("Row_" + sub, parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var l = Text("Label", row, label, 25, UITheme.Text, TextAlignmentOptions.Left, spacing: 8f);
            SetRect(l.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(2, -8), new Vector2(380, 32));
            var s = Mono("Sub", row, sub, 12, UITheme.TextDim, TextAlignmentOptions.Left, 20f);
            SetRect(s.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(3, -40), new Vector2(380, 18));
            var slot = Rect("Control", row);
            SetRect(slot, Right, Right, Right, Vector2.zero, new Vector2(320, height));
            return slot;
        }

        public static RectTransform GroupLabel(Transform parent, string text)
        {
            var row = Rect("Group_" + text, parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            var t = Mono("Label", row, text, 14, UITheme.TextDim, TextAlignmentOptions.BottomLeft, 30f);
            SetRect(t.rectTransform, BottomLeft, BottomRight, BottomLeft, new Vector2(0, 10), new Vector2(0, 24));
            var line = Img("Line", row, UITheme.LineDim);
            SetRect(line.rectTransform, BottomLeft, BottomRight, Bottom, Vector2.zero, new Vector2(0, 2));
            return row;
        }

        /// <summary>얇은 막대 + 마름모 손잡이 슬라이더. 값 글자는 손잡이 위에 따라다닌다.</summary>
        public static (Slider, TMP_Text) Slider(RectTransform slot)
        {
            var root = Rect("Slider", slot);
            Place(root, Right, new Vector2(-6, 0), new Vector2(300, 30));
            var rail = Img("Rail", root, UITheme.Rail, raycast: true); // 막대 어디를 눌러도 되게
            SetRect(rail.rectTransform, Left, Right, Center, Vector2.zero, new Vector2(0, 3));

            // Slider는 Fill과 Handle을 부모 높이에 맞춰 늘린다. 그래서 부모를 막대 두께(3)와 높이 0으로 둔다.
            var fillArea = Rect("FillArea", root);
            SetRect(fillArea, Left, Right, Center, Vector2.zero, new Vector2(0, 3));
            var fill = Img("Fill", fillArea, UITheme.Periwinkle);
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = Rect("HandleArea", root);
            SetRect(handleArea, Left, Right, Center, Vector2.zero, Vector2.zero);
            var handle = Sprite("Handle", handleArea, "ui_diamond", Hex("#DFE2EC"), raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(18, 18);
            var value = Mono("Value", handle.transform, "0", 12, UITheme.TextDim, TextAlignmentOptions.Center, 4f);
            Place(value.rectTransform, Center, new Vector2(0, 22), new Vector2(80, 18));

            var slider = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            var c = slider.colors;
            c.normalColor = Color.white;
            c.highlightedColor = UITheme.Cyan * new Color(1.15f, 1.05f, 0.95f, 1f);
            c.pressedColor = UITheme.Cyan;
            c.selectedColor = Color.white;
            c.fadeDuration = 0.12f;
            slider.colors = c;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return (slider, value);
        }

        /// <summary>켜고 끄는 스위치 (마름모 손잡이).</summary>
        public static UIToggleSwitch Toggle(RectTransform slot)
        {
            var box = Sprite("Toggle", slot, "ui_cut6_fill", UITheme.ToggleOff, sliced: true, raycast: true);
            Place(box.rectTransform, Right, new Vector2(-6, 0), new Vector2(74, 32));
            var line = Sprite("Line", box.transform, "ui_cut6_line", UITheme.LineDim, sliced: true);
            Stretch(line.rectTransform);
            var knob = Sprite("Knob", box.transform, "ui_diamond", UITheme.TextDim);
            Place(knob.rectTransform, Center, new Vector2(-20, 0), new Vector2(22, 22));
            var glow = Sprite("Glow", knob.transform, "ui_dot", new Color(0, 0, 0, 0));
            Place(glow.rectTransform, Center, Vector2.zero, new Vector2(46, 46));
            var sw = box.gameObject.AddComponent<UIToggleSwitch>();
            SetField(sw, "fill", box);
            SetField(sw, "line", line);
            SetField(sw, "knob", knob.rectTransform);
            SetField(sw, "knobImage", knob);
            SetField(sw, "knobGlow", glow);
            return sw;
        }

        // ================================================================ 화면 효과

        /// <summary>
        /// 분위기 효과를 만든다. sceneLayer(배경 위, 인물 아래)에는 육각형과 먼지, fxLayer(화면 위, UI 아래)에는 노이즈, 스캔라인, 비네트.
        /// </summary>
        public static ScreenFx CreateScreenFx(RectTransform sceneLayer, RectTransform fxLayer)
        {
            var hexRoot = Rect("Hexes", sceneLayer);
            Stretch(hexRoot);
            var hexes = new[]
            {
                HexDeco(hexRoot, new Vector2(173, -184), new Vector2(200, 226), 0.06f),
                HexDeco(hexRoot, new Vector2(1478, -616), new Vector2(122, 138), 0.09f),
                HexDeco(hexRoot, new Vector2(614, -691), new Vector2(72, 81), 0.05f),
            };
            var dustRoot = Rect("Dust", sceneLayer);
            Stretch(dustRoot);
            var dot = Sprite("DustTemplate", dustRoot, "ui_dot", UITheme.PeriwinkleBright.WithAlpha(0f));
            Place(dot.rectTransform, BottomLeft, Vector2.zero, new Vector2(4, 4));
            dot.rectTransform.pivot = Center;

            var noise = Rect("Noise", fxLayer).gameObject.AddComponent<RawImage>();
            noise.texture = UIArtGenerator.LoadTexture("ui_noise");
            noise.color = new Color(0, 0, 0, 0.22f); // 어둡게 지글거리는 노이즈 (선형 색공간에서 흰 노이즈는 너무 밝다)
            noise.raycastTarget = false;
            Stretch(noise.rectTransform);
            var scan = Rect("Scanlines", fxLayer).gameObject.AddComponent<RawImage>();
            scan.texture = UIArtGenerator.LoadTexture("ui_scanline");
            scan.color = new Color(0, 0, 0, 0.30f);
            scan.raycastTarget = false;
            Stretch(scan.rectTransform);
            var vignette = Sprite("AmbientVignette", fxLayer, "ui_vignette", new Color(0, 0, 0, 0.8f));
            Stretch(vignette.rectTransform);

            var fx = fxLayer.gameObject.AddComponent<ScreenFx>();
            SetField(fx, "scanlines", scan);
            SetField(fx, "noise", noise);
            SetArray(fx, "hexes", hexes);
            SetField(fx, "dustRoot", dustRoot);
            SetField(fx, "dustTemplate", dot);
            return fx;
        }

        static RectTransform HexDeco(Transform parent, Vector2 pos, Vector2 size, float alpha)
        {
            var hex = Sprite("Hex", parent, "ui_hex_line", UITheme.Periwinkle.WithAlpha(alpha));
            SetRect(hex.rectTransform, TopLeft, TopLeft, Center, pos + new Vector2(size.x / 2, -size.y / 2), size);
            return hex.rectTransform;
        }

        public static Color Hex(string hex) => UITheme.Hex(hex);
    }
}
