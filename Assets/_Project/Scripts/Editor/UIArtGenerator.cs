using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// 임시 UI 그림과 UI 폰트 에셋을 만든다 (UI 시안 dystopia-vn-ui.html을 Unity로 옮긴 것).
    /// - Art/UI/*.png: 모서리가 깎인 패널(9-slice), 그라데이션, 아이콘, 육각형, 스캔라인, 노이즈 등. 전부 흰색이고 Image 색으로 칠한다.
    /// - 이미 있는 파일은 건너뛴다. 두부님의 최종 UI 아트는 같은 파일 이름으로 덮어쓰면 된다 (9-slice 테두리는 아래 표대로 다시 잡힌다).
    /// - Art/Fonts: IBM Plex Mono(영문 라벨), Michroma(제목) TMP 폰트 에셋. 한글은 Pretendard로 대체(fallback)된다.
    /// 씬 생성 메뉴가 먼저 이것을 부른다.
    /// </summary>
    public sealed class UIArtGenerator : AssetPostprocessor
    {
        public const string UIDir = "Assets/_Project/Art/UI";

        // 폰트
        const string FontsDir = "Assets/_Project/Art/Fonts";
        public const string MonoFontAsset = FontsDir + "/IBMPlexMono/IBMPlexMono-Regular SDF.asset";
        public const string MonoMediumFontAsset = FontsDir + "/IBMPlexMono/IBMPlexMono-Medium SDF.asset";
        public const string DisplayFontAsset = FontsDir + "/Michroma/Michroma-Regular SDF.asset";

        /// <summary>9-slice 테두리(픽셀). 이 표에 있는 그림만 9-slice로 임포트한다.</summary>
        static readonly Dictionary<string, int> Borders = new Dictionary<string, int>
        {
            { "ui_cut16_fill", 20 }, { "ui_cut16_line", 20 },
            { "ui_cut10_fill", 14 }, { "ui_cut10_line", 14 },
            { "ui_cut6_fill", 9 }, { "ui_cut6_line", 9 },
        };

        /// <summary>반복(타일)해서 쓰는 그림. RawImage의 uvRect로 반복한다.</summary>
        static readonly HashSet<string> Repeat = new HashSet<string> { "ui_scanline", "ui_noise" };

        [MenuItem("Tools/Project Vita/임시 UI 그림 만들기", priority = 21)]
        public static void GenerateFromMenu()
        {
            int made = Generate();
            EnsureFonts();
            Debug.Log($"[UI] 임시 UI 그림 {made}개를 만들었다 (이미 있는 파일은 건너뜀). 폰트 에셋도 확인했다.");
        }

        // ------------------------------------------------------------ 임포트 설정

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UIDir + "/", StringComparison.Ordinal)) return;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;

            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(settings);

            if (Borders.TryGetValue(name, out int b)) ti.spriteBorder = new Vector4(b, b, b, b);
            if (Repeat.Contains(name))
            {
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.filterMode = name == "ui_scanline" ? FilterMode.Point : FilterMode.Bilinear;
            }
            else
            {
                ti.wrapMode = TextureWrapMode.Clamp;
            }
        }

        // ------------------------------------------------------------ 그림

        /// <summary>없는 그림만 만든다. 만든 개수를 돌려준다.</summary>
        public static int Generate()
        {
            Directory.CreateDirectory(UIDir);
            int made = 0;
            void Make(string name, Func<Raster> draw)
            {
                string path = $"{UIDir}/{name}.png";
                if (File.Exists(path)) return;
                draw().Save(path);
                made++;
            }

            // 모서리가 깎인 패널 (왼쪽 위, 오른쪽 아래). 채우기와 테두리를 따로 둬서 색을 따로 칠한다.
            foreach (var (cut, size, line) in new[] { (16, 64, 2f), (10, 48, 2f), (6, 32, 1.6f) })
            {
                Make($"ui_cut{cut}_fill", () => { var r = new Raster(size, size); r.FillPolygon(CutShape(size, size, cut, 0f)); return r; });
                Make($"ui_cut{cut}_line", () => { var r = new Raster(size, size); r.StrokePolyline(CutShape(size, size, cut, line * 0.5f), line, true); return r; });
            }

            // 그라데이션: 세로(위 → 아래 투명), 가로(왼쪽 → 오른쪽 투명), 가운데가 진한 가로선
            Make("ui_grad_v", () => Raster.Gradient(4, 128, (x, y) => 1f - y));
            Make("ui_grad_h", () => Raster.Gradient(128, 4, (x, y) => 1f - x));
            Make("ui_grad_hc", () => Raster.Gradient(256, 4, (x, y) => 1f - Mathf.Abs(x - 0.5f) * 2f));

            // 모서리 꺾쇠 (왼쪽 위 모양. 오른쪽 아래는 180도 돌려 쓴다)
            Make("ui_corner", () => { var r = new Raster(16, 16); r.StrokePolyline(new[] { new Vector2(1.5f, 15f), new Vector2(1.5f, 1.5f), new Vector2(15f, 1.5f) }, 2.4f, false); return r; });

            // 도형
            Make("ui_tri", () => { var r = new Raster(32, 32); r.FillPolygon(new[] { new Vector2(6, 3), new Vector2(29, 16), new Vector2(6, 29) }); return r; });
            Make("ui_diamond", () => { var r = new Raster(32, 32); r.FillPolygon(new[] { new Vector2(16, 1), new Vector2(31, 16), new Vector2(16, 31), new Vector2(1, 16) }); return r; });
            Make("ui_hex_line", () => { var r = new Raster(200, 230); r.StrokePolyline(Hex(200, 230, 3f), 2.6f, true); return r; });
            Make("ui_hex_fill", () => { var r = new Raster(40, 44); r.FillPolygon(Hex(40, 44, 0f)); return r; });
            Make("ui_dot", () => Raster.Gradient(16, 16, (x, y) => Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2f)));
            Make("ui_vignette", () => Raster.Gradient(256, 256, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.46f)) / 0.7071f;
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 1f, d));
            }));

            // 화면 효과 (반복)
            Make("ui_scanline", () => Raster.Gradient(4, 3, (x, y) => y < 0.34f ? 1f : 0f));
            Make("ui_noise", () =>
            {
                var rnd = new System.Random(7);
                return Raster.Gradient(128, 128, (x, y) => (float)rnd.NextDouble());
            });

            // 아이콘 (24 단위 그림을 2배로)
            Make("icon_auto", () =>
            {
                var r = new Raster(48, 48);
                r.StrokeArc(new Vector2(24, 24), 16f, -90f, 202f, 3.2f);
                r.StrokePolyline(new[] { new Vector2(8, 6), new Vector2(8, 18), new Vector2(20, 18) }, 3.2f, false);
                return r;
            });
            Make("icon_skip", () =>
            {
                var r = new Raster(48, 48);
                r.StrokePolyline(new[] { new Vector2(8, 10), new Vector2(22, 24), new Vector2(8, 38) }, 3.2f, false);
                r.StrokePolyline(new[] { new Vector2(26, 10), new Vector2(40, 24), new Vector2(26, 38) }, 3.2f, false);
                return r;
            });
            Make("icon_log", () =>
            {
                var r = new Raster(48, 48);
                r.Stroke(new Vector2(10, 8), new Vector2(38, 8), 3.2f);
                r.Stroke(new Vector2(10, 18), new Vector2(38, 18), 3.2f);
                r.Stroke(new Vector2(10, 28), new Vector2(28, 28), 3.2f);
                r.Stroke(new Vector2(10, 38), new Vector2(22, 38), 3.2f);
                return r;
            });
            Make("icon_menu", () =>
            {
                var r = new Raster(48, 48);
                r.Stroke(new Vector2(7, 14), new Vector2(41, 14), 3.2f);
                r.Stroke(new Vector2(7, 24), new Vector2(29, 24), 3.2f);
                r.Stroke(new Vector2(7, 34), new Vector2(41, 34), 3.2f);
                return r;
            });
            Make("icon_stats", () =>
            {
                var r = new Raster(48, 48);
                r.Stroke(new Vector2(12, 38), new Vector2(12, 24), 3.6f);
                r.Stroke(new Vector2(24, 38), new Vector2(24, 12), 3.6f);
                r.Stroke(new Vector2(36, 38), new Vector2(36, 18), 3.6f);
                return r;
            });
            Make("icon_back", () =>
            {
                var r = new Raster(48, 48);
                r.Stroke(new Vector2(38, 24), new Vector2(10, 24), 3.2f);
                r.StrokePolyline(new[] { new Vector2(22, 12), new Vector2(10, 24), new Vector2(22, 36) }, 3.2f, false);
                return r;
            });
            Make("icon_close", () =>
            {
                var r = new Raster(32, 32);
                r.Stroke(new Vector2(6, 6), new Vector2(26, 26), 3.4f);
                r.Stroke(new Vector2(26, 6), new Vector2(6, 26), 3.4f);
                return r;
            });

            if (made > 0) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return made;
        }

        public static Sprite Load(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UIDir}/{name}.png");
            if (sprite == null) Debug.LogError($"[UI] {UIDir}/{name}.png가 없다. 임시 UI 그림 만들기를 실행한다.");
            return sprite;
        }

        public static Texture2D LoadTexture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{UIDir}/{name}.png");

        // ------------------------------------------------------------ 폰트

        /// <summary>
        /// UI 폰트를 맞춘다. 한글 기본 = 에이투지체 Medium(굵게는 Bold, TMP 기본 폰트로 지정).
        /// 영문 라벨(IBM Plex Mono)에 섞인 한글은 Medium으로, 제목(Michroma)에 섞인 한글은 Bold로 그린다 (fallback).
        /// </summary>
        public static void EnsureFonts()
        {
            ProjectSetup.ApplyTmpAndFonts();
            var medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ProjectSetup.FontAssetRegular);
            var bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ProjectSetup.FontAssetBold);
            foreach (var (ttf, asset, korean) in new[]
            {
                (FontsDir + "/IBMPlexMono/IBMPlexMono-Regular.ttf", MonoFontAsset, medium),
                (FontsDir + "/IBMPlexMono/IBMPlexMono-Medium.ttf", MonoMediumFontAsset, medium),
                (FontsDir + "/Michroma/Michroma-Regular.ttf", DisplayFontAsset, bold),
            })
            {
                var fa = ProjectSetup.GetOrCreateFontAsset(ttf, asset, 1024);
                if (fa == null || korean == null) continue;
                fa.fallbackFontAssetTable = new List<TMP_FontAsset> { korean };
                EditorUtility.SetDirty(fa);
            }
            AssetDatabase.SaveAssets();
        }

        public static TMP_FontAsset KoreanBold => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ProjectSetup.FontAssetBold);

        public static TMP_FontAsset Mono => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MonoFontAsset);
        public static TMP_FontAsset MonoMedium => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MonoMediumFontAsset);
        public static TMP_FontAsset Display => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontAsset);

        // ------------------------------------------------------------ 도형 좌표 (위에서 아래로 y가 커진다)

        static Vector2[] CutShape(float w, float h, float cut, float inset)
        {
            float i = inset;
            // 안쪽으로 줄일 때 깎인 모서리도 같은 두께로 들어오게 대각선 쪽을 조금 더 민다.
            float c = cut + i * 0.4142f;
            return new[]
            {
                new Vector2(c, i), new Vector2(w - i, i), new Vector2(w - i, h - c),
                new Vector2(w - c, h - i), new Vector2(i, h - i), new Vector2(i, c),
            };
        }

        static Vector2[] Hex(float w, float h, float inset)
        {
            float cx = w * 0.5f, i = inset;
            return new[]
            {
                new Vector2(cx, i), new Vector2(w - i, h * 0.2435f), new Vector2(w - i, h * 0.7565f),
                new Vector2(cx, h - i), new Vector2(i, h * 0.7565f), new Vector2(i, h * 0.2435f),
            };
        }

        // ------------------------------------------------------------ 작은 래스터라이저 (흰색 + 알파, 가장자리 부드럽게)

        sealed class Raster
        {
            readonly int w, h;
            readonly float[] a;

            public Raster(int width, int height)
            {
                w = width;
                h = height;
                a = new float[w * h];
            }

            public static Raster Gradient(int width, int height, Func<float, float, float> alpha)
            {
                var r = new Raster(width, height);
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        r.a[y * width + x] = Mathf.Clamp01(alpha(width == 1 ? 0 : x / (float)(width - 1), height == 1 ? 0 : y / (float)(height - 1)));
                return r;
            }

            void Put(int x, int y, float v)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                int i = y * w + x;
                if (v > a[i]) a[i] = v;
            }

            /// <summary>다각형 채우기. 픽셀마다 4x4 점을 찍어 덮인 비율을 알파로 쓴다.</summary>
            public void FillPolygon(Vector2[] pts)
            {
                const int ss = 4;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int inside = 0;
                        for (int sy = 0; sy < ss; sy++)
                            for (int sx = 0; sx < ss; sx++)
                                if (Inside(pts, new Vector2(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss))) inside++;
                        if (inside > 0) Put(x, y, inside / (float)(ss * ss));
                    }
                }
            }

            public void Stroke(Vector2 p0, Vector2 p1, float width)
            {
                float half = width * 0.5f;
                int x0 = Mathf.FloorToInt(Mathf.Min(p0.x, p1.x) - half - 1), x1 = Mathf.CeilToInt(Mathf.Max(p0.x, p1.x) + half + 1);
                int y0 = Mathf.FloorToInt(Mathf.Min(p0.y, p1.y) - half - 1), y1 = Mathf.CeilToInt(Mathf.Max(p0.y, p1.y) + half + 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = DistanceToSegment(new Vector2(x + 0.5f, y + 0.5f), p0, p1);
                        Put(x, y, Mathf.Clamp01(half + 0.5f - d));
                    }
            }

            public void StrokePolyline(Vector2[] pts, float width, bool closed)
            {
                for (int i = 0; i + 1 < pts.Length; i++) Stroke(pts[i], pts[i + 1], width);
                if (closed) Stroke(pts[pts.Length - 1], pts[0], width);
            }

            /// <summary>호. 각도는 화면 기준(y 아래)으로 시계 방향이 +다.</summary>
            public void StrokeArc(Vector2 c, float radius, float fromDeg, float toDeg, float width)
            {
                int steps = Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 4f);
                Vector2 prev = Point(c, radius, fromDeg);
                for (int i = 1; i <= steps; i++)
                {
                    var next = Point(c, radius, Mathf.Lerp(fromDeg, toDeg, i / (float)steps));
                    Stroke(prev, next, width);
                    prev = next;
                }
            }

            static Vector2 Point(Vector2 c, float r, float deg) =>
                c + new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * r;

            static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
            {
                var ab = b - a;
                float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                return Vector2.Distance(p, a + ab * t);
            }

            static bool Inside(Vector2[] poly, Vector2 p)
            {
                bool inside = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                        p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                        inside = !inside;
                }
                return inside;
            }

            /// <summary>흰색 PNG로 저장한다. 그린 좌표는 위가 0이므로 위아래를 뒤집어 넣는다.</summary>
            public void Save(string path)
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        px[(h - 1 - y) * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a[y * w + x] * 255f));
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
