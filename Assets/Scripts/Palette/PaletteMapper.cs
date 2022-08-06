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

        private const int BaseFlightGroupColorCount = 3;
        protected abstract int FlightGroupColorRedIndex { get; }
        protected abstract int FlightGroupColorBlueIndex { get; }
        protected abstract int FlightGroupColorYellowIndex { get; }

        protected int CustomFlightGroupColorStartXOffset { get; private set; }
        protected int CustomFlightGroupColorCount { get; private set; }

        public int PaletteSize => _palette.Length;

        // Constant for now. If we need more custom colors we can increase these and change calculations later.
        // Will still want powers of 2.
        private const int PaletteWidth = 256;
        private const int PaletteHeight = 2;

        private static Color ColorFromBytes(byte[] bytes) => new Color(Convert.ToSingle(bytes[0]) / 63, Convert.ToSingle(bytes[1]) / 63, Convert.ToSingle(bytes[2]) / 63);

        public ColorInfo GetColorInfo(byte colorId, int? flightGroupColor)
        {
            var index = GetBaseColorIndex(GetBaseColorId(colorId), flightGroupColor);

            if (index is null)
                Debug.LogWarning($"Unknown color: {colorId} ({colorId:X})");

            return new ColorInfo(index, IsFlatShaded(colorId));
        }

        public Vector2 GetUv(ColorInfo colorInfo)
        {
            var offset = colorInfo.Index.HasValue
                ? (colorInfo.Index.Value + 0.5f) / PaletteWidth
                : 0f;

            return new Vector2(offset, 0.5f / PaletteHeight);
        }

        protected bool IsFlatShaded(byte colorId) => (colorId & 0x80) != 0;

        protected byte GetBaseColorId(byte colorId) => (byte)(colorId & 0x7f);

        protected abstract int? GetBaseColorIndex(byte baseColorId, int? flightGroupColor);

        protected int GetFlightGroupColorIndex(int flightGroupColor) => flightGroupColor switch
        {
            0 => FlightGroupColorRedIndex,
            1 => FlightGroupColorBlueIndex,
            2 => FlightGroupColorYellowIndex,
            _ => GetCustomColorIndex(flightGroupColor, FlightGroupColorRedIndex)
        };

        private int GetCustomColorIndex(int flightGroupColor, int defaultIndex)
        {
            var customColor = flightGroupColor - BaseFlightGroupColorCount;

            if (customColor >= 0 && customColor < CustomFlightGroupColorCount)
                return CustomFlightGroupColorStartXOffset + customColor;

            return defaultIndex;
        }

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
            using var fs = File.OpenRead(filename);
            return LoadPalette(fs);
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

        protected override int FlightGroupColorRedIndex => Red + RegularOffset;
        protected override int FlightGroupColorBlueIndex => Blue + RegularOffset;
        protected override int FlightGroupColorYellowIndex => Yellow + RegularOffset;

        protected override int? GetBaseColorIndex(byte baseColorId, int? flightGroupColor) => baseColorId switch
        {
            0x1 => ImperialBlue + LowOffset, // first 12
            0x2 => ImperialGray + LowOffset, // first 12
            0x3 => RebelBeige + LowOffset, // first 12
            0x4 => Gray + LowOffset, // first 12
            0x5 => ImperialBlue + MidOffset, // last 12
            0x6 => ImperialGray + MidOffset, // last 12
            0x7 => RebelBeige + MidOffset, // last 12
            0x8 => Gray + MidOffset, // last 12
            0x9 => ImperialBlue + HighOffset, // last 8
            0xa => ImperialGray + HighOffset, // last 8
            0xb => RebelBeige + HighOffset, // last 8
            0xc => Gray + HighOffset, // last 8

            0xd => Yellow + RegularOffset, // all 12
            0xe => GetFlightGroupColorIndex(flightGroupColor ?? 0), // used for flight group color; all 12
            0xf => Blue + RegularOffset, // all 12

            0x10 => Cockpit + 6, // all 12 colors
            0x11 => Cockpit + 7, // last 8 colors

            0x12 => DeathStar + LowOffset, // all 16 colors
            0x13 => DeathStar + MidOffset, // last 12
            0x14 => DeathStar + HighOffset, // last 8

            0x15 => GlowGreenIndex, // flashing green
            0x16 => GlowRedIndex, // flashing red (red engines)
            0x17 => GlowBlueIndex, // flashing blue (blue engines)

            0x18 => Red + LowOffset, // first 8
            0x19 => Red + RegularOffset, // middle 6
            0x1a => Red + MidOffset, // last 6

            0x1b => Gray + HighOffset, // darkest 4 from gray in the game

            // more research needed for these colors:
            0x1d => ImperialGray + LowOffset + 1,
            0x1e => RebelBeige + LowOffset + 1,
            0x1f => Gray + LowOffset + 1,

            0x20 => ImperialBlue + LowOffset + 2,
            0x21 => ImperialGray + LowOffset + 2,
            0x22 => RebelBeige + LowOffset + 2,
            0x23 => Gray + LowOffset + 2,
            0x24 => ImperialBlue + MidOffset - 1,
            0x25 => ImperialGray + MidOffset - 1,
            0x26 => RebelBeige + MidOffset - 2,
            0x27 => Gray + MidOffset - 2,
            0x28 => Gray + LowOffset - 1,
            0x48 => Gray + MidOffset - 1,
            0x68 => Gray + HighOffset - 1,

            0x7f => DeathStar + LowOffset, // used in a DS tower at a lower LOD (as 0xff)

            _ => null
        };
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

        protected override int FlightGroupColorRedIndex => Red + RegularOffset;
        protected override int FlightGroupColorBlueIndex => Blue + RegularOffset;
        protected override int FlightGroupColorYellowIndex => Yellow + RegularOffset;

        protected override int? GetBaseColorIndex(byte baseColorId, int? flightGroupColor) => baseColorId switch
        {
            0x1 => ImperialBlue + LowOffset,
            0x2 => ImperialGray + LowOffset,
            0x3 => RebelBeige + LowOffset,
            0x4 => Gray + LowOffset,
            0x5 => ImperialBlue + MidOffset,
            0x6 => ImperialGray + MidOffset,
            0x7 => RebelBeige + MidOffset,
            0x8 => Gray + MidOffset,
            0x9 => ImperialBlue + HighOffset,
            0xa => ImperialGray + HighOffset,
            0xb => RebelBeige + HighOffset,
            0xc => Gray + HighOffset,

            0xd => Yellow + RegularOffset,
            0xe => GetFlightGroupColorIndex(flightGroupColor ?? 0), // used for flight group color; all 12
            0xf => Blue + RegularOffset,

            0x11 => Cockpit + SmallOffset,

            0x13 => Gray + MidOffset,
            0x14 => Gray + HighOffset - 1,
            0x15 => GlowGreenIndex, // flashing green
            0x16 => GlowRedIndex, // flashing red (red engines)
            0x17 => GlowBlueIndex, // flashing blue (blue engines)

            0x1b => Gray + HighOffset, // darkest 5 from gray in the game

            // More research especially needed for the colors that follow. Initial stab a values are very rough guesses and have not had much validation.
            // Note that TIE Fighter has a wider palette to play around with, so we probably need more offset ranges.

            0x10 => Cockpit + SmallOffset,
            0x1a => Red + RegularOffset,

            // Note: 0x9c maps to an orange (we could use 130 for this) in flight, but it appears that blue
            // (which it appears to be in the tech/film rooms) was the original intent.
            // Colors above 0x80 are flat-shaded colors matching the lower colors: 0x09c ^ 0x80 = 0x1c = blue
            0x1c => ImperialBlue + LowOffset + 1,
            0x1d => ImperialGray + LowOffset + 1,
            0x1e => RebelBeige + LowOffset + 1,
            0x1f => Gray + LowOffset + 1,

            0x20 => ImperialBlue + LowOffset + 2,
            0x21 => ImperialGray + LowOffset + 2,
            0x22 => RebelBeige + LowOffset + 2,
            0x23 => Gray + LowOffset + 2,
            0x24 => ImperialBlue + MidOffset - 1,
            0x25 => ImperialGray + MidOffset - 1,
            0x26 => RebelBeige + MidOffset - 2,
            0x27 => Gray + MidOffset - 2,

            _ => null
        };
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
