using System;
using System.Linq;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class VectorQuaternionAnchor
    {
        public const int SIZE = 14;
        public byte[] AnchorBytes { get; set; } = new byte[SIZE];

        public byte[] Anchor1
        { 
            get { return AnchorBytes.Take(10).ToArray(); }
            set { Array.Copy(value, AnchorBytes, 10); }
        }

        public byte[] Anchor2
        {
            get { return AnchorBytes.Skip(10).ToArray(); }
            set { Array.Copy(value, 0, AnchorBytes, 10, 4); }
        }

        public VectorQuaternionAnchor() { }
    }
}
