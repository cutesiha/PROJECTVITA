using System;
using UnityEngine;

namespace ProjectVita.Core
{
    /// <summary>
    /// 세이브 슬롯용 화면 썸네일. 프레임 끝에서 화면을 찍어 작게 줄인다.
    /// GPU 복사 대신 CPU로 줄인다 (선형 색공간에서 감마가 틀어지지 않게, 한 번만 하니 느려도 괜찮다).
    /// </summary>
    public static class ScreenThumbnail
    {
        public const int Width = 384;
        public const int Height = 216;

        /// <summary>지금 화면을 찍는다. 실패하면 null. 받은 쪽에서 다 쓰면 Destroy한다.</summary>
        public static async Awaitable<Texture2D> CaptureAsync()
        {
            await Awaitable.EndOfFrameAsync();
            Texture2D full = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                return full == null ? null : Downscale(full);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[썸네일] 화면을 찍지 못했다: " + e.Message);
                return null;
            }
            finally
            {
                if (full != null) UnityEngine.Object.Destroy(full);
            }
        }

        /// <summary>칸마다 2x2 점을 평균 낸다.</summary>
        static Texture2D Downscale(Texture2D src)
        {
            var px = src.GetPixels32();
            int sw = src.width, sh = src.height;
            var dst = new Color32[Width * Height];
            float fx = sw / (float)Width, fy = sh / (float)Height;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int j = 0; j < 2; j++)
                    {
                        int sy = Mathf.Min(sh - 1, (int)((y + 0.25f + j * 0.5f) * fy));
                        for (int i = 0; i < 2; i++)
                        {
                            int sx = Mathf.Min(sw - 1, (int)((x + 0.25f + i * 0.5f) * fx));
                            var c = px[sy * sw + sx];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                        }
                    }
                    dst[y * Width + x] = new Color32((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), 255);
                }
            }
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.SetPixels32(dst);
            tex.Apply();
            return tex;
        }
    }
}
