using System;
using UnityEngine;

namespace NightCourier.Art
{
    /// <summary>Neon Ward at night. Every colour in the game comes from here.</summary>
    public static class Palette
    {
        public static readonly Color Night = Hex(0x0B1026);
        public static readonly Color Road = Hex(0x141C3D);
        public static readonly Color RoadLine = Hex(0x26336A);
        public static readonly Color Window = Hex(0x3EF2FF, 0.55f);
        public static readonly Color Bike = Hex(0x3EF2FF);
        public static readonly Color Bonus = Hex(0xFFD23E);
        public static readonly Color Scout = Hex(0xFF5A4E);
        public static readonly Color Hauler = Hex(0xA97BFF);
        public static readonly Color Hurt = Hex(0xFF2D55);

        public static Color Hex(int rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }

    /// <summary>
    /// Shapes drawn into textures at startup from signed distance functions, so the game runs with zero art
    /// files. White with a soft glow; renderers tint them. Bodies match the gameplay radii in RideTuning.
    /// Swap for real sprites later without touching gameplay.
    /// </summary>
    public static class NeonArt
    {
        private static Sprite _bike;
        private static Sprite _scout;
        private static Sprite _hauler;
        private static Sprite _dot;
        private static Sprite _ground;

        /// <summary>The bike seen from above, pointing right: about 1 unit long.</summary>
        public static Sprite Bike => _bike != null ? _bike : (_bike = Shape(64, 1.4f, (x, y) =>
            Mathf.Min(Capsule(x, y, -0.62f, 0.62f, 0.13f), Capsule(y, x - 0.42f, -0.34f, 0.34f, 0.07f))));

        /// <summary>Diamond drone: body about 0.6 units across.</summary>
        public static Sprite Scout => _scout != null ? _scout : (_scout = Shape(48, 1.1f, (x, y) =>
            (Mathf.Abs(x) + Mathf.Abs(y * 1.25f)) * 0.75f - 0.42f));

        /// <summary>Square hauler with a hollow core: body about 1.1 units across.</summary>
        public static Sprite Hauler => _hauler != null ? _hauler : (_hauler = Shape(64, 2.2f, (x, y) =>
            Mathf.Abs(Box(x, y, 0.5f, 0.12f) + 0.1f) - 0.1f));

        /// <summary>Soft round glow, 1 unit across.</summary>
        public static Sprite Dot => _dot != null ? _dot : (_dot = Shape(32, 1f, (x, y) => Mathf.Sqrt(x * x + y * y) - 0.25f));

        /// <summary>
        /// One 8 × 8 unit city block: dark lots, lighter roads with lane marks, a few lit windows. Tiles seamlessly;
        /// use it with <see cref="SpriteDrawMode.Tiled"/>.
        /// </summary>
        public static Sprite Ground => _ground != null ? _ground : (_ground = DrawGround());

        private static Sprite Shape(int size, float units, Func<float, float, float> distance)
        {
            var pixels = new Color32[size * size];
            float pixel = 2f / size;
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    // -1..1 across the texture.
                    float x = (px + 0.5f) * pixel - 1f, y = (py + 0.5f) * pixel - 1f;
                    float d = distance(x, y);
                    float body = Mathf.Clamp01(0.5f - d / pixel);
                    float glow = Mathf.Exp(-Mathf.Max(d, 0f) * 9f) * 0.45f;
                    float a = Mathf.Max(body, glow);
                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            return Make(pixels, size, size, size / units, TextureWrapMode.Clamp);
        }

        private static Sprite DrawGround()
        {
            const int size = 128; // 16 pixels per unit
            var pixels = new Color32[size * size];
            var rng = new System.Random(7);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool road = x < 20 || y < 20;
                    bool lane = (x == 9 || x == 10) && (y / 6) % 2 == 0 || (y == 9 || y == 10) && (x / 6) % 2 == 0;
                    Color c = road ? Palette.Road : Palette.Night;
                    if (lane)
                    {
                        c = Palette.RoadLine;
                    }
                    else if (!road && x % 12 == 2 && y % 10 == 4 && rng.NextDouble() < 0.35)
                    {
                        c = Color.Lerp(Palette.Night, Palette.Window, 0.5f);
                    }

                    pixels[y * size + x] = c;
                }
            }

            return Make(pixels, size, size, 16f, TextureWrapMode.Repeat);
        }

        private static Sprite Make(Color32[] pixels, int w, int h, float pixelsPerUnit, TextureWrapMode wrap)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Distance to a horizontal capsule from (x0, 0) to (x1, 0).</summary>
        private static float Capsule(float x, float y, float x0, float x1, float radius)
        {
            float cx = Mathf.Clamp(x, x0, x1);
            return Mathf.Sqrt((x - cx) * (x - cx) + y * y) - radius;
        }

        private static float Box(float x, float y, float half, float corner)
        {
            float qx = Mathf.Abs(x) - half + corner, qy = Mathf.Abs(y) - half + corner;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - corner;
        }
    }
}
