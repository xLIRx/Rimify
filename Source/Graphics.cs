using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Rimify
{
    // Procedural UI textures and icons / Процедурные текстуры и векторные иконки интерфейса
    [StaticConstructorOnStartup]
    public static class TexIcons
    {
        // --- PlaySettings Music Toggle Icon / Иконка ноты в ванильном стиле с контуром ---
        public static readonly Texture2D MusicToggle = CreateIcon(24, 24, (x, y) =>
        {
            float DistToSegment(float px, float py, float x1, float y1, float x2, float y2)
            {
                float dx = x2 - x1;
                float dy = y2 - y1;
                float lenSq = dx * dx + dy * dy;
                float t = Mathf.Clamp01(((px - x1) * dx + (py - y1) * dy) / lenSq);
                float nx = x1 + t * dx;
                float ny = y1 + t * dy;
                return Mathf.Sqrt((px - nx) * (px - nx) + (py - ny) * (py - ny));
            }

            float h1x = (x - 6.5f) * 0.866f + (y - 6.2f) * 0.5f;
            float h1y = -(x - 6.5f) * 0.5f + (y - 6.2f) * 0.866f;
            float dHead1 = (h1x * h1x) / 5.8f + (h1y * h1y) / 3.2f;

            float h2x = (x - 15.5f) * 0.866f + (y - 9.2f) * 0.5f;
            float h2y = -(x - 15.5f) * 0.5f + (y - 9.2f) * 0.866f;
            float dHead2 = (h2x * h2x) / 5.8f + (h2y * h2y) / 3.2f;

            float dStem1 = DistToSegment(x, y, 8.5f, 6.2f, 8.5f, 19.5f);
            float dStem2 = DistToSegment(x, y, 17.5f, 9.2f, 17.5f, 22.5f);
            float dBeam = DistToSegment(x, y, 8.5f, 19.5f, 17.5f, 22.5f);

            bool isInsideBody = dHead1 <= 1.0f || dHead2 <= 1.0f || dStem1 <= 1.0f || dStem2 <= 1.0f || dBeam <= 1.35f;
            if (isInsideBody) return new Color(0.66f, 0.67f, 0.69f);

            bool isOutline = dHead1 <= 2.2f || dHead2 <= 2.2f || dStem1 <= 2.05f || dStem2 <= 2.05f || dBeam <= 2.45f;
            if (isOutline) return new Color(0.12f, 0.13f, 0.15f);

            return Color.clear;
        });

        // --- Shuffle Active Icon (Crossing Arrows) / Иконка перемешивания (активна) ---
        public static readonly Texture2D Shuffle = CreateIcon(32, 32, (x, y) =>
        {
            bool line1 = (x >= 6f && x <= 13f && Mathf.Abs(y - 21f) <= 1.4f) ||
                         (x >= 13f && x <= 19f && Mathf.Abs((y - 21f) + (x - 13f) * 1.8f) <= 1.8f) ||
                         (x >= 19f && x <= 24f && Mathf.Abs(y - 10f) <= 1.4f);

            bool line2 = (x >= 6f && x <= 13f && Mathf.Abs(y - 10f) <= 1.4f) ||
                         (x >= 13f && x <= 19f && Mathf.Abs((y - 10f) - (x - 13f) * 1.8f) <= 1.8f) ||
                         (x >= 19f && x <= 24f && Mathf.Abs(y - 21f) <= 1.4f);

            bool arrowUp = (x >= 21f && x <= 26f && Mathf.Abs(y - 21f) <= (26f - x) * 1.5f && x + (y - 21f) <= 26f);
            bool arrowDown = (x >= 21f && x <= 26f && Mathf.Abs(y - 10f) <= (26f - x) * 1.5f && x - (y - 10f) <= 26f);

            return (line1 || line2 || arrowUp || arrowDown) ? Color.white : Color.clear;
        });

        // --- Sequential Icon (Straight Parallel Arrows) / Иконка воспроизведения по порядку ---
        public static readonly Texture2D Sequential = CreateIcon(32, 32, (x, y) =>
        {
            bool line1 = (x >= 6f && x <= 22f && Mathf.Abs(y - 20f) <= 1.4f);
            bool arrow1 = (x >= 19f && x <= 25f && Mathf.Abs(y - 20f) <= (25f - x) * 1.5f && (x + Mathf.Abs(y - 20f) <= 25f));

            bool line2 = (x >= 6f && x <= 22f && Mathf.Abs(y - 11f) <= 1.4f);
            bool arrow2 = (x >= 19f && x <= 25f && Mathf.Abs(y - 11f) <= (25f - x) * 1.5f && (x + Mathf.Abs(y - 11f) <= 25f));

            return (line1 || arrow1 || line2 || arrow2) ? Color.white : Color.clear;
        });

        // --- Repeat Single Track Icon (Loop with '1') / Повтор одного трека ---
        public static readonly Texture2D Repeat = CreateIcon(32, 32, (x, y) =>
        {
            float dx = x - 15.5f;
            float dy = y - 15.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);

            bool ring = r >= 8f && r <= 11f && !(x >= 15f && y >= 11f && y <= 20f);
            bool tip = (x >= 14f && x <= 21f && y >= 21f && y <= 27f && Mathf.Abs(x - 17.5f) <= (y - 21f));

            bool one = (x >= 14.5f && x <= 16.5f && y >= 11f && y <= 19f) ||
                       (x >= 12.5f && x <= 15f && y >= 16.5f && y <= 18.5f && (18.5f - y) >= (15f - x));

            return (ring || tip || one) ? Color.white : Color.clear;
        });

        // --- Repeat Loop All Icon / Обычная петля без цифры 1 ---
        public static readonly Texture2D RepeatAll = CreateIcon(32, 32, (x, y) =>
        {
            float dx = x - 15.5f;
            float dy = y - 15.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);

            bool ring = r >= 8f && r <= 11f && !(x >= 15f && y >= 11f && y <= 20f);
            bool tip = (x >= 14f && x <= 21f && y >= 21f && y <= 27f && Mathf.Abs(x - 17.5f) <= (y - 21f));

            return (ring || tip) ? Color.white : Color.clear;
        });

        // --- Continuous Playback Icon (Infinity Symbol) / Непрерывное воспроизведение (Бесконечность) ---
        public static readonly Texture2D Continuous = CreateIcon(32, 32, (x, y) =>
        {
            float cy = y - 15.5f;
            // Left loop
            float d1 = Mathf.Sqrt((x - 10.5f) * (x - 10.5f) + cy * cy);
            bool l1 = d1 >= 4.5f && d1 <= 7.2f && x <= 16f;

            // Right loop
            float d2 = Mathf.Sqrt((x - 20.5f) * (x - 20.5f) + cy * cy);
            bool l2 = d2 >= 4.5f && d2 <= 7.2f && x >= 15f;

            // Center crossing
            bool cross = Mathf.Abs(cy) <= 2.2f && Mathf.Abs(x - 15.5f) <= 2.5f && Mathf.Abs(cy - (x - 15.5f) * 0.7f) <= 1.4f;

            return (l1 || l2 || cross) ? Color.white : Color.clear;
        });

        // --- Atmospheric Pause Icon (Hourglass) / Атмосферная тишина и паузы (Песочные часы) ---
        public static readonly Texture2D Atmospheric = CreateIcon(32, 32, (x, y) =>
        {
            float cx = Mathf.Abs(x - 15.5f);
            float cy = Mathf.Abs(y - 15.5f);

            // Top and bottom plates
            if ((y >= 6f && y <= 8f && cx <= 8.5f) || (y >= 23f && y <= 25f && cx <= 8.5f))
                return Color.white;

            // Diagonal glass walls
            float expectedW = (cy / 8.5f) * 7.5f + 1.2f;
            if (cy <= 8.5f && Mathf.Abs(cx - expectedW) <= 1.2f)
                return Color.white;

            // Sand inside
            if (y >= 9f && y <= 13f && cx <= (14f - y) * 1.2f + 1f)
                return Color.white;

            return Color.clear;
        });

        // --- Player Controls ---
        public static readonly Texture2D Play = CreateIcon(32, 32, (x, y) =>
        {
            float px = (x - 10f) / 14f;
            if (px < 0f || px > 1f) return Color.clear;
            float halfH = (1f - px) * 8.5f;
            return Mathf.Abs(y - 15.5f) <= halfH ? Color.white : Color.clear;
        });

        public static readonly Texture2D Pause = CreateIcon(32, 32, (x, y) =>
        {
            bool b1 = x >= 9f && x <= 13f && y >= 7f && y <= 24f;
            bool b2 = x >= 18f && x <= 22f && y >= 7f && y <= 24f;
            return (b1 || b2) ? Color.white : Color.clear;
        });

        public static readonly Texture2D Prev = CreateIcon(32, 32, (x, y) =>
        {
            bool bar = x >= 7f && x <= 9f && y >= 7f && y <= 24f;
            bool tri = false;
            if (x >= 12f && x <= 23f)
            {
                float px = (x - 12f) / 11f;
                tri = Mathf.Abs(y - 15.5f) <= px * 7.5f;
            }
            return (bar || tri) ? Color.white : Color.clear;
        });

        public static readonly Texture2D Next = CreateIcon(32, 32, (x, y) =>
        {
            bool bar = x >= 22f && x <= 24f && y >= 7f && y <= 24f;
            bool tri = false;
            if (x >= 8f && x <= 19f)
            {
                float px = (19f - x) / 11f;
                tri = Mathf.Abs(y - 15.5f) <= px * 7.5f;
            }
            return (bar || tri) ? Color.white : Color.clear;
        });

        // --- Tag: Combat ---
        public static readonly Texture2D Combat = CreateIcon(32, 32, (x, y) =>
        {
            float cx = 15.5f;
            float dx = Mathf.Abs(x - cx);

            if (y >= 11f && y <= 28f)
            {
                float t = (y - 11f) / 17f;
                float bladeW = (1f - t) * 4.2f;

                if (dx <= bladeW + 0.6f)
                {
                    if (dx <= 0.8f) return new Color(1f, 0.95f, 0.95f);
                    if (dx <= bladeW) return new Color(0.95f, 0.20f, 0.20f);
                    return new Color(0.55f, 0.08f, 0.08f);
                }
            }

            if (y >= 9f && y <= 11.5f && dx <= 7.5f)
            {
                if (dx >= 6f) return new Color(1f, 0.85f, 0.25f);
                return new Color(0.85f, 0.68f, 0.15f);
            }

            if (y >= 5f && y < 9f && dx <= 1.4f) return new Color(0.35f, 0.25f, 0.20f);

            if (y >= 2.5f && y <= 5.2f && dx <= 2.2f)
            {
                float pr = Mathf.Sqrt(dx * dx + (y - 3.8f) * (y - 3.8f));
                if (pr <= 2.2f) return new Color(0.95f, 0.78f, 0.20f);
            }

            return Color.clear;
        });

        // --- Tag: Peaceful ---
        public static readonly Texture2D Peaceful = CreateIcon(32, 32, (x, y) =>
        {
            float u = (x - y) * 0.7071f;
            float v = (x + y - 31f) * 0.7071f;

            if (v < -11f || v > 11f) return Color.clear;

            float maxW = (1f - (v * v) / 121f) * 6.5f;
            if (Mathf.Abs(u) <= maxW)
            {
                if (Mathf.Abs(u) <= 0.7f) return new Color(0.8f, 1f, 0.85f);
                return new Color(0.18f, 0.80f, 0.44f);
            }

            if (v <= -9f && v >= -14f && Mathf.Abs(u - (v + 11f) * 0.4f) <= 0.9f)
            {
                return new Color(0.14f, 0.65f, 0.35f);
            }

            return Color.clear;
        });

        // --- Tag: Day ---
        public static readonly Texture2D Day = CreateIcon(32, 32, (x, y) =>
        {
            float dx = x - 15.5f;
            float dy = y - 15.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);

            if (r <= 5.5f) return new Color(1f, 0.82f, 0.15f);

            if (r >= 7.5f && r <= 13.5f)
            {
                float angle = Mathf.Atan2(dy, dx);
                float normAngle = Mathf.Repeat(angle + Mathf.PI * 0.125f, Mathf.PI * 0.25f) - Mathf.PI * 0.125f;
                float rayWidth = (14f - r) * 0.18f;
                if (Mathf.Abs(normAngle) <= rayWidth)
                {
                    return new Color(1f, 0.72f, 0.05f);
                }
            }

            return Color.clear;
        });

        // --- Tag: Night ---
        public static readonly Texture2D Night = CreateIcon(32, 32, (x, y) =>
        {
            float dx1 = x - 15.0f;
            float dy1 = y - 15.5f;
            float r1 = Mathf.Sqrt(dx1 * dx1 + dy1 * dy1);

            float dx2 = x - 19.5f;
            float dy2 = y - 17.5f;
            float r2 = Mathf.Sqrt(dx2 * dx2 + dy2 * dy2);

            if (r1 <= 10f && r2 >= 8.5f && x <= 22f) return new Color(0.35f, 0.72f, 0.98f);

            float sx = Mathf.Abs(x - 9f);
            float sy = Mathf.Abs(y - 21f);
            if (sx + sy <= 2.2f) return new Color(1f, 0.92f, 0.65f);

            return Color.clear;
        });

        // --- Tag: Any Time ---
        public static readonly Texture2D AnyTime = CreateIcon(32, 32, (x, y) =>
        {
            float dx = x - 15.5f;
            float dy = y - 15.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);

            if (r > 10f) return Color.clear;
            if (Mathf.Abs(dx) <= 0.6f) return new Color(0.2f, 0.22f, 0.25f);
            if (dx < 0f) return new Color(1f, 0.82f, 0.15f);

            float cutR = Mathf.Sqrt((x - 17.5f) * (x - 17.5f) + dy * dy);
            return cutR >= 5.5f ? new Color(0.35f, 0.72f, 0.98f) : Color.clear;
        });

        // --- Tag: Rotation Active ---
        public static readonly Texture2D Audible = CreateIcon(32, 32, (x, y) =>
        {
            float cy = 15.5f;
            float dy = Mathf.Abs(y - cy);

            bool box = (x >= 6f && x <= 10f && dy <= 4f);
            bool cone = (x >= 10f && x <= 15f && dy <= (x - 10f) * 1.5f + 4f);
            if (box || cone) return new Color(0.30f, 0.85f, 0.45f);

            float dWave1 = Mathf.Sqrt((x - 12f) * (x - 12f) + dy * dy);
            if (dWave1 >= 7.5f && dWave1 <= 9.5f && x >= 17f && dy <= 6.5f) return new Color(0.45f, 0.95f, 0.60f);

            float dWave2 = Mathf.Sqrt((x - 12f) * (x - 12f) + dy * dy);
            if (dWave2 >= 12f && dWave2 <= 14f && x >= 21f && dy <= 9.5f) return new Color(0.55f, 0.98f, 0.70f);

            return Color.clear;
        });

        // --- Tag: Rotation Muted ---
        public static readonly Texture2D Muted = CreateIcon(32, 32, (x, y) =>
        {
            float cy = 15.5f;
            float dy = Mathf.Abs(y - cy);

            bool box = (x >= 6f && x <= 10f && dy <= 4f);
            bool cone = (x >= 10f && x <= 15f && dy <= (x - 10f) * 1.5f + 4f);

            float slash = Mathf.Abs(x - (31f - y));
            if (slash <= 1.4f && x >= 6f && x <= 26f) return new Color(0.95f, 0.22f, 0.22f);
            if (box || cone) return new Color(0.48f, 0.50f, 0.52f);

            return Color.clear;
        });

        // --- Tag: Add To Playlist ---
        public static readonly Texture2D AddToPlaylist = CreateIcon(32, 32, (x, y) =>
        {
            float dx = Mathf.Abs(x - 15.5f);
            float dy = Mathf.Abs(y - 15.5f);
            return (dy <= 1.5f && dx <= 8f) || (dx <= 1.5f && dy <= 8f) ? new Color(0.85f, 0.88f, 0.92f) : Color.clear;
        });

        // --- Tag: Remove From Playlist ---
        public static readonly Texture2D RemoveFromPlaylist = CreateIcon(32, 32, (x, y) =>
        {
            float dx = Mathf.Abs(x - 15.5f);
            float dy = Mathf.Abs(y - 15.5f);
            return (dy <= 1.5f && dx <= 8f) ? new Color(0.95f, 0.35f, 0.35f) : Color.clear;
        });

        // --- Tag: Trash Can ---
        public static readonly Texture2D Trash = CreateIcon(32, 32, (x, y) =>
        {
            bool handle = (x >= 13f && x <= 18f && y >= 25f && y <= 27f) && !(x >= 14.5f && x <= 16.5f && y == 25f);
            bool lid = (x >= 7f && x <= 24f && y >= 22f && y <= 24f);

            if (y >= 6f && y <= 20f)
            {
                float progress = (y - 6f) / 14f;
                float xLeft = 10f - progress * 1.5f;
                float xRight = 21f + progress * 1.5f;

                if (x >= xLeft && x <= xRight)
                {
                    bool rib1 = (x >= 12.5f && x <= 13.8f && y >= 8f && y <= 18f);
                    bool rib2 = (x >= 15.2f && x <= 16.5f && y >= 8f && y <= 18f);
                    bool rib3 = (x >= 17.8f && x <= 19.1f && y >= 8f && y <= 18f);

                    if (rib1 || rib2 || rib3) return Color.clear;
                    return new Color(0.95f, 0.32f, 0.32f);
                }
            }

            if (handle || lid) return new Color(0.95f, 0.32f, 0.32f);
            return Color.clear;
        });

        // --- Fallback Source Icons ---
        public static readonly Texture2D SourceCustom = CreateIcon(24, 24, (x, y) =>
        {
            if (x >= 2f && x <= 21f && y >= 4f && y <= 19f)
            {
                if (x >= 9f && x <= 16f)
                {
                    float p = (x - 9f) / 7f;
                    if (Mathf.Abs(y - 11.5f) <= (1f - p) * 4f) return Color.white;
                }
                return new Color(0.90f, 0.18f, 0.15f);
            }
            return Color.clear;
        });

        public static readonly Texture2D SourceCore = CreateIcon(24, 24, (x, y) =>
        {
            float d2 = (x - 11.5f) * (x - 11.5f) + (y - 11.5f) * (y - 11.5f);
            if (d2 <= 25f) return new Color(0.45f, 0.7f, 0.95f);
            if (Mathf.Abs((x - 11.5f) + (y - 11.5f)) <= 1.2f && d2 <= 90f && d2 >= 16f) return new Color(0.85f, 0.85f, 0.85f);
            return Color.clear;
        });

        public static readonly Texture2D SourceMod = CreateIcon(24, 24, (x, y) =>
        {
            if ((x >= 4f && x <= 19f && (Mathf.Abs(y - 5f) <= 0.6f || Mathf.Abs(y - 18f) <= 0.6f)) ||
                (y >= 5f && y <= 18f && (Mathf.Abs(x - 4f) <= 0.6f || Mathf.Abs(x - 19f) <= 0.6f)))
            {
                return new Color(0.85f, 0.65f, 0.35f);
            }
            return Color.clear;
        });

        private static Texture2D CreateIcon(int w, int h, Func<float, float, Color> colorSampler)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Color s1 = colorSampler(x + 0.25f, y + 0.25f);
                    Color s2 = colorSampler(x + 0.75f, y + 0.25f);
                    Color s3 = colorSampler(x + 0.25f, y + 0.75f);
                    Color s4 = colorSampler(x + 0.75f, y + 0.75f);

                    float r = (s1.r * s1.a + s2.r * s2.a + s3.r * s3.a + s4.r * s4.a) * 0.25f;
                    float g = (s1.g * s1.a + s2.g * s2.a + s3.g * s3.a + s4.g * s4.a) * 0.25f;
                    float b = (s1.b * s1.a + s2.b * s2.a + s3.b * s3.a + s4.b * s4.a) * 0.25f;
                    float a = (s1.a + s2.a + s3.a + s4.a) * 0.25f;

                    tex.SetPixel(x, y, new Color(r, g, b, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }

    public static class SourceIconCache
    {
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D GetIcon(SongDef song)
        {
            if (song == null) return TexIcons.SourceCore;
            if (song.defName != null && (song.defName.StartsWith("Rimify_") || song.defName.StartsWith("YTM_")))
                return TexIcons.SourceCustom;

            string key = "Core";
            if (song.modContentPack != null)
            {
                key = song.modContentPack.PackageIdPlayerFacing ?? song.modContentPack.PackageId ?? "Mod";
            }
            if (string.IsNullOrEmpty(key)) key = "Core";

            if (cache.TryGetValue(key, out Texture2D cached)) return cached;

            Texture2D result = ResolveIcon(song);
            cache[key] = result;
            return result;
        }

        private static Texture2D ResolveIcon(SongDef song)
        {
            if (song.modContentPack != null && ModLister.AllExpansions != null)
            {
                foreach (var exp in ModLister.AllExpansions)
                {
                    if (exp != null && !string.IsNullOrEmpty(exp.linkedMod))
                    {
                        if (exp.linkedMod.Equals(song.modContentPack.PackageId, StringComparison.OrdinalIgnoreCase) ||
                            exp.linkedMod.Equals(song.modContentPack.PackageIdPlayerFacing, StringComparison.OrdinalIgnoreCase))
                        {
                            if (exp.Icon != null) return exp.Icon;
                        }
                    }
                }
            }

            if (song.modContentPack != null)
            {
                var meta = (!string.IsNullOrEmpty(song.modContentPack.PackageIdPlayerFacing) ? ModLister.GetModWithIdentifier(song.modContentPack.PackageIdPlayerFacing) : null) ??
                           (!string.IsNullOrEmpty(song.modContentPack.PackageId) ? ModLister.GetModWithIdentifier(song.modContentPack.PackageId) : null);

                if (meta?.Icon != null) return meta.Icon;
            }
            else
            {
                var coreMeta = ModLister.GetModWithIdentifier("ludeon.rimworld");
                if (coreMeta?.Icon != null) return coreMeta.Icon;
            }

            return song.modContentPack != null && !song.modContentPack.IsCoreMod ? TexIcons.SourceMod : TexIcons.SourceCore;
        }
    }
}