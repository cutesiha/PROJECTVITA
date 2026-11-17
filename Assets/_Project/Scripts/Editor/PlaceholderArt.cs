using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// 임시 그림(단색 PNG + 이름표 글자)과 임시 소리(WAV)를 만든다.
    /// **이미 있는 파일은 건너뛴다.** 최종 아트나 소리를 같은 파일 이름으로 덮어쓰면 그것이 그대로 쓰인다.
    /// 만들 목록은 아래 표에 있다. 새 임시 리소스가 필요하면 표에 한 줄 추가하고 메뉴를 다시 실행한다.
    /// </summary>
    public static class PlaceholderArt
    {
        const string MenuRoot = "Tools/Project Vita/";

        // ------------------------------------------------------------ 만들 목록

        static readonly (string name, string color)[] Backgrounds =
        {
            ("ruins_night", "#1E2A3A"),
            ("corridor", "#2A2F2B"),
            ("lab", "#12302F"),
        };

        static readonly (string name, string color)[] CGs =
        {
            ("flashback_01", "#3A2420"),
        };

        // id, Yarn 화자 이름(라벨용), 색, 표정들
        static readonly (string id, string label, string color, string[] expressions)[] Characters =
        {
            ("Protagonist", "주인공", "#8E2A24", new[] { "default", "surprised", "smile" }),
            ("Stranger", "???", "#3E4470", new[] { "default" }),
        };

        // 전투 더미 유닛 (Art/Battle/{id}.png). 세계관 고유명사를 쓰지 않는다.
        static readonly (string id, string label, string color)[] BattleUnits =
        {
            ("Ally_A", "아군 A", "#8E2A24"),
            ("Ally_B", "아군 B", "#2A5A8E"),
            ("Ally_C", "아군 C", "#2E7D4F"),
            ("Enemy_Construct_1", "인조 생물 1", "#4F6B2E"),
            ("Enemy_Construct_2", "인조 생물 2", "#7A3B22"),
            ("Enemy_Construct_3", "인조 생물 3", "#2F3F6E"),
        };

        // ------------------------------------------------------------ 메뉴

        [MenuItem(MenuRoot + "임시 그림과 소리 만들기", priority = 13)]
        public static void Generate()
        {
            int made = 0, skipped = 0;
            var font = TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                Debug.LogError("[Placeholder] TMP 기본 폰트가 없다. 1단계 기본 세팅을 먼저 실행한다.");
                return;
            }

            foreach (var (name, color) in Backgrounds)
                Make($"{AssetCatalogBuilder.BackgroundsDir}/{name}.png", ref made, ref skipped,
                    () => MakeBackground(font, 1920, 1080, Hex(color), "BG", name));

            foreach (var (name, color) in CGs)
                Make($"{AssetCatalogBuilder.CGDir}/{name}.png", ref made, ref skipped,
                    () => MakeBackground(font, 1920, 1080, Hex(color), "CG", name));

            foreach (var (id, label, color, expressions) in Characters)
            {
                for (int i = 0; i < expressions.Length; i++)
                {
                    string expr = expressions[i];
                    // 표정마다 밝기를 조금씩 달리해서 바뀐 것이 한눈에 보이게 한다.
                    float shade = 1f + 0.22f * i * (i % 2 == 0 ? -1f : 1f);
                    Make($"{AssetCatalogBuilder.CharactersDir}/{id}/{id}_{expr}.png", ref made, ref skipped,
                        () => MakeStanding(font, Hex(color) * shade, label, expr));
                }
            }

            foreach (var (id, label, color) in BattleUnits)
                Make($"{AssetCatalogBuilder.BattleArtDir}/{id}.png", ref made, ref skipped,
                    () => MakeFigure(font, 500, 700, Hex(color), label, id, id.StartsWith("Enemy")));

            MakeWav($"{AssetCatalogBuilder.BgmDir}/calm_ominous.wav", ref made, ref skipped, Drone);
            MakeWav($"{AssetCatalogBuilder.BgmDir}/tension.wav", ref made, ref skipped, Pulse);
            MakeWav($"{AssetCatalogBuilder.SfxDir}/footsteps.wav", ref made, ref skipped, () => Footsteps(1f));
            MakeWav($"{AssetCatalogBuilder.SfxDir}/footsteps_far.wav", ref made, ref skipped, () => Footsteps(0.35f));
            MakeWav($"{AssetCatalogBuilder.SfxDir}/impact.wav", ref made, ref skipped, Impact);

            AssetDatabase.Refresh();
            // 이름표 글자를 찍느라 Dynamic 폰트에 글자가 늘었다. 커밋 노이즈를 줄이려고 비운다.
            ProjectSetup.ClearDynamicFonts();
            AssetCatalogBuilder.Rebuild(log: true);
            Debug.Log($"[Placeholder] 임시 리소스 {made}개 생성, {skipped}개는 이미 있어서 건너뜀.");
        }

        static void Make(string path, ref int made, ref int skipped, Func<Texture2D> draw)
        {
            if (File.Exists(path))
            {
                skipped++;
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tex = draw();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            made++;
        }

        // ------------------------------------------------------------ 그림

        static Texture2D MakeBackground(TMP_FontAsset font, int w, int h, Color color, string kind, string name)
        {
            var px = new Color32[w * h];
            // 아래로 갈수록 조금 어두운 세로 그라데이션 + 지평선 띠 (단색보다 무대처럼 보이게)
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / h;
                var c = Color.Lerp(color * 0.6f, color * 1.15f, t);
                if (Mathf.Abs(t - 0.38f) < 0.004f) c = Color.Lerp(c, Color.white, 0.25f);
                c.a = 1f;
                Color32 c32 = c;
                for (int x = 0; x < w; x++) px[y * w + x] = c32;
            }
            var labelColor = new Color(1f, 1f, 1f, 0.55f);
            DrawText(px, w, h, font, kind, 56f, new Vector2(w / 2f, h * 0.58f), labelColor);
            DrawText(px, w, h, font, name, 96f, new Vector2(w / 2f, h * 0.48f), labelColor);
            return ToTexture(px, w, h);
        }

        static Texture2D MakeStanding(TMP_FontAsset font, Color color, string label, string expression)
        {
            const int w = 600, h = 1200;
            var px = new Color32[w * h]; // 투명
            color.a = 1f;
            // 몸통과 머리만 있는 실루엣
            FillRect(px, w, h, 90, 0, 510, 760, color);
            FillRect(px, w, h, 60, 560, 540, 760, color);
            FillRect(px, w, h, 190, 780, 410, 1080, color * 1.1f);
            var white = new Color(1f, 1f, 1f, 0.95f);
            // 대사창에 가려지지 않게 어깨 높이에 쓴다.
            DrawText(px, w, h, font, label, 72f, new Vector2(w / 2f, 690f), white);
            DrawText(px, w, h, font, expression, 52f, new Vector2(w / 2f, 605f), white);
            return ToTexture(px, w, h);
        }

        /// <summary>전투용 그림. 적은 넓적한 덩어리, 아군은 사람 실루엣.</summary>
        static Texture2D MakeFigure(TMP_FontAsset font, int w, int h, Color color, string label, string id, bool enemy)
        {
            var px = new Color32[w * h];
            color.a = 1f;
            if (enemy)
            {
                FillRect(px, w, h, 40, 60, 460, 420, color);
                FillRect(px, w, h, 90, 420, 410, 560, color * 1.15f);
                FillRect(px, w, h, 150, 560, 230, 640, color * 1.3f);
                FillRect(px, w, h, 270, 560, 350, 640, color * 1.3f);
            }
            else
            {
                FillRect(px, w, h, 90, 0, 410, 430, color);
                FillRect(px, w, h, 60, 330, 440, 450, color);
                FillRect(px, w, h, 160, 470, 340, 680, color * 1.1f);
            }
            var white = new Color(1f, 1f, 1f, 0.95f);
            DrawText(px, w, h, font, label, 56f, new Vector2(w / 2f, 300f), white);
            DrawText(px, w, h, font, id, 30f, new Vector2(w / 2f, 236f), white);
            return ToTexture(px, w, h);
        }

        static void FillRect(Color32[] px, int w, int h, int x0, int y0, int x1, int y1, Color color)
        {
            color.a = 1f;
            Color32 c = color;
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y1); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x1); x++)
                px[y * w + x] = c;
        }

        static Texture2D ToTexture(Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// TMP 폰트 아틀라스(SDF)에서 글자 모양을 읽어 픽셀에 직접 찍는다. 가운데 정렬, 여러 줄 지원.
        /// GPU 렌더링을 쓰지 않아서 렌더 파이프라인이나 색 공간의 영향을 받지 않는다.
        /// </summary>
        static void DrawText(Color32[] px, int w, int h, TMP_FontAsset font, string text, float sizePx, Vector2 center, Color color)
        {
            font.TryAddCharacters(text.Replace("\n", ""), out _);
            var face = font.faceInfo;
            float s = sizePx / face.pointSize;
            float k = Mathf.Clamp(0.25f / (font.atlasPadding * s), 0.02f, 0.25f); // 가장자리 부드럽게
            const float edge = 0.47f; // 0.5보다 조금 낮추면 살짝 굵어진다

            var atlasCache = new Dictionary<int, Color32[]>();
            string[] lines = text.Split('\n');
            float lineH = face.lineHeight * s;
            float top = center.y + lines.Length * lineH / 2f;

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                float width = 0f;
                foreach (char ch in line)
                    if (font.characterLookupTable.TryGetValue(ch, out var c)) width += c.glyph.metrics.horizontalAdvance * s;

                float pen = center.x - width / 2f;
                float baseline = top - face.ascentLine * s - li * lineH;

                foreach (char ch in line)
                {
                    if (!font.characterLookupTable.TryGetValue(ch, out var character)) continue;
                    var g = character.glyph;
                    var m = g.metrics;
                    var r = g.glyphRect;
                    if (r.width > 0 && r.height > 0 && m.width > 0 && m.height > 0)
                    {
                        if (!atlasCache.TryGetValue(g.atlasIndex, out var atlas))
                        {
                            atlas = ReadAtlas(font.atlasTextures[g.atlasIndex]);
                            atlasCache[g.atlasIndex] = atlas;
                        }
                        int aw = font.atlasTextures[g.atlasIndex].width;
                        int ah = font.atlasTextures[g.atlasIndex].height;

                        float x0 = pen + m.horizontalBearingX * s;
                        float yTop = baseline + m.horizontalBearingY * s;
                        float gw = m.width * s, gh = m.height * s;
                        float yBottom = yTop - gh;

                        for (int py = Mathf.Max(0, Mathf.FloorToInt(yBottom)); py < Mathf.Min(h, Mathf.CeilToInt(yTop)); py++)
                        for (int pxX = Mathf.Max(0, Mathf.FloorToInt(x0)); pxX < Mathf.Min(w, Mathf.CeilToInt(x0 + gw)); pxX++)
                        {
                            float u = (pxX + 0.5f - x0) / gw;
                            float v = (py + 0.5f - yBottom) / gh;
                            if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
                            float a = Sample(atlas, aw, ah, r.x + u * r.width, r.y + v * r.height);
                            float cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - k, edge + k, a)) * color.a;
                            if (cover <= 0f) continue;
                            int i = py * w + pxX;
                            Color dst = px[i];
                            var outC = Color.Lerp(dst, color, cover);
                            outC.a = Mathf.Max(dst.a, cover);
                            px[i] = outC;
                        }
                    }
                    pen += m.horizontalAdvance * s;
                }
            }
        }

        static float Sample(Color32[] atlas, int w, int h, float x, float y)
        {
            x -= 0.5f;
            y -= 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, w - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, h - 1);
            int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = x - Mathf.Floor(x), fy = y - Mathf.Floor(y);
            float a00 = atlas[y0 * w + x0].a, a10 = atlas[y0 * w + x1].a, a01 = atlas[y1 * w + x0].a, a11 = atlas[y1 * w + x1].a;
            return Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy) / 255f;
        }

        static Color32[] ReadAtlas(Texture2D tex)
        {
            if (tex.isReadable) return tex.GetPixels32();
            // 읽기 불가 텍스처면 GPU로 복사해서 읽는다.
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var result = copy.GetPixels32();
            UnityEngine.Object.DestroyImmediate(copy);
            return result;
        }

        // ------------------------------------------------------------ 소리 (16bit 모노 WAV)

        const int SampleRate = 22050;

        static void MakeWav(string path, ref int made, ref int skipped, Func<float[]> synth)
        {
            if (File.Exists(path))
            {
                skipped++;
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WriteWav(path, synth());
            made++;
        }

        /// <summary>낮게 울리는 드론. 8초 정확히 반복되게 주파수를 맞췄다.</summary>
        static float[] Drone()
        {
            const float seconds = 8f;
            var data = new float[(int)(SampleRate * seconds)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / SampleRate;
                float trem = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
                data[i] = trem * (0.30f * Mathf.Sin(2f * Mathf.PI * 55f * t)
                                + 0.18f * Mathf.Sin(2f * Mathf.PI * 82.5f * t)
                                + 0.06f * Mathf.Sin(2f * Mathf.PI * 110f * t));
            }
            return data;
        }

        /// <summary>심장 박동 같은 저음 펄스. 4초 반복.</summary>
        static float[] Pulse()
        {
            const float seconds = 4f, beat = 0.8f;
            var data = new float[(int)(SampleRate * seconds)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / SampleRate;
                float local = t % beat;
                float env = Mathf.Exp(-local * 9f) + 0.6f * Mathf.Exp(-Mathf.Max(0f, local - 0.18f) * 12f) * (local > 0.18f ? 1f : 0f);
                data[i] = 0.5f * env * Mathf.Sin(2f * Mathf.PI * 48f * t);
            }
            return data;
        }

        static float[] Footsteps(float volume)
        {
            var rnd = new System.Random(7);
            var data = new float[(int)(SampleRate * 0.7f)];
            foreach (float start in new[] { 0f, 0.36f })
            {
                int s0 = (int)(start * SampleRate);
                float lp = 0f;
                for (int i = 0; i < SampleRate * 0.12f && s0 + i < data.Length; i++)
                {
                    float env = Mathf.Exp(-i / (SampleRate * 0.025f));
                    lp = Mathf.Lerp(lp, (float)(rnd.NextDouble() * 2 - 1), volume < 0.5f ? 0.15f : 0.35f);
                    data[s0 + i] += 0.8f * volume * env * lp;
                }
            }
            return data;
        }

        static float[] Impact()
        {
            var rnd = new System.Random(11);
            var data = new float[(int)(SampleRate * 0.6f)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / SampleRate;
                float env = Mathf.Exp(-t * 8f);
                data[i] = env * (0.55f * Mathf.Sin(2f * Mathf.PI * (70f - 30f * t) * t) + 0.35f * (float)(rnd.NextDouble() * 2 - 1) * Mathf.Exp(-t * 25f));
            }
            return data;
        }

        static void WriteWav(string path, float[] samples)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            int dataBytes = samples.Length * 2;
            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + dataBytes);
            bw.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write((short)1); // mono
            bw.Write(SampleRate);
            bw.Write(SampleRate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(dataBytes);
            foreach (var f in samples) bw.Write((short)(Mathf.Clamp(f, -1f, 1f) * 32000f));
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
