using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts.Palette
{
    public interface IPaletteMapper
    {
        int PaletteSize { get; }
        Color GetColor(long colorId);
        ColorInfo GetColorInfo(long colorId, int? flightGroupColor);
    }

    public abstract class PaletteMapper : IPaletteMapper
    {
        public PaletteMapper(List<Color> palette, params Color[] customFlightGroupColors)
        {
            // Repeat the colors to prevent Unity from blending adjacent colors together
            var repeatedFlightGroupColors = new Color[customFlightGroupColors.Length * 3];
            for (var i = 0; i < customFlightGroupColors.Length; i++)
            {
                repeatedFlightGroupColors[i * 3] = customFlightGroupColors[i];
                repeatedFlightGroupColors[i * 3 + 1] = customFlightGroupColors[i];
                repeatedFlightGroupColors[i * 3 + 2] = customFlightGroupColors[i];
            }

            _palette = palette
                .Concat(BaseGlowColors)
                .Concat(repeatedFlightGroupColors)
                .ToArray();

            GlowStartIndex = palette.Count;
            GlowRedIndex = GlowStartIndex + 1;
            GlowGreenIndex = GlowStartIndex + 4;
            GlowBlueIndex = GlowStartIndex + 7;

            CustomFlightGroupColorStartIndex = palette.Count + BaseGlowColors.Length;
            CustomFlightGroupColorCount = customFlightGroupColors.Length;
        }

        // Create 3 of each color to keep Unity from blending adjacent colors together
        private static Color[] GlowColors = new Color[]
        {
            Color.red,
            Color.red,
            Color.red,
            Color.green,
            Color.green,
            Color.green,
            Color.blue,
            Color.blue,
            Color.blue
        };

        private static Color[] BaseGlowColors = new Color[]
        {
            new Color(0.3f, 0f, 0f),
            new Color(0.3f, 0f, 0f),
            new Color(0.3f, 0f, 0f),
            new Color(0f, 0.3f, 0f),
            new Color(0f, 0.3f, 0f),
            new Color(0f, 0.3f, 0f),
            new Color(0f, 0f, 0.3f),
            new Color(0f, 0f, 0.3f),
            new Color(0f, 0f, 0.3f)
        };

        protected Color[] _palette;

        protected abstract int CockpitStartIndex { get; }
        protected abstract int CockpitPaletteLength { get; }

        protected int GlowStartIndex { get; private set; }
        protected int GlowRedIndex { get; private set; }
        protected int GlowGreenIndex { get; private set; }
        protected int GlowBlueIndex { get; private set; }

        protected int CustomFlightGroupColorStartIndex { get; private set; }
        protected int CustomFlightGroupColorCount { get; private set; }

        public int PaletteSize => _palette.Length;

        private static Color ColorFromBytes(byte[] bytes) => new Color(Convert.ToSingle(bytes[0]) / 63, Convert.ToSingle(bytes[1]) / 63, Convert.ToSingle(bytes[2]) / 63);

        public Color GetColor(long colorId)
        {
            var colorInfo = GetColorInfo(colorId, null);

            if (colorInfo.OverrideColor.HasValue)
                return colorInfo.OverrideColor.Value;

            if (colorInfo.Index.HasValue)
                return _palette[colorInfo.Index.Value];
            
            if (colorInfo.OverrideColor.HasValue)
                return colorInfo.OverrideColor.Value;

            Debug.LogWarning($"Unknown color: {colorId} ({colorId:X})");
            return Color.magenta;
        }

        public abstract ColorInfo GetColorInfo(long colorId, int? flightGroupColor);

        public Texture GeneratePaletteTexture()
        {
            var texture = new Texture2D(PaletteSize, 1);

            texture.SetPixels(_palette);

            texture.Apply();

            return texture;
        }

        public Texture GenerateSpecularMap()
        {
            var texture = new Texture2D(PaletteSize, 1, TextureFormat.RGBA32, false);

            texture.SetPixels32(Enumerable.Repeat(new Color32(0, 0, 0, 0), PaletteSize).ToArray());
            texture.SetPixels32(CockpitStartIndex, 0, CockpitPaletteLength, 1, Enumerable.Repeat(new Color32(0x8f, 0x8f, 0x8f, 0xff), CockpitPaletteLength).ToArray());

            texture.Apply();

            return texture;
        }

        public Texture GenerateEmissionMap()
        {
            var texture = new Texture2D(PaletteSize, 1, TextureFormat.RGBA32, false);

            texture.SetPixels(Enumerable.Repeat(new Color(0, 0, 0), PaletteSize).ToArray());
            texture.SetPixels(GlowStartIndex, 0, GlowColors.Length, 1, GlowColors);

            texture.Apply();

            return texture;
        }

        public static List<Color> LoadPalette(string filename)
        {
            using (var fs = File.OpenRead(filename))
            {
                return LoadPalette(fs);
            }
        }

        public static List<Color> LoadPalette(Stream fs)
        {
            var entryCount = fs.Length / 3;
            var palette = new byte[entryCount][];

            for (var i = 0; i < entryCount; i++)
            {
                var paletteEntry = new byte[3];
                fs.Read(paletteEntry, 0, 3);
                palette[i] = paletteEntry;
            }

            return palette.Select(x => ColorFromBytes(x)).ToList();
        }
    }

    public class XWingPaletteMapper : PaletteMapper
    {
        public XWingPaletteMapper(List<Color> palette, params Color[] customFlightGroupColors) : base(palette, customFlightGroupColors) { }

        private const int
            ImperialBlue = 0,  // 0-15: imperial blue (16)
            ImperialGray = 16, // 16-31: imperial gray (16)
            RebelBeige = 32,   // 32-47: rebel beige (16)
            Gray = 48,         // 48-63: gray (16)
            Yellow = 64,       // 64-75: yellow (12)
            Red = 76,          // 76-87: red (12)
            Blue = 88,         // 88-99: blue (12)
            Cockpit = 100,     // 100-111: cockpit (12)
            DeathStar = 112;   // 112-127: Death Star (16)

        private const int
            RegularOffset = 9, // Pick something in a standard 12-entry palette section
            LowOffset = 7, // First 12 entries in a large 16-entry palette section (brighter)
            MidOffset = 11, // Last 12 entries in a large 16-entry palette section
            HighOffset = 15; // Last 8 entries in a large 16-entry palette section (darker)

        protected override int CockpitStartIndex => Cockpit;
        protected override int CockpitPaletteLength => 12;

        public override ColorInfo GetColorInfo(long colorId, int? flightGroupColor)
        {
            switch (colorId)
            {
                case 0x1: return new ColorInfo(ImperialBlue + LowOffset); // first 12
                case 0x2: return new ColorInfo(ImperialGray + LowOffset); // first 12
                case 0x3: return new ColorInfo(RebelBeige + LowOffset); // first 12
                case 0x4: return new ColorInfo(Gray + LowOffset); // first 12
                case 0x5: return new ColorInfo(ImperialBlue + MidOffset); // last 12
                case 0x6: return new ColorInfo(ImperialGray + MidOffset); // last 12
                case 0x7: return new ColorInfo(RebelBeige + MidOffset); // last 12
                case 0x8: return new ColorInfo(Gray + MidOffset); // last 12
                case 0x9: return new ColorInfo(ImperialBlue + HighOffset); // last 8
                case 0xa: return new ColorInfo(ImperialGray + HighOffset); // last 8
                case 0xb: return new ColorInfo(RebelBeige + HighOffset); // last 8
                case 0xc: return new ColorInfo(Gray + HighOffset); // last 8

                case 0xd: return new ColorInfo(Yellow + RegularOffset); // all 12

                case 0xe: // used for flight group color; all 12
                    if (flightGroupColor.HasValue)
                    {
                        if (flightGroupColor.Value == 1)
                            return new ColorInfo(Blue + RegularOffset);

                        if (flightGroupColor.Value == 2)
                            return new ColorInfo(Yellow + RegularOffset);

                        var customColor = flightGroupColor.Value - 3;

                        if (customColor >= 0 && customColor < CustomFlightGroupColorCount)
                            return new ColorInfo(CustomFlightGroupColorStartIndex + (customColor * 3 + 1));
                    }

                    return new ColorInfo(Red + RegularOffset);

                case 0xf: return new ColorInfo(Blue + RegularOffset); // all 12

                // TODO: cockpit really needs some additional specular/smoothness mapping
                case 0x10: return new ColorInfo(Cockpit + 6); // all 12 colors
                case 0x11: return new ColorInfo(Cockpit + 7); // last 8 colors

                case 0x12: return new ColorInfo(DeathStar + LowOffset); // all 16 colors
                case 0x13: return new ColorInfo(DeathStar + MidOffset); // last 12
                case 0x14: return new ColorInfo(DeathStar + HighOffset); // last 8

                case 0x15: return new ColorInfo(GlowGreenIndex); // flashing green
                case 0x16: return new ColorInfo(GlowRedIndex); // flashing red (red engines)
                case 0x17: return new ColorInfo(GlowBlueIndex); // flashing blue (blue engines)

                case 0x18: return new ColorInfo(Red + LowOffset); // first 8
                case 0x19: return new ColorInfo(Red + RegularOffset); // middle 6
                case 0x1a: return new ColorInfo(Red + MidOffset); // last 6

                case 0x1b: return new ColorInfo(Gray + HighOffset); // darkest 4 from gray in the game

                // more research needed for these colors:
                case 0x1d: return new ColorInfo(ImperialGray + LowOffset);
                case 0x1e: return new ColorInfo(RebelBeige + MidOffset);
                case 0x1f: return new ColorInfo(Gray + LowOffset + 1);

                case 0x20: return new ColorInfo(ImperialBlue + MidOffset);
                case 0x21: return new ColorInfo(ImperialGray + MidOffset);
                case 0x22: return new ColorInfo(RebelBeige + LowOffset);
                case 0x23: return new ColorInfo(Gray + LowOffset + 2);
                case 0x24: return new ColorInfo(ImperialBlue + MidOffset - 1);
                case 0x25: return new ColorInfo(ImperialGray + MidOffset - 1);
                case 0x26: return new ColorInfo(RebelBeige + MidOffset - 2);
                case 0x27: return new ColorInfo(Gray + MidOffset - 2);
                case 0x28: return new ColorInfo(Gray + LowOffset - 1);
                case 0x48: return new ColorInfo(Gray + MidOffset - 1);
                case 0x68: return new ColorInfo(Gray + HighOffset - 1);

                case 0x82: return new ColorInfo(ImperialGray + MidOffset);
                case 0x83: return new ColorInfo(RebelBeige + MidOffset);
                case 0x84: return new ColorInfo(Gray + MidOffset);

                case 0x8c: return new ColorInfo(Gray + HighOffset - 2);
                case 0x8d: return new ColorInfo(Yellow + RegularOffset);
                case 0x8f: return new ColorInfo(Blue + RegularOffset);

                case 0x96: return new ColorInfo(GlowRedIndex); // flashing red (red engines)
                case 0x97: return new ColorInfo(GlowBlueIndex); // flashing blue (blue engines)

                case 0x9b: return new ColorInfo(Gray + HighOffset); // near-black gray on transport; doesn't appear to be shaded

                case 0xff: return new ColorInfo(DeathStar + LowOffset); // used in a a DS tower?

                default: return new ColorInfo(null);
            }
        }
    }

    public class TieFighterPaletteMapper : PaletteMapper
    {
        public TieFighterPaletteMapper(List<Color> palette, params Color[] customFlightGroupColors) : base(palette, customFlightGroupColors) { }

        private const int
            ImperialBlue = 0,  // 0-19: imperial blue (20)
            ImperialGray = 20, // 20-39: imperial gray (20)
            RebelBeige = 40,   // 40-59: rebel beige (20)
            Gray = 60,         // 60-79: gray (20)
            Yellow = 80,       // 80-91: yellow (12)
            Red = 92,          // 92-103: red (12)
            Blue = 104,        // 104-115: blue (12)
            Cockpit = 116,     // 116-121 cockpit (6)
            Orange = 130;      // Orange in-game, blue in tech room (probably maps to an in-memory location outside of VGA.PAC)

        private const int
            RegularOffset = 9, // Pick something in a standard 12-entry palette section
            SmallOffset = 3, // Pick something in a small 6-entry palette section
            LowOffset = 7, // First 12 entries in a large 20-entry palette section
            MidOffset = 11, // Middle 12 entries in a large 20-entry palette section
            HighOffset = 15; // Last 12 entries in a large 20-entry palette section

        protected override int CockpitStartIndex => Cockpit;
        protected override int CockpitPaletteLength => 6;

        public override ColorInfo GetColorInfo(long colorId, int? flightGroupColor)
        {
            switch (colorId)
            {
                case 0x1: return new ColorInfo(ImperialBlue + LowOffset);
                case 0x2: return new ColorInfo(ImperialGray + LowOffset);
                case 0x3: return new ColorInfo(RebelBeige + LowOffset);
                case 0x4: return new ColorInfo(Gray + LowOffset);
                case 0x5: return new ColorInfo(ImperialBlue + MidOffset);
                case 0x6: return new ColorInfo(ImperialGray + MidOffset);
                case 0x7: return new ColorInfo(RebelBeige + MidOffset);
                case 0x8: return new ColorInfo(Gray + MidOffset);
                case 0x9: return new ColorInfo(ImperialBlue + HighOffset);
                case 0xa: return new ColorInfo(ImperialGray + HighOffset);
                case 0xb: return new ColorInfo(RebelBeige + HighOffset);
                case 0xc: return new ColorInfo(Gray + HighOffset);

                case 0xd: return new ColorInfo(Yellow + RegularOffset);

                case 0xe: // used for flight group color; all 12
                    if (flightGroupColor.HasValue)
                    {
                        if (flightGroupColor.Value == 1)
                            return new ColorInfo(Blue + RegularOffset);

                        if (flightGroupColor.Value == 2)
                            return new ColorInfo(Yellow + RegularOffset);

                        var customColor = flightGroupColor.Value - 3;
                        if (customColor >= 0 && customColor < CustomFlightGroupColorCount)
                            return new ColorInfo(CustomFlightGroupColorStartIndex + (customColor * 3 + 1));
                    }

                    return new ColorInfo(Red + RegularOffset);

                case 0xf: return new ColorInfo(Blue + RegularOffset);

                case 0x11: return new ColorInfo(Cockpit + SmallOffset);

                case 0x13: return new ColorInfo(Gray + MidOffset);
                case 0x14: return new ColorInfo(Gray + HighOffset - 1);
                case 0x15: return new ColorInfo(GlowGreenIndex); // flashing green
                case 0x16: return new ColorInfo(GlowRedIndex); // flashing red (red engines)
                case 0x17: return new ColorInfo(GlowBlueIndex); // flashing blue (blue engines)

                case 0x1b: return new ColorInfo(Gray + HighOffset); // darkest 5 from gray in the game

                // More research especially needed for the colors that follow. Initial stab a values are very rough guesses and have not had much validation.
                // Note that TIE Fighter has a wider palette to play around with, so we probably need more offset ranges.

                case 0x10: return new ColorInfo(Cockpit + SmallOffset);
                case 0x1a: return new ColorInfo(Red + RegularOffset);
                case 0x1c: return new ColorInfo(Blue + RegularOffset);
                case 0x1d: return new ColorInfo(ImperialGray + LowOffset);
                case 0x1e: return new ColorInfo(RebelBeige + MidOffset);
                case 0x1f: return new ColorInfo(Gray + LowOffset + 1);

                case 0x20: return new ColorInfo(ImperialBlue + MidOffset);
                case 0x21: return new ColorInfo(ImperialGray + MidOffset);
                case 0x22: return new ColorInfo(RebelBeige + LowOffset);
                case 0x23: return new ColorInfo(Gray + LowOffset + 2);
                case 0x24: return new ColorInfo(ImperialBlue + MidOffset - 1);
                case 0x25: return new ColorInfo(ImperialGray + MidOffset - 1);
                case 0x26: return new ColorInfo(RebelBeige + MidOffset - 2);
                case 0x27: return new ColorInfo(Gray + MidOffset - 2);

                case 0x81: return new ColorInfo(ImperialBlue + LowOffset);
                case 0x82: return new ColorInfo(ImperialGray + MidOffset);
                case 0x83: return new ColorInfo(RebelBeige + MidOffset);
                case 0x84: return new ColorInfo(Gray + MidOffset);

                case 0x86: return new ColorInfo(Gray + LowOffset);
                case 0x85: return new ColorInfo(ImperialBlue + MidOffset);
                case 0x88: return new ColorInfo(Gray + HighOffset - 2);
                case 0x89: return new ColorInfo(ImperialBlue + MidOffset);

                case 0x8a: return new ColorInfo(ImperialGray + MidOffset);
                case 0x8b: return new ColorInfo(RebelBeige + MidOffset);

                case 0x8c: return new ColorInfo(Gray + HighOffset - 2);
                case 0x8d: return new ColorInfo(Yellow + RegularOffset);
                case 0x8f: return new ColorInfo(Blue + RegularOffset);

                case 0x90: return new ColorInfo(Gray + HighOffset);

                case 0x96: return new ColorInfo(GlowRedIndex); // flashing red (red engines)
                case 0x97: return new ColorInfo(GlowBlueIndex); // flashing blue (blue engines)

                case 0x9b: return new ColorInfo(Gray + HighOffset); // near-black gray on transport; doesn't appear to be shaded

                case 0x9c: return new ColorInfo(Orange); // blue in tech room, orange in game

                default: return new ColorInfo(null);
            }
        }
    }

    public struct ColorInfo
    {
        // TODO: expand to include more information about the palette range.
        public ColorInfo(int? index, Color? overrideColor = null)
        {
            Index = index;
            OverrideColor = overrideColor;
        }

        public readonly int? Index;
        public readonly Color? OverrideColor;
    }
}
