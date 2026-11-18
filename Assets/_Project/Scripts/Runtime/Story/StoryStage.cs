using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using ProjectVita.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Story
{
    /// <summary>
    /// Story 씬의 무대: 배경, CG, 스탠딩, 화면 효과(페이드, 와이프, 흔들림, 플래시, 비네트).
    /// Yarn 명령(StoryCommands)과 대사창(StoryPresenter)이 이것을 부른다.
    /// 그림은 AssetCatalog에서 이름으로 찾는다.
    /// </summary>
    public sealed class StoryStage : MonoBehaviour
    {
        public static StoryStage Current { get; private set; }

        [Header("무대 (흔들림은 이 안만 움직인다)")]
        [SerializeField] RectTransform shakeRoot;
        [SerializeField] Image backgroundBack;
        [SerializeField] Image backgroundFront;
        [SerializeField] RectTransform characterRoot;
        [SerializeField] Image cgImage;

        [Header("화면 효과")]
        [SerializeField] Image vignette;
        [SerializeField] RectTransform wipeOverlay;
        [SerializeField] Image flash;
        [SerializeField] Image fadeOverlay;

        [Header("스탠딩")]
        [SerializeField] float standingHeight = 1000f;
        [SerializeField] float dimBrightness = 0.45f;

        public const string DefaultPosition = "center";

        static readonly Dictionary<string, float> Positions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            { "farleft", 0.13f }, { "left", 0.28f }, { "center", 0.5f }, { "right", 0.72f }, { "farright", 0.87f },
            { "왼쪽", 0.28f }, { "가운데", 0.5f }, { "오른쪽", 0.72f },
        };

        sealed class Standing
        {
            public AssetCatalog.CharacterEntry def;
            public string expression;
            public string position;
            public Image image;
            public RectTransform rect;
            public float alpha;
            public float brightness = 1f;
            public bool leaving;
            public CancellationTokenSource fadeCts, moveCts, dimCts;

            public void Apply()
            {
                if (image != null) image.color = new Color(brightness, brightness, brightness, alpha);
            }
        }

        readonly Dictionary<string, Standing> standings = new Dictionary<string, Standing>(StringComparer.OrdinalIgnoreCase);
        string backgroundName = string.Empty;
        string cgName = string.Empty;
        float vignetteStrength;
        CancellationTokenSource bgCts, cgCts, fadeCts, flashCts, shakeCts, vignetteCts;

        CancellationToken Life => destroyCancellationToken;

        void Awake()
        {
            Current = this;
            SetImage(backgroundBack, null, 0f);
            SetImage(backgroundFront, null, 0f);
            SetImage(cgImage, null, 0f);
            vignette.sprite = CreateVignetteSprite();
            vignette.color = new Color(1f, 1f, 1f, 0f);
            flash.color = new Color(1f, 1f, 1f, 0f);
            fadeOverlay.color = new Color(0f, 0f, 0f, 0f);
            wipeOverlay.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        // ================================================================ 배경

        /// <summary>&lt;&lt;bg 이름 [cut|fade|wipe] [시간]&gt;&gt;. 이름이 none/off면 배경을 지운다.</summary>
        public async Awaitable SetBackground(string name, string transition, float time)
        {
            bool clear = IsOff(name);
            Sprite sprite = null;
            if (!clear)
            {
                sprite = AssetCatalog.Instance.FindBackground(name);
                if (sprite == null)
                {
                    Debug.LogWarning($"[Stage] 배경 '{name}'이 없다. Art/Backgrounds/{name}.png를 넣는다.");
                    return;
                }
            }
            backgroundName = clear ? string.Empty : name;
            var token = Tween.Restart(ref bgCts, Life);

            switch (Normalize(transition, "fade"))
            {
                case "cut":
                    SetImage(backgroundFront, sprite, 1f);
                    SetImage(backgroundBack, null, 0f);
                    break;

                case "wipe":
                    await Wipe(time, () =>
                    {
                        SetImage(backgroundFront, sprite, 1f);
                        SetImage(backgroundBack, null, 0f);
                    }, token);
                    break;

                default: // fade: 이전 그림 위로 새 그림이 서서히 나타난다
                    var old = backgroundFront.enabled ? backgroundFront.sprite : null;
                    float oldAlpha = old != null ? backgroundFront.color.a : 0f;
                    SetImage(backgroundBack, old, oldAlpha);
                    SetImage(backgroundFront, sprite, 0f);
                    await Tween.Run(time, t =>
                    {
                        if (sprite != null) SetAlpha(backgroundFront, t);
                        else SetAlpha(backgroundBack, oldAlpha * (1f - t));
                    }, token);
                    if (!token.IsCancellationRequested && this != null) SetImage(backgroundBack, null, 0f);
                    break;
            }
        }

        async Awaitable Wipe(float time, Action atMiddle, CancellationToken token)
        {
            float half = Mathf.Max(0.05f, time * 0.5f);
            wipeOverlay.gameObject.SetActive(true);
            // 왼쪽에서 오른쪽으로 덮고, 가운데에서 그림을 바꾼 뒤, 왼쪽부터 걷어 낸다.
            await Tween.Run(half, t => SetWipe(0f, Tween.EaseInOut(t)), token);
            if (token.IsCancellationRequested || this == null) return;
            atMiddle();
            await Tween.Run(half, t => SetWipe(Tween.EaseInOut(t), 1f), token);
            if (this != null) wipeOverlay.gameObject.SetActive(false);
        }

        void SetWipe(float min, float max)
        {
            wipeOverlay.anchorMin = new Vector2(min, 0f);
            wipeOverlay.anchorMax = new Vector2(max, 1f);
            wipeOverlay.offsetMin = wipeOverlay.offsetMax = Vector2.zero;
        }

        // ================================================================ CG

        /// <summary>&lt;&lt;cg 이름 [cut|fade] [시간]&gt;&gt;, &lt;&lt;cg off&gt;&gt;. CG는 스탠딩 위에 뜬다.</summary>
        public async Awaitable SetCG(string name, string transition, float time)
        {
            var token = Tween.Restart(ref cgCts, Life);
            bool cut = Normalize(transition, "fade") == "cut";

            if (IsOff(name))
            {
                cgName = string.Empty;
                float start = cgImage.enabled ? cgImage.color.a : 0f;
                if (!cut) await Tween.Run(time, t => SetAlpha(cgImage, start * (1f - t)), token);
                if (!token.IsCancellationRequested && this != null) SetImage(cgImage, null, 0f);
                return;
            }

            var sprite = AssetCatalog.Instance.FindCG(name);
            if (sprite == null)
            {
                Debug.LogWarning($"[Stage] CG '{name}'이 없다. Art/CG/{name}.png를 넣는다.");
                return;
            }
            cgName = name;
            if (cut)
            {
                SetImage(cgImage, sprite, 1f);
                return;
            }
            SetImage(cgImage, sprite, 0f);
            await Tween.Run(time, t => SetAlpha(cgImage, t), token);
        }

        // ================================================================ 스탠딩

        /// <summary>
        /// &lt;&lt;show 캐릭터 [표정] [위치] [시간]&gt;&gt;. 이미 화면에 있으면 표정과 위치만 바꾼다.
        /// 표정을 빼고 &lt;&lt;show 주인공 left&gt;&gt;처럼 위치만 써도 된다.
        /// </summary>
        public void Show(string key, string expression, string position, float time)
        {
            if (string.IsNullOrEmpty(position) && IsPosition(expression))
            {
                position = expression;
                expression = string.Empty;
            }

            var def = FindCharacter(key);
            if (def == null) return;
            if (!string.IsNullOrEmpty(position) && !IsPosition(position))
            {
                Debug.LogWarning($"[Stage] 위치 '{position}'을 모른다. left, center, right, farleft, farright 중 하나를 쓴다.");
                position = string.Empty;
            }

            bool isNew = !standings.TryGetValue(def.id, out var s);
            if (isNew)
            {
                s = CreateStanding(def);
                s.position = string.IsNullOrEmpty(position) ? DefaultPosition : position;
                PlaceAt(s, Positions[s.position]);
                standings[def.id] = s;
            }
            s.leaving = false;

            if (isNew || !string.IsNullOrEmpty(expression)) SetExpression(s, expression);
            if (!isNew && !string.IsNullOrEmpty(position) && !string.Equals(position, s.position, StringComparison.OrdinalIgnoreCase))
                Move(def.id, position, time);

            Tween.Forget(FadeStanding(s, 1f, time, false));
        }

        /// <summary>&lt;&lt;hide 캐릭터 [시간]&gt;&gt;, &lt;&lt;hide all&gt;&gt;</summary>
        public void Hide(string key, float time)
        {
            if (string.Equals(key, "all", StringComparison.OrdinalIgnoreCase) || key == "모두")
            {
                foreach (var each in new List<Standing>(standings.Values)) BeginHide(each, time);
                return;
            }
            if (TryGetStanding(key, "hide", out var s)) BeginHide(s, time);
        }

        void BeginHide(Standing s, float time)
        {
            s.leaving = true;
            Tween.Forget(FadeStanding(s, 0f, time, true));
        }

        /// <summary>&lt;&lt;move 캐릭터 위치 [시간]&gt;&gt;</summary>
        public void Move(string key, string position, float time)
        {
            if (!TryGetStanding(key, "move", out var s)) return;
            if (!IsPosition(position))
            {
                Debug.LogWarning($"[Stage] 위치 '{position}'을 모른다. left, center, right, farleft, farright 중 하나를 쓴다.");
                return;
            }
            var token = Tween.Restart(ref s.moveCts, Life);
            float from = s.rect.anchorMin.x;
            float to = Positions[position];
            s.position = position;
            Tween.Forget(Tween.Run(time, t => PlaceAt(s, Mathf.Lerp(from, to, Tween.EaseInOut(t))), token));
        }

        /// <summary>&lt;&lt;face 캐릭터 표정&gt;&gt; 또는 대사 끝의 #face:표정</summary>
        public void Face(string key, string expression)
        {
            if (TryGetStanding(key, "face", out var s)) SetExpression(s, expression);
        }

        /// <summary>말하는 사람만 밝게, 나머지는 어둡게. 나레이션(speaker 없음)이면 모두 원래 밝기.</summary>
        public void Highlight(string speakerName)
        {
            bool narration = string.IsNullOrEmpty(speakerName);
            var def = narration ? null : AssetCatalog.Instance.FindCharacter(speakerName);
            foreach (var s in standings.Values)
            {
                float target = narration || (def != null && s.def == def) ? 1f : dimBrightness;
                if (Mathf.Approximately(target, s.brightness)) continue;
                var token = Tween.Restart(ref s.dimCts, Life);
                float from = s.brightness;
                Tween.Forget(Tween.Run(0.15f, t =>
                {
                    s.brightness = Mathf.Lerp(from, target, t);
                    s.Apply();
                }, token));
            }
        }

        Standing CreateStanding(AssetCatalog.CharacterEntry def)
        {
            var go = new GameObject("Standing_" + def.id, typeof(RectTransform), typeof(Image));
            go.layer = gameObject.layer;
            go.transform.SetParent(characterRoot, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0f);
            var s = new Standing { def = def, image = img, rect = rt, alpha = 0f };
            s.Apply();
            return s;
        }

        void SetExpression(Standing s, string expression)
        {
            if (!string.IsNullOrEmpty(expression) && !s.def.HasExpression(expression))
                Debug.LogWarning($"[Stage] {s.def.id}에 '{expression}' 표정이 없다. Art/Characters/{s.def.id}/{s.def.id}_{expression}.png를 넣는다. 지금은 기본 표정을 쓴다.");
            var sprite = s.def.GetExpression(expression, out var resolved);
            s.expression = resolved;
            s.image.sprite = sprite;
            if (sprite != null)
            {
                float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
                s.rect.sizeDelta = new Vector2(standingHeight * aspect, standingHeight);
            }
        }

        static void PlaceAt(Standing s, float x)
        {
            s.rect.anchorMin = s.rect.anchorMax = new Vector2(x, 0f);
            s.rect.anchoredPosition = Vector2.zero;
        }

        async Awaitable FadeStanding(Standing s, float target, float time, bool destroyAfter)
        {
            var token = Tween.Restart(ref s.fadeCts, Life);
            float from = s.alpha;
            await Tween.Run(time, t =>
            {
                s.alpha = Mathf.Lerp(from, target, t);
                s.Apply();
            }, token);

            if (destroyAfter && !token.IsCancellationRequested && s.leaving && this != null)
            {
                standings.Remove(s.def.id);
                if (s.image != null) Destroy(s.image.gameObject);
            }
        }

        AssetCatalog.CharacterEntry FindCharacter(string key)
        {
            var def = AssetCatalog.Instance.FindCharacter(key);
            if (def == null)
                Debug.LogWarning($"[Stage] 캐릭터 '{key}'가 없다. Data/Characters.csv와 Art/Characters 폴더를 확인한다.");
            return def;
        }

        bool TryGetStanding(string key, string command, out Standing s)
        {
            s = null;
            var def = FindCharacter(key);
            if (def == null) return false;
            if (standings.TryGetValue(def.id, out s) && !s.leaving) return true;
            Debug.LogWarning($"[Stage] <<{command} {key}>>: {def.id}가 화면에 없다. 먼저 <<show {key}>>를 쓴다.");
            return false;
        }

        // ================================================================ 화면 효과

        /// <summary>&lt;&lt;fade out [시간] [black|white|#색]&gt;&gt;, &lt;&lt;fade in [시간]&gt;&gt;. 대사창까지 덮는다.</summary>
        public async Awaitable Fade(string direction, float time, string color)
        {
            string dir = Normalize(direction, "out");
            if (dir != "in" && dir != "out")
            {
                Debug.LogWarning($"[Stage] <<fade {direction}>>: in 또는 out을 쓴다.");
                return;
            }
            var token = Tween.Restart(ref fadeCts, Life);
            var current = fadeOverlay.color;
            // fade in은 색을 따로 쓰지 않으면 지금 덮고 있는 색 그대로 걷어 낸다.
            var c = string.IsNullOrEmpty(color) ? (dir == "out" ? Color.black : current) : ParseColor(color, Color.black);
            float start = current.a;
            float target = dir == "in" ? 0f : 1f;
            await Tween.Run(time, t => fadeOverlay.color = new Color(c.r, c.g, c.b, Mathf.Lerp(start, target, t)), token);
        }

        /// <summary>&lt;&lt;flash [white|red|#색] [시간]&gt;&gt;. 화면 전체가 번쩍인다. 기다리지 않는다.</summary>
        public void Flash(string color, float time)
        {
            var token = Tween.Restart(ref flashCts, Life);
            var c = ParseColor(color, Color.white);
            Tween.Forget(Tween.Run(time, t => flash.color = new Color(c.r, c.g, c.b, 1f - Tween.EaseOut(t)), token));
        }

        /// <summary>&lt;&lt;shake [세기] [시간]&gt;&gt;. 세기 1 = 약 30px. 무대만 흔들고 대사창은 그대로 둔다. 기다리지 않는다.</summary>
        public void Shake(float strength, float time)
        {
            if (!GameSettings.ScreenShake) return; // 설정에서 화면 흔들림을 껐다
            var token = Tween.Restart(ref shakeCts, Life);
            float amp = 30f * Mathf.Max(0f, strength);
            Tween.Forget(Tween.Run(time, t =>
            {
                shakeRoot.anchoredPosition = t >= 1f
                    ? Vector2.zero
                    : new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)) * amp * (1f - t);
            }, token));
        }

        /// <summary>&lt;&lt;vignette 0.6&gt;&gt;, &lt;&lt;vignette on&gt;&gt;, &lt;&lt;vignette off&gt;&gt;. 화면 가장자리를 어둡게 한다.</summary>
        public void Vignette(string value)
        {
            float target;
            if (IsOff(value) || value == "0") target = 0f;
            else if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)) target = 0.6f;
            else if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out target))
            {
                Debug.LogWarning($"[Stage] <<vignette {value}>>: on, off 또는 0~1 숫자를 쓴다.");
                return;
            }
            target = Mathf.Clamp01(target);
            vignetteStrength = target;
            var token = Tween.Restart(ref vignetteCts, Life);
            float from = vignette.color.a;
            Tween.Forget(Tween.Run(0.4f, t => vignette.color = new Color(1f, 1f, 1f, Mathf.Lerp(from, target, t)), token));
        }

        // ================================================================ 세이브용 상태

        [Serializable]
        public sealed class StandingState
        {
            public string id;
            public string expression;
            public string position;
        }

        [Serializable]
        public sealed class StageState
        {
            public string background = string.Empty;
            public string cg = string.Empty;
            public float vignette;
            public List<StandingState> characters = new List<StandingState>();
        }

        public StageState GetState()
        {
            var state = new StageState { background = backgroundName, cg = cgName, vignette = vignetteStrength };
            foreach (var s in standings.Values)
                if (!s.leaving) state.characters.Add(new StandingState { id = s.def.id, expression = s.expression, position = s.position });
            return state;
        }

        /// <summary>이어하기: 저장한 무대를 전환 없이 바로 세운다.</summary>
        public void ApplyState(StageState state)
        {
            if (state == null) return;
            if (!string.IsNullOrEmpty(state.background)) Tween.Forget(SetBackground(state.background, "cut", 0f));
            if (!string.IsNullOrEmpty(state.cg)) Tween.Forget(SetCG(state.cg, "cut", 0f));
            if (state.vignette > 0f)
            {
                vignetteStrength = state.vignette;
                vignette.color = new Color(1f, 1f, 1f, state.vignette);
            }
            foreach (var c in state.characters) Show(c.id, c.expression, c.position, 0f);
        }

        // ================================================================ 공통

        public static bool IsPosition(string value) => !string.IsNullOrEmpty(value) && Positions.ContainsKey(value);

        static bool IsOff(string value) =>
            string.IsNullOrEmpty(value) || value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("none", StringComparison.OrdinalIgnoreCase);

        static string Normalize(string value, string fallback) =>
            string.IsNullOrEmpty(value) ? fallback : value.Trim().ToLowerInvariant();

        static void SetImage(Image img, Sprite sprite, float alpha)
        {
            img.sprite = sprite;
            img.enabled = sprite != null;
            SetAlpha(img, alpha);
        }

        static void SetAlpha(Image img, float alpha)
        {
            if (img == null) return;
            var c = img.color;
            c.a = alpha;
            img.color = c;
        }

        public static Color ParseColor(string value, Color fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            switch (value.Trim().ToLowerInvariant())
            {
                case "black": case "검정": return Color.black;
                case "white": case "흰색": return Color.white;
                case "red": case "빨강": return new Color(0.85f, 0.08f, 0.08f);
            }
            if (ColorUtility.TryParseHtmlString(value, out var c)) return c;
            Debug.LogWarning($"[Stage] 색 '{value}'을 모른다. black, white, red 또는 #RRGGBB를 쓴다.");
            return fallback;
        }

        /// <summary>가장자리가 어두운 원형 그라데이션. 그림 파일 없이 만든다.</summary>
        static Sprite CreateVignetteSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Vignette" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, d));
                px[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
