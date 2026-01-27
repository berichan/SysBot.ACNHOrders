using NHSE.Core;

namespace ACNHMobileSpawner
{
    public static class OffsetHelper
    {
        // some helpers
        public const ulong PlayerSize = 0x131F70;
        public const ulong PlayerOtherStartPadding = 0x37BE0;

        // player other 
        public const ulong InventoryOffset = 0xB27BB758; // player 0 (A) 
        private const ulong playerOtherStart = InventoryOffset - 0x10; // helps to get other values, unused 

        public const ulong WalletAddress = InventoryOffset + 0xB8;
        public const ulong MilesAddress = InventoryOffset - 0x25590;
        public const ulong BankAddress = InventoryOffset + 0x2D5D4;

        // main player offsets functions
        private static ulong getPlayerStart(ulong invOffset) => invOffset - 0x10 - PlayerOtherStartPadding + 0x110;
        public static ulong getPlayerIdAddress(ulong invOffset) => getPlayerStart(invOffset) + 0xC138;
        public static ulong getPlayerProfileMainAddress(ulong invOffset) => getPlayerStart(invOffset) + 0x12830;
        public static ulong getManpu(ulong invOffset) => invOffset - 0x10 + 0x12C7C + 72;
        public static ulong getTownNameAddress(ulong invOffset) => getPlayerIdAddress(invOffset) - 0xB8 + 0x04;

        // main save offsets
        public const ulong TurnipAddress = 0xB14DBB30;
        public const ulong VillagerAddress = TurnipAddress - 0x2d40 - 0x48d920 + 0x10;
        public const ulong VillagerHouseAddress = TurnipAddress - 0x2d40 - 0x48d920 + 0x481c10;
        public const ulong BackupSaveDiff = 0x9B0EB0;

        private const ulong FieldItemStart = VillagerAddress - 0x10 + 0x22f3f0;
        private const ulong FieldBufferSize = MapGrid.AcreHeight * 32 * 32 * (MapGrid.AcreWidth + 1);
        public const ulong FieldSize = MapGrid.MapTileCount32x32 * Item.SIZE;
        public const ulong FieldItemStartLayer1 = FieldItemStart + FieldBufferSize;
        public const ulong FieldItemStartLayer2 = (FieldItemStart + FieldSize) + (FieldBufferSize * 3); // 2 for layer 1 + 1 buffer for this layer

        public const ulong LandMakingMapStart = FieldItemStart + 0xdb600;
        public const ulong OutsideFieldStart = FieldItemStart + 0x1005ac;
        public const ulong MainFieldStructurStart = FieldItemStart + 0x100200;

        // other addresses
        public const ulong TextSpeedAddress = 0xBD9A9FC;
        public const ulong ChatBufferSize = 0x1E;

        public const ulong DodoAddress = 0xAC1A164;
        public const ulong OnlineSessionAddress = 0x9499748;
        public const ulong TimeAddress = 0x0BD91B00;

        // pointers
        public static readonly long[] PlayerCoordJumps = [0x4BF9E30L, 0x18L, 0x178L, 0xD0L, 0xD8L]; // [[[[main+4BF9E30]+18]+178]+D0]+D8
        public static readonly long[] ChatCoordJumps = [0x5254A40L, 0x40L];

        public static readonly long[] VillagerArrivingJumps = [0x526B6E8L, 0x40L, 0x170L];
        public static readonly long[] VillagerArrivingNIDJumps = [0x526B6E8L, 0xD0L];
        public const ulong ArriverVillageShift = 0x1C;

        public static readonly long[] VillagerListJumps = [0x59E5A40L, 0x38L, 0xE0L, 0x1ECL, 0x17AL];
        public const ulong VillagerListUnitSize = 0x1C;

        // exefs (main)
        public const ulong AnimationSpeedOffset = 0x043BC3C0;
        public const ulong WalkSpeedOffset = 0x016127A0;
        public const ulong CollisionStateOffset = 0x0155FDC0;
        public const ulong TimeStateAddress = 0x00328BD0;

        public const ulong ArriverNID = 0xBA022438;

        // dlc
        public const ulong PokiAddress = 0xB449E6C8;
    }
}

