using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class AnchorHelper
    {
        public const string FileName = "Anchor.bin";

        public VectorQuaternionAnchor HouseEntryAnchor { get; } = new();
        public VectorQuaternionAnchor DropPositionAnchor { get; } = new();
        public VectorQuaternionAnchor AirportEntryAnchor { get;} = new();
        public VectorQuaternionAnchor OrvilleTalkAnchor { get; } = new();
        public VectorQuaternionAnchor AirportExitAnchor { get; } = new();

        public static AnchorHelper Instance { get; } = new();

        public AnchorHelper() { }

        private void Initialise()
        {
            if (!File.Exists(FileName))
                File.WriteAllBytes(FileName, GetBytes());
        }

        private byte[] GetBytes()
        {
            var anchors = GatherAnchors();
            byte[] toRet = new byte[anchors.Length * VectorQuaternionAnchor.SIZE];

            for (int i = 0; i < anchors.Length; ++i)
                Array.Copy(anchors[i].AnchorBytes, 0, toRet, i * VectorQuaternionAnchor.SIZE, VectorQuaternionAnchor.SIZE);

            return toRet;
        }

        private void LoadAllAnchors(byte[] toLoad)
        {
            var anchors = GatherAnchors();
            if (toLoad.Length != (anchors.Length * VectorQuaternionAnchor.SIZE))
                throw new Exception("Attempting to load anchors of the incorrect size. Please re-create anchors");

            for (int i = 0; i < anchors.Length; ++i)
                anchors[i].AnchorBytes = toLoad.Skip(i * VectorQuaternionAnchor.SIZE).Take(VectorQuaternionAnchor.SIZE).ToArray();
        }

        private VectorQuaternionAnchor[] GatherAnchors()
        {
            return new VectorQuaternionAnchor[5]
            {
                HouseEntryAnchor,
                DropPositionAnchor,
                AirportEntryAnchor,
                OrvilleTalkAnchor,
                AirportExitAnchor
            };
        }
    }
}
