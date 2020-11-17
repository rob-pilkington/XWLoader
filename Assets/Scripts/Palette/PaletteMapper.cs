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
        ColorInfo GetColorInfo(byte colorId, int? flightGroupColor);
        Vector2 GetUv(ColorInfo colorInfo);
        Texture GeneratePaletteTexture();
        Texture GenerateSpecularMap();
        Texture GenerateEmissionMap();
    }

    public abstract class PaletteMapper : IPaletteMapper
    {
        public PaletteMapper(List<Color> palette, params Color[] customFlightGroupColors)
        {
            _palette = palette
                .Concat(BaseGlowColors)
                .Concat(customFlightGroupColors)
                // Padding fixed at 256 for now.
                .Concat(Enumerable.Repeat(Color.black, PaletteWidth - palette.Count - BaseGlowColors.Length - customFlightGroupColors.Length))
                // second row to match height of 2
                .Concat(Enumerable.Repeat(Color.black, PaletteWidth))
                .ToArray();

            GlowStartXOffset = palette.Count;
            GlowRedIndex = GlowStartXOffset;
            GlowGreenIndex = GlowStartXOffset + 1;
            GlowBlueIndex = GlowStartXOffset + 2;

            CustomFlightGroupColorStartXOffset = palette.Count + BaseGlowColors.Length;
            CustomFlightGroupColorCount = customFlightGroupColors.Length;
        }

        private static readonly Color[] GlowColors = new Color[]
        {
            new Color(1f, 1f, 1f),
            Color.green,
            new Color(1f, 1f, 1f)
        };

        private static readonly Color[] BaseGlowColors = new Color[]
        {
            new Color(1.0f, 0f, 0f),
            new Color(0f, 0.0f, 0f),
            new Color(0f, 0f, 1.0f)
        };

        protected Color[] _palette;

        protected abstract int CockpitStartXOffset { get; }
        protected abstract int CockpitPaletteLength { get; }

        protected int GlowStartXOffset { get; private set; }
        protected int GlowRedIndex { get; private set; }
        protected int GlowGreenIndex { get; private set; }
        protected int GlowBlueIndex { get; private set; }

        protected int CustomFlightGroupColorStartXOffset { get; private set; }
        protected int CustomFlightGroupColorCount { get; private set; }

        public int PaletteSize => _palette.Length;

        // Constant for now. If we need more custom colors we can increase these and change calculations later.
        // Will still want powers of 2.
        private const int PaletteWidth = 256;
        private const int PaletteHeight = 2;

        private static Color ColorFromBytes(byte[] bytes) => new Color(Convert.ToSingle(bytes[0]) / 63, Convert.ToSingle(bytes[1]) / 63, Convert.ToSingle(bytes[2]) / 63);

        public abstract ColorInfo GetColorInfo(byte colorId, int? flightGroupColor);

        public Vector2 GetUv(ColorInfo colorInfo)
        {
            var offset = colorInfo.Index.HasValue
                ? (colorInfo.Index.Value + 0.5f) / PaletteWidth
                : 0f;

            return new Vector2(offset, 0.5f / PaletteHeight);
        }

        protected bool IsFlatShaded(byte colorId) => (colorId & 0x80) != 0;

        protected byte GetBaseColorId(byte colorId) => (byte)(colorId & 0x7f);

        public Texture GeneratePaletteTexture()
        {
            var texture = new Texture2D(PaletteWidth, PaletteHeight);

            texture.SetPixels(_palette);

            texture.Apply();

            return texture;
        }

        public Texture GenerateSpecularMap()
        {
            var texture = new Texture2D(PaletteWidth, PaletteHeight);

            texture.SetPixels32(Enumerable.Repeat(new Color32(0, 0, 0, 0), PaletteSize).ToArray());
            texture.SetPixels32(CockpitStartXOffset, 0, CockpitPaletteLength, 1, Enumerable.Repeat(new Color32(0x8f, 0x8f, 0x8f, 0xff), CockpitPaletteLength).ToArray());

            texture.Apply();

            return texture;
        }

        public Texture GenerateEmissionMap()
        {
            var texture = new Texture2D(PaletteWidth, PaletteHeight);

            texture.SetPixels(Enumerable.Repeat(new Color(0, 0, 0), PaletteSize).ToArray());
            texture.SetPixels(GlowStartXOffset, 0, GlowColors.Length, 1, GlowColors);

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

        protected override int CockpitStartXOffset => Cockpit;
        protected override int CockpitPaletteLength => 12;

        public override ColorInfo GetColorInfo(byte colorId, int? flightGroupColor)
        {
            var index = GetIndex();

            return new ColorInfo(index, IsFlatShaded(colorId));

            int? GetIndex()
            {
                switch (GetBaseColorId(colorId))
                {
                    case 0x1: return ImperialBlue + LowOffset; // first 12
                    case 0x2: return ImperialGray + LowOffset; // first 12
                    case 0x3: return RebelBeige + LowOffset; // first 12
                    case 0x4: return Gray + LowOffset; // first 12
                    case 0x5: return ImperialBlue + MidOffset; // last 12
                    case 0x6: return ImperialGray + MidOffset; // last 12
                    case 0x7: return RebelBeige + MidOffset; // last 12
                    case 0x8: return Gray + MidOffset; // last 12
                    case 0x9: return ImperialBlue + HighOffset; // last 8
                    case 0xa: return ImperialGray + HighOffset; // last 8
                    case 0xb: return RebelBeige + HighOffset; // last 8
                    case 0xc: return Gray + HighOffset; // last 8

                    case 0xd: return Yellow + RegularOffset; // all 12

                    case 0xe: // used for flight group color; all 12
                        if (flightGroupColor.HasValue)
                        {
                            if (flightGroupColor.Value == 1)
                                return Blue + RegularOffset;

                            if (flightGroupColor.Value == 2)
                                return Yellow + RegularOffset;

                            var customColor = flightGroupColor.Value - 3;

                            if (customColor >= 0 && customColor < CustomFlightGroupColorCount)
                                return CustomFlightGroupColorStartXOffset + customColor;
                        }

                        return Red + RegularOffset;

                    case 0xf: return Blue + RegularOffset; // all 12

                    case 0x10: return Cockpit + 6; // all 12 colors
                    case 0x11: return Cockpit + 7; // last 8 colors

                    case 0x12: return DeathStar + LowOffset; // all 16 colors
                    case 0x13: return DeathStar + MidOffset; // last 12
                    case 0x14: return DeathStar + HighOffset; // last 8

                    case 0x15: return GlowGreenIndex; // flashing green
                    case 0x16: return GlowRedIndex; // flashing red (red engines)
                    case 0x17: return GlowBlueIndex; // flashing blue (blue engines)

                    case 0x18: return Red + LowOffset; // first 8
                    case 0x19: return Red + RegularOffset; // middle 6
                    case 0x1a: return Red + MidOffset; // last 6

                    case 0x1b: return Gray + HighOffset; // darkest 4 from gray in the game

                    // more research needed for these colors:
                    case 0x1d: return ImperialGray + LowOffset;
                    case 0x1e: return RebelBeige + MidOffset;
                    case 0x1f: return Gray + LowOffset + 1;

                    case 0x20: return ImperialBlue + MidOffset;
                    case 0x21: return ImperialGray + MidOffset;
                    case 0x22: return RebelBeige + LowOffset;
                    case 0x23: return Gray + LowOffset + 2;
                    case 0x24: return ImperialBlue + MidOffset - 1;
                    case 0x25: return ImperialGray + MidOffset - 1;
                    case 0x26: return RebelBeige + MidOffset - 2;
                    case 0x27: return Gray + MidOffset - 2;
                    case 0x28: return Gray + LowOffset - 1;
                    case 0x48: return Gray + MidOffset - 1;
                    case 0x68: return Gray + HighOffset - 1;

                    case 0x7f: return DeathStar + LowOffset; // used in a DS tower at a lower LOD (as 0xff)

                    default:
                        Debug.LogWarning($"Unknown color: {colorId} ({colorId:X})");
                        return null;
                }
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

        protected override int CockpitStartXOffset => Cockpit;
        protected override int CockpitPaletteLength => 6;

        public override ColorInfo GetColorInfo(byte colorId, int? flightGroupColor)
        {
            var index = GetIndex();

            return new ColorInfo(index, IsFlatShaded(colorId));

            int? GetIndex()
            {
                // Note: 0x9c maps to an orange (we could use 130 for this) in flight, but it appears that blue
                // (which it appears to be in the tech/film rooms) was the original intent.
                // Colors above 0x80 are flat-shaded colors matching the lower colors: 0x09c ^ 0x80 = 0x1c = blue
                switch (GetBaseColorId(colorId))
                {
                    case 0x1: return ImperialBlue + LowOffset;
                    case 0x2: return ImperialGray + LowOffset;
                    case 0x3: return RebelBeige + LowOffset;
                    case 0x4: return Gray + LowOffset;
                    case 0x5: return ImperialBlue + MidOffset;
                    case 0x6: return ImperialGray + MidOffset;
                    case 0x7: return RebelBeige + MidOffset;
                    case 0x8: return Gray + MidOffset;
                    case 0x9: return ImperialBlue + HighOffset;
                    case 0xa: return ImperialGray + HighOffset;
                    case 0xb: return RebelBeige + HighOffset;
                    case 0xc: return Gray + HighOffset;

                    case 0xd: return Yellow + RegularOffset;

                    case 0xe: // used for flight group color; all 12
                        if (flightGroupColor.HasValue)
                        {
                            if (flightGroupColor.Value == 1)
                                return Blue + RegularOffset;

                            if (flightGroupColor.Value == 2)
                                return Yellow + RegularOffset;

                            var customColor = flightGroupColor.Value - 3;
                            if (customColor >= 0 && customColor < CustomFlightGroupColorCount)
                                return CustomFlightGroupColorStartXOffset + customColor;
                        }

                        return Red + RegularOffset;

                    case 0xf: return Blue + RegularOffset;

                    case 0x11: return Cockpit + SmallOffset;

                    case 0x13: return Gray + MidOffset;
                    case 0x14: return Gray + HighOffset - 1;
                    case 0x15: return GlowGreenIndex; // flashing green
                    case 0x16: return GlowRedIndex; // flashing red (red engines)
                    case 0x17: return GlowBlueIndex; // flashing blue (blue engines)

                    case 0x1b: return Gray + HighOffset; // darkest 5 from gray in the game

                    // More research especially needed for the colors that follow. Initial stab a values are very rough guesses and have not had much validation.
                    // Note that TIE Fighter has a wider palette to play around with, so we probably need more offset ranges.

                    case 0x10: return Cockpit + SmallOffset;
                    case 0x1a: return Red + RegularOffset;
                    case 0x1c: return Blue + RegularOffset;
                    case 0x1d: return ImperialGray + LowOffset;
                    case 0x1e: return RebelBeige + MidOffset;
                    case 0x1f: return Gray + LowOffset + 1;

                    case 0x20: return ImperialBlue + MidOffset;
                    case 0x21: return ImperialGray + MidOffset;
                    case 0x22: return RebelBeige + LowOffset;
                    case 0x23: return Gray + LowOffset + 2;
                    case 0x24: return ImperialBlue + MidOffset - 1;
                    case 0x25: return ImperialGray + MidOffset - 1;
                    case 0x26: return RebelBeige + MidOffset - 2;
                    case 0x27: return Gray + MidOffset - 2;

                    default:
                        Debug.LogWarning($"Unknown color: {colorId} ({colorId:X})");
                        return null;
                }
            }
        }
    }

    public struct ColorInfo
    {
        // TODO: expand to include more information about the palette range.
        public ColorInfo(int? index, bool isFlatShaded = false)
        {
            Index = index;
            IsFlatShaded = isFlatShaded;
        }

        public readonly int? Index;
        public readonly bool IsFlatShaded;
    }
}
