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
        public static readonly Color Bike = Hex(0x3EF2FF);
        public static readonly Color Bonus = Hex(0xFFD23E);
        public static readonly Color Scout = Hex(0xFF5A4E);
        public static readonly Color Hauler = Hex(0xA97BFF);
        public static readonly Color Hurt = Hex(0xFF2D55);
        public static readonly Color Parcel = Hex(0x7CFFB2);
        public static readonly Color Elite = Hex(0xFFF1C2);

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
        /// Map 1, Market Street: one 16 × 16 unit tile with a 4-unit avenue, rows of stalls under striped awnings
        /// and strings of warm lanterns across the side streets. Tiles seamlessly; use it with Tiled draw mode.
        /// </summary>
        public static Sprite MarketStreet => _market != null ? _market : (_market = DrawMarketStreet());

        private static Sprite _market;

        private static Sprite DrawMarketStreet()
        {
            const int size = 256; // 16 pixels per unit
            var pixels = new Color32[size * size];
            Color awningA = Palette.Hex(0x8A2E4F), awningB = Palette.Hex(0x2E5E8A), lantern = Palette.Hex(0xFFB347);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool avenue = x < 64;                 // 4-unit avenue running north-south
                    bool street = y % 64 < 16;            // 1-unit side street every 4 units
                    Color c = avenue || street ? Palette.Road : Palette.Night;
                    if (avenue && (x == 31 || x == 32) && (y / 8) % 2 == 0)
                    {
                        c = Palette.RoadLine; // centre line
                    }
                    else if (!avenue && !street)
                    {
                        // Stalls: 3 × 2 unit blocks with a striped awning along the street edge.
                        int sx = (x - 64) % 48, sy = y % 64 - 16; // 4 stalls fill the 192 px beside the avenue
                        bool stall = sx >= 4 && sx < 44 && sy >= 6 && sy < 42;
                        bool awning = stall && sy < 14;
                        if (awning)
                        {
                            c = ((x - 64) / 6) % 2 == 0 ? awningA : awningB;
                        }
                        else if (stall)
                        {
                            c = Color.Lerp(Palette.Night, Palette.RoadLine, 0.45f);
                        }
                    }
                    else if (street && !avenue && (y % 64 == 8) && x % 16 < 3)
                    {
                        c = lantern; // a string of lanterns down the middle of the side street
                    }

                    pixels[y * size + x] = c;
                }
            }

            return Make(pixels, size, size, 16f, TextureWrapMode.Repeat);
        }

        /// <summary>Thin glowing ring, 2 units across (radius 1): scale it to a radius.</summary>
        public static Sprite Ring => _ring != null ? _ring : (_ring = Shape(64, 2.3f, (x, y) => Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 0.87f) - 0.025f));

        /// <summary>Light beam pointing right from the pivot, 1 unit long, fading with distance: scale x to the range.</summary>
        public static Sprite Beam(float halfAngleDegrees)
        {
            int key = Mathf.RoundToInt(halfAngleDegrees);
            if (Beams.TryGetValue(key, out var beam))
            {
                return beam;
            }

            const int size = 64;
            float tan = Mathf.Tan(key * Mathf.Deg2Rad);
            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) / size, y = ((py + 0.5f) / size - 0.5f) * 2f; // x 0..1 along, y -1..1 across
                    float edge = x * tan;
                    float inside = Mathf.Clamp01((edge - Mathf.Abs(y)) * size * 0.5f);
                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)(inside * (1f - x) * 255f));
                }
            }

            // 1 × 1 unit with the edges at ±x·tan of the half-height: scale x by the range and y by twice the range.
            beam = Make(pixels, size, size, size, TextureWrapMode.Clamp, new Vector2(0f, 0.5f));
            Beams[key] = beam;
            return beam;
        }

        private static readonly System.Collections.Generic.Dictionary<int, Sprite> Beams = new System.Collections.Generic.Dictionary<int, Sprite>();
        private static Sprite _ring;

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

        private static Sprite Make(Color32[] pixels, int w, int h, float pixelsPerUnit, TextureWrapMode wrap, Vector2? pivot = null)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, w, h), pivot ?? new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
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
