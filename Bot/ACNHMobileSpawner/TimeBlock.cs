using System.Runtime.InteropServices;

namespace ACNHMobileSpawner
{
    [StructLayout(LayoutKind.Sequential, Size = SIZE)]
    public class TimeBlock
    {
        public const int SIZE = 0x6;

        public ushort Year { get; set; }
        public byte Month { get; set; }
        public byte Day { get; set; }
        public byte Hour { get; set; }
        public byte Minute { get; set; }

        public override string ToString() => $"{Hour:00}:{Minute:00} ({Year}-{Month:00}-{Day:00})";
    }
}