using System;
using System.IO;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public class AnchorHelper
    {
        public readonly string FileName;

        public PosRotAnchor HouseEntryAnchor { get; } = new();
        public PosRotAnchor DropPositionAnchor { get; } = new();
        public PosRotAnchor AirportEntryAnchor { get;} = new();
        public PosRotAnchor OrvilleTalkAnchor { get; } = new();
        public PosRotAnchor AirportExitAnchor { get; } = new();

        public PosRotAnchor[] Anchors => GatherAnchors();

        public AnchorHelper():this("Anchor.bin") { }

        public AnchorHelper(string filename)
        {
            FileName = filename;
            Initialise();
        }

        public void Save()
        {
            var data = GetBytes();
            File.WriteAllBytes(FileName, data);
        }

        public bool IsOneEmpty(out int empty)
        {
            var anchors = GatherAnchors();
            for (int i = 0; i < anchors.Length; ++i)
            {
                if (anchors[i].IsEmpty())
                {
                    empty = i;
                    return true;
                }
            }
            empty = -1;
            return false;
        }

        private void Initialise()
        {
            if (!File.Exists(FileName))
                File.WriteAllBytes(FileName, GetBytes());

            var data = File.ReadAllBytes(FileName);
            LoadAllAnchors(data);
        }

        private byte[] GetBytes()
        {
            var anchors = GatherAnchors();
            byte[] toRet = new byte[anchors.Length * PosRotAnchor.SIZE];

            for (int i = 0; i < anchors.Length; ++i)
                Array.Copy(anchors[i].AnchorBytes, 0, toRet, i * PosRotAnchor.SIZE, PosRotAnchor.SIZE);

            return toRet;
        }

        private void LoadAllAnchors(byte[] toLoad)
        {
            var anchors = GatherAnchors();
            if (toLoad.Length != (anchors.Length * PosRotAnchor.SIZE))
                throw new Exception("Attempting to load anchors of the incorrect size. Please re-create anchors");

            for (int i = 0; i < anchors.Length; ++i)
                anchors[i].AssignableBytes = toLoad.Skip(i * PosRotAnchor.SIZE).Take(PosRotAnchor.SIZE).ToArray();
        }

        private PosRotAnchor[] GatherAnchors()
        {
            return new PosRotAnchor[5]
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
