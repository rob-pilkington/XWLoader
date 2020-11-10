using System;
using System.IO;
using UnityEngine;

namespace Assets.Scripts.Palette
{
    public interface IPaletteMapper
    {
        Color GetColor(long colorId, Color? flightGroupColor);
    }

    public abstract class PaletteMapper : IPaletteMapper
    {
        protected byte[][] _palette;

        public PaletteMapper(byte[][] palette) => _palette = palette;

        public static Color ColorFromBytes(byte[] bytes) => new Color(Convert.ToSingle(bytes[0]) / 63, Convert.ToSingle(bytes[1]) / 63, Convert.ToSingle(bytes[2]) / 63);

        public abstract Color GetColor(long colorId, Color? flightGroupColor);

        public static byte[][] LoadPalette(string filename)
        {
            using (var fs = File.OpenRead(filename))
            {
                return LoadPalette(fs);
            }
        }

        public static byte[][] LoadPalette(Stream fs)
        {
            var entryCount = fs.Length / 3;
            var palette = new byte[entryCount][];

            for (var i = 0; i < entryCount; i++)
            {
                var paletteEntry = new byte[3];
                fs.Read(paletteEntry, 0, 3);
                palette[i] = paletteEntry;
            }

            return palette;
        }
    }

    public class XWingPaletteMapper : PaletteMapper
    {
        public XWingPaletteMapper(byte[][] palette) : base(palette) { }

        public override Color GetColor(long colorId, Color? flightGroupColor)
        {
            // Color ranges:
            const int
                ImperialBlue = 0,  // 0-15: imperial blue (16)
                ImperialGray = 16, // 16-31: imperial gray (16)
                RebelBeige = 32,   // 32-47: rebel beige (16)
                Gray = 48,         // 48-63: gray (16)
                Yellow = 64,       // 64-75: yellow (12)
                Red = 76,          // 76-87: red (12)
                Blue = 88,         // 88-99: blue (12)
                Cockpit = 100,     // 100-111: cockpit (12)
                DeathStar = 112;   // 112-127: Death Star (16)

            const int
                RegularOffset = 9, // Pick something in a standard 12-entry palette section
                LowOffset = 7, // First 12 entries in a large 16-entry palette section (brighter)
                MidOffset = 11, // Last 12 entries in a large 16-entry palette section
                HighOffset = 15; // Last 8 entries in a large 16-entry palette section (darker)

            switch (colorId)
            {
                case 0x1: return ColorFromBytes(_palette[ImperialBlue + LowOffset]); // first 12
                case 0x2: return ColorFromBytes(_palette[ImperialGray + LowOffset]); // first 12
                case 0x3: return ColorFromBytes(_palette[RebelBeige + LowOffset]); // first 12
                case 0x4: return ColorFromBytes(_palette[Gray + LowOffset]); // first 12
                case 0x5: return ColorFromBytes(_palette[ImperialBlue + MidOffset]); // last 12
                case 0x6: return ColorFromBytes(_palette[ImperialGray + MidOffset]); // last 12
                case 0x7: return ColorFromBytes(_palette[RebelBeige + MidOffset]); // last 12
                case 0x8: return ColorFromBytes(_palette[Gray + MidOffset]); // last 12
                case 0x9: return ColorFromBytes(_palette[ImperialBlue + HighOffset]); // last 8
                case 0xa: return ColorFromBytes(_palette[ImperialGray + HighOffset]); // last 8
                case 0xb: return ColorFromBytes(_palette[RebelBeige + HighOffset]); // last 8
                case 0xc: return ColorFromBytes(_palette[Gray + HighOffset]); // last 8

                case 0xd: return ColorFromBytes(_palette[Yellow + RegularOffset]); // all 12
                case 0xe: return flightGroupColor ?? ColorFromBytes(_palette[Red + RegularOffset]); // used for flight group color; all 12
                case 0xf: return ColorFromBytes(_palette[Blue + RegularOffset]); // all 12

                // TODO: cockpit really needs some additional specular/smoothness mapping
                case 0x10: return ColorFromBytes(_palette[Cockpit + RegularOffset]); // all 12 colors
                case 0x11: return ColorFromBytes(_palette[Cockpit + RegularOffset + 1]); // last 8 colors

                case 0x12: return ColorFromBytes(_palette[DeathStar + LowOffset]); // all 16 colors
                case 0x13: return ColorFromBytes(_palette[DeathStar + MidOffset]); // last 12
                case 0x14: return ColorFromBytes(_palette[DeathStar + HighOffset]); // last 8

                case 0x15: return Color.green; // flashing green
                case 0x16: return Color.red; // flashing red (red engines)
                case 0x17: return Color.blue; // flashing blue (blue engines)

                case 0x18: return ColorFromBytes(_palette[Red + LowOffset]); // first 8
                case 0x19: return ColorFromBytes(_palette[Red + RegularOffset]); // middle 6
                case 0x1a: return ColorFromBytes(_palette[Red + MidOffset]); // last 6

                case 0x1b: return ColorFromBytes(_palette[Gray + HighOffset]); // darkest 4 from gray in the game

                // more research needed for these colors:
                case 0x1d: return ColorFromBytes(_palette[ImperialGray + LowOffset]);
                case 0x1e: return ColorFromBytes(_palette[RebelBeige + MidOffset]);
                case 0x1f: return ColorFromBytes(_palette[Gray + LowOffset + 1]);

                case 0x20: return ColorFromBytes(_palette[ImperialBlue + MidOffset]);
                case 0x21: return ColorFromBytes(_palette[ImperialGray + MidOffset]);
                case 0x22: return ColorFromBytes(_palette[RebelBeige + LowOffset]);
                case 0x23: return ColorFromBytes(_palette[Gray + LowOffset + 2]);
                case 0x24: return ColorFromBytes(_palette[ImperialBlue + MidOffset - 1]);
                case 0x25: return ColorFromBytes(_palette[ImperialGray + MidOffset - 1]);
                case 0x26: return ColorFromBytes(_palette[RebelBeige + MidOffset - 2]);
                case 0x27: return ColorFromBytes(_palette[Gray + MidOffset - 2]);
                case 0x28: return ColorFromBytes(_palette[Gray + LowOffset - 1]);
                case 0x48: return ColorFromBytes(_palette[Gray + MidOffset - 1]);
                case 0x68: return ColorFromBytes(_palette[Gray + HighOffset - 1]);

                case 0x82: return ColorFromBytes(_palette[ImperialGray + MidOffset]);
                case 0x83: return ColorFromBytes(_palette[RebelBeige + MidOffset]);
                case 0x84: return ColorFromBytes(_palette[Gray + MidOffset]);

                case 0x8c: return ColorFromBytes(_palette[Gray + HighOffset - 2]);
                case 0x8d: return ColorFromBytes(_palette[Yellow + RegularOffset]);
                case 0x8f: return ColorFromBytes(_palette[Blue + RegularOffset]);

                // I have a feeling these colors are "no shading" colors
                case 0x96: return Color.red; // flashing red (red engines)
                case 0x97: return Color.blue; // flashing blue (blue engines)

                case 0x9b: return ColorFromBytes(_palette[Gray + HighOffset]); // near-black gray on transport; doesn't appear to be shaded

                case 0xff: return ColorFromBytes(_palette[DeathStar + LowOffset]); // used in a a DS tower?

                default:
                    Debug.LogWarning($"Unknown color: {colorId} ({colorId.ToString("X")})");
                    return Color.magenta;
            }
        }
    }

    public class TieFighterPaletteMapper : PaletteMapper
    {
        public TieFighterPaletteMapper(byte[][] palette) : base(palette) { }

        public override Color GetColor(long colorId, Color? flightGroupColor)
        {
            // Color ranges:
            const int
                ImperialBlue = 0,  // 0-19: imperial blue (20)
                ImperialGray = 20, // 20-39: imperial gray (20)
                RebelBeige = 40,   // 40-59: rebel beige (20)
                Gray = 60,         // 60-79: gray (20)
                Yellow = 80,       // 80-91: yellow (12)
                Red = 92,          // 92-103: red (12)
                Blue = 104,        // 104-115: blue (12)
                Cockpit = 116,     // 116-121 cockpit (6)
                Orange = 129;      // Orange in-game, blue in tech room (probably maps to an in-memory location outside of VGA.PAC)

            const int
                RegularOffset = 9, // Pick something in a standard 12-entry palette section
                SmallOffset = 3, // Pick something in a small 6-entry palette section
                LowOffset = 7, // First 12 entries in a large 20-entry palette section
                MidOffset = 11, // Middle 12 entries in a large 20-entry palette section
                HighOffset = 15; // Last 12 entries in a large 20-entry palette section

            switch (colorId)
            {
                case 0x1: return ColorFromBytes(_palette[ImperialBlue + LowOffset]);
                case 0x2: return ColorFromBytes(_palette[ImperialGray + LowOffset]);
                case 0x3: return ColorFromBytes(_palette[RebelBeige + LowOffset]);
                case 0x4: return ColorFromBytes(_palette[Gray + LowOffset]);
                case 0x5: return ColorFromBytes(_palette[ImperialBlue + MidOffset]);
                case 0x6: return ColorFromBytes(_palette[ImperialGray + MidOffset]);
                case 0x7: return ColorFromBytes(_palette[RebelBeige + MidOffset]);
                case 0x8: return ColorFromBytes(_palette[Gray + MidOffset]);
                case 0x9: return ColorFromBytes(_palette[ImperialBlue + HighOffset]);
                case 0xa: return ColorFromBytes(_palette[ImperialGray + HighOffset]);
                case 0xb: return ColorFromBytes(_palette[RebelBeige + HighOffset]);
                case 0xc: return ColorFromBytes(_palette[Gray + HighOffset]);

                case 0xd: return ColorFromBytes(_palette[Yellow + RegularOffset]);
                case 0xe: return flightGroupColor ?? ColorFromBytes(_palette[Red + RegularOffset]); // used for flight group color
                case 0xf: return ColorFromBytes(_palette[Blue + RegularOffset]);

                case 0x11: return ColorFromBytes(_palette[Cockpit + SmallOffset]);

                case 0x13: return ColorFromBytes(_palette[Gray + MidOffset]);
                case 0x14: return ColorFromBytes(_palette[Gray + HighOffset - 1]);
                case 0x15: return Color.green; // flashing green
                case 0x16: return Color.red; // flashing red (red engines)
                case 0x17: return Color.blue; // flashing blue (blue engines)

                case 0x1b: return ColorFromBytes(_palette[Gray + HighOffset]); // darkest 5 from gray in the game

                // More research especially needed for the colors that follow. Initial stab a values are very rough guesses and have not had much validation.
                // Note that TIE Fighter has a wider palette to play around with, so we probably need more offset ranges.

                case 0x10: return ColorFromBytes(_palette[Cockpit + SmallOffset]);
                case 0x1a: return ColorFromBytes(_palette[Red + RegularOffset]);
                case 0x1c: return ColorFromBytes(_palette[Blue + RegularOffset]);
                case 0x1d: return ColorFromBytes(_palette[ImperialGray + LowOffset]);
                case 0x1e: return ColorFromBytes(_palette[RebelBeige + MidOffset]);
                case 0x1f: return ColorFromBytes(_palette[Gray + LowOffset + 1]);

                case 0x20: return ColorFromBytes(_palette[ImperialBlue + MidOffset]);
                case 0x21: return ColorFromBytes(_palette[ImperialGray + MidOffset]);
                case 0x22: return ColorFromBytes(_palette[RebelBeige + LowOffset]);
                case 0x23: return ColorFromBytes(_palette[Gray + LowOffset + 2]);
                case 0x24: return ColorFromBytes(_palette[ImperialBlue + MidOffset - 1]);
                case 0x25: return ColorFromBytes(_palette[ImperialGray + MidOffset - 1]);
                case 0x26: return ColorFromBytes(_palette[RebelBeige + MidOffset - 2]);
                case 0x27: return ColorFromBytes(_palette[Gray + MidOffset - 2]);

                case 0x81: return ColorFromBytes(_palette[ImperialBlue + LowOffset]);
                case 0x82: return ColorFromBytes(_palette[ImperialGray + MidOffset]);
                case 0x83: return ColorFromBytes(_palette[RebelBeige + MidOffset]);
                case 0x84: return ColorFromBytes(_palette[Gray + MidOffset]);

                case 0x86: return ColorFromBytes(_palette[Gray + LowOffset]);
                case 0x85: return ColorFromBytes(_palette[ImperialBlue + MidOffset]);
                case 0x88: return ColorFromBytes(_palette[Gray + HighOffset - 2]);
                case 0x89: return ColorFromBytes(_palette[ImperialBlue + MidOffset]);

                case 0x8a: return ColorFromBytes(_palette[ImperialGray + MidOffset]);
                case 0x8b: return ColorFromBytes(_palette[RebelBeige + MidOffset]);

                case 0x8c: return ColorFromBytes(_palette[Gray + HighOffset - 2]);
                case 0x8d: return ColorFromBytes(_palette[Yellow + RegularOffset]);
                case 0x8f: return ColorFromBytes(_palette[Blue + RegularOffset]);

                case 0x90: return ColorFromBytes(_palette[Gray + HighOffset]);

                // I have a feeling these colors are "no shading" colors
                case 0x96: return Color.red; // flashing red (red engines)
                case 0x97: return Color.blue; // flashing blue (blue engines)

                case 0x9b: return ColorFromBytes(_palette[Gray + HighOffset]); // near-black gray on transport; doesn't appear to be shaded

                case 0x9c: return ColorFromBytes(_palette[Orange]); // blue in tech room, orange in game

                default:
                    Debug.LogWarning($"Unknown color: {colorId} ({colorId.ToString("X")})");
                    return Color.magenta;
            }
        }
    }
}
