using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NHSE.Core;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public sealed class CrossBot : SwitchRoutineExecutor<CrossBotConfig>
    {
        public readonly ConcurrentQueue<ItemRequest> Injections = new();
        public readonly ConcurrentQueue<OrderRequest<MultiItem>> Orders = new();
        public readonly LoopHelpers Loopers;
        public readonly DropBotState State;
        public readonly AnchorHelper Anchors;

        public bool CleanRequested { private get; set; }
        public string DodoCode { get; set; } = "No code set yet.";

        public CrossBot(CrossBotConfig cfg) : base(cfg)
        {
            State = new DropBotState(cfg.DropConfig);
            Loopers = new LoopHelpers(this);
            Anchors = new AnchorHelper(Config.AnchorFilename);
        }

        private const int pocket = Item.SIZE * 20;
        private const int size = (pocket + 0x18) * 2;
        private const int shift = -0x18 - (Item.SIZE * 20);

        public override void SoftStop() => Config.AcceptingCommands = false;

        protected override async Task MainLoop(CancellationToken token)
        {
            // Disconnect our virtual controller; will reconnect once we send a button command after a request.
            LogUtil.LogInfo("Detaching controller on startup as first interaction.", Config.IP);
            await Connection.SendAsync(SwitchCommand.DetachController(), token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);

            // For viewing player vectors
            // await ViewPlayerVectors(token).ConfigureAwait(false);

            // Validate inventory offset.
            LogUtil.LogInfo("Checking inventory offset for validity.", Config.IP);
            var valid = await GetIsPlayerInventoryValid(Config.Offset, token).ConfigureAwait(false);
            if (!valid)
            {
                LogUtil.LogInfo($"Inventory read from {Config.Offset} (0x{Config.Offset:X8}) does not appear to be valid.", Config.IP);
                if (Config.RequireValidInventoryMetadata)
                {
                    LogUtil.LogInfo("Exiting!", Config.IP);
                    return;
                }
            }

            LogUtil.LogInfo("Successfully connected to bot. Starting main loop!", Config.IP);
            while (!token.IsCancellationRequested)
                await OrderLoop(token).ConfigureAwait(false);
        }


        private async Task OrderLoop(CancellationToken token)
        {
            if (!Config.AcceptingCommands)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                return;
            }

            await EnsureAnchorsAreInitialised(token);

            if (Orders.TryDequeue(out var item))
            {
                await ExecuteOrder(item, token).ConfigureAwait(false);
            }
        }

        private async Task<OrderResult> ExecuteOrder(OrderRequest<MultiItem> order, CancellationToken token)
        {
            // Method:
            // 1) Restart the game. This is the most reliable way to do this, and not much slower than closing the gate.
            // 2) Wait for Isabelle's speech (if any), teleport player into their airport then in front of orville, open gate & dodo code
            // 3) Notify player to come now, teleport outside into drop zone, wait the config time or until the user leaves
            // 4) Once the timer runs out or the user leaves, start over.

            await RestartGame(token).ConfigureAwait(false);

            // Wait for the load time which feels like an age.
            await Task.Delay(55_000, token).ConfigureAwait(false);

            // Press A a few times on title screen
            for (int i = 0; i < 5; ++i)
                await Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);

            // Inject order while waiting for load

            // Wait for the game to teleport us from the "hell" position to our front door. Keep pressing A & B incase we're stuck at the day intro.
            bool gameStarted = await EnsureAnchorMatches(0, 75_000, async () =>
            {
                await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);
                await Click(SwitchButton.B, 0_500, token).ConfigureAwait(false);
            }, token);

            if (!gameStarted)
            {
                var error = "Failed to reach the overworld.";
                LogUtil.LogError($"{error} Trying next request.", Config.IP);
                order.OrderCancelled(this, $"{error} Sorry, your request has been removed.");
                return OrderResult.Faulted;
            }

            order.OrderInitializing(this, string.Empty);

            while (!await Loopers.IsOverworld(Config.CoordinatePointer, token).ConfigureAwait(false))
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Delay for animation
            await Task.Delay(1_800, token).ConfigureAwait(false);

            // Inject the airport entry anchor
            await SendAnchorBytes(2, token).ConfigureAwait(false);

            // Get out of any calls, events, etc
            bool atAirport = await EnsureAnchorMatches(2, 20_000, async () =>
            {
                await Click(SwitchButton.A, 0_300, token).ConfigureAwait(false);
                await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);
                await SendAnchorBytes(2, token).ConfigureAwait(false);
            }, token);

            // Go into airport
            await SetStick(SwitchStick.LEFT, short.MaxValue, short.MaxValue, 1_500, token).ConfigureAwait(false);
            await SetStick(SwitchStick.LEFT, 0, 0, 1_500, token).ConfigureAwait(false);

            while (!await Loopers.IsOverworld(Config.CoordinatePointer, token).ConfigureAwait(false))
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Delay for animation
            await Task.Delay(1_200, token).ConfigureAwait(false);

            // Teleport to Orville (twice, in case we get pulled back)
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(3, token).ConfigureAwait(false);

            var coord = await Loopers.GetCoordinateAddress(Config.CoordinatePointer, token).ConfigureAwait(false);
            await Loopers.GetDodoCode(coord, Config.DodoOffset, token).ConfigureAwait(false);

            order.OrderReady(this, $"Your Dodo code is {Loopers.DodoCode}");

            // Teleport to airport leave zone (twice, in case we get pulled back)
            await SendAnchorBytes(4, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(4, token).ConfigureAwait(false);

            // Walk out
            await SetStick(SwitchStick.LEFT, 0, -20_000, 1_500, token).ConfigureAwait(false);
            await SetStick(SwitchStick.LEFT, 0, 0, 1_500, token).ConfigureAwait(false);

            while (!await Loopers.IsOverworld(Config.CoordinatePointer, token).ConfigureAwait(false))
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Delay for animation
            await Task.Delay(1_200, token).ConfigureAwait(false);

            while (!await Loopers.IsOverworld(Config.CoordinatePointer, token).ConfigureAwait(false))
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Teleport to drop zone (twice, in case we get pulled back)
            await SendAnchorBytes(1, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(1, token).ConfigureAwait(false);

            return OrderResult.Success;
        }

        private async Task RestartGame(CancellationToken token)
        {
            // Close game
            await Click(SwitchButton.HOME, 1_800, token).ConfigureAwait(false);
            await Click(SwitchButton.X, 0_500, token).ConfigureAwait(false);
            await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);

            // Wait for "closing software" wheel
            await Task.Delay(10_000, token).ConfigureAwait(false);

            // Start game
            for (int i = 0; i < 5; ++i)
                await Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
        }

        private async Task<bool> EnsureAnchorMatches(int anchorIndex, int millisecondsTimeout, Func<Task> toDoPerLoop, CancellationToken token)
        {
            bool success = false;
            var startTime = DateTime.Now;
            while (!success)
            {
                if (toDoPerLoop != null)
                    await toDoPerLoop().ConfigureAwait(false);

                bool anchorMatches = await DoesAnchorMatch(anchorIndex, token).ConfigureAwait(false);
                if (!anchorMatches)
                    await Task.Delay(1_000, token).ConfigureAwait(false);
                else
                    success = true;

                if (Math.Abs((DateTime.Now - startTime).TotalMilliseconds) > millisecondsTimeout)
                    return false;
            }

            return true;
        }

        // Does the current RAM anchor match the one we've saved?
        private async Task<bool> DoesAnchorMatch(int anchorIndex, CancellationToken token)
        {
            var anchorMemory = await ReadAnchor(token).ConfigureAwait(false);
            return anchorMemory.AnchorBytes.SequenceEqual(Anchors.Anchors[anchorIndex].AnchorBytes);
        }

        private async Task EnsureAnchorsAreInitialised(CancellationToken token)
        {
            while (Config.ForceUpdateAnchors || Anchors.IsOneEmpty(out _))
                await Task.Delay(1_000).ConfigureAwait(false);
        }

        public async Task<bool> UpdateAnchor(int index, CancellationToken token)
        {
            var anchors = Anchors.Anchors;
            if (index < 0 || index > anchors.Length)
                return false;

            var anchor = await ReadAnchor(token).ConfigureAwait(false);
            var bytesA = anchor.Anchor1;
            var bytesB = anchor.Anchor2;

            anchors[index].Anchor1 = bytesA;
            anchors[index].Anchor2 = bytesB;
            Anchors.Save();
            LogUtil.LogInfo($"Updated anchor {index}.", Config.IP);
            return true;
        }

        public async Task<bool> SendAnchorBytes(int index, CancellationToken token)
        {
            var anchors = Anchors.Anchors;
            if (index < 0 || index > anchors.Length)
                return false;

            ulong offset = await Loopers.GetCoordinateAddress(Config.CoordinatePointer, token).ConfigureAwait(false);
            await Connection.WriteBytesAbsoluteAsync(anchors[index].Anchor1, offset, token).ConfigureAwait(false);
            await Connection.WriteBytesAbsoluteAsync(anchors[index].Anchor2, offset + 0x3A, token).ConfigureAwait(false);

            return true;
        }

        private async Task<PosRotAnchor> ReadAnchor(CancellationToken token)
        {
            ulong offset = await Loopers.GetCoordinateAddress(Config.CoordinatePointer, token).ConfigureAwait(false);
            var bytesA = await Connection.ReadBytesAbsoluteAsync(offset, 0xA, token).ConfigureAwait(false);
            var bytesB = await Connection.ReadBytesAbsoluteAsync(offset + 0x3A, 0x4, token).ConfigureAwait(false);
            var sequentinalAnchor = bytesA.Concat(bytesB).ToArray();
            return new PosRotAnchor(sequentinalAnchor);
        }

        private async Task DropLoop(CancellationToken token)
        {
            if (!Config.AcceptingCommands)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                return;
            }

            if (Injections.TryDequeue(out var item))
            {
                var count = await DropItems(item, token).ConfigureAwait(false);
                State.AfterDrop(count);
            }
            else if ((State.CleanRequired && State.Config.AutoClean) || CleanRequested)
            {
                await CleanUp(State.Config.PickupCount, token).ConfigureAwait(false);
                State.AfterClean();
                CleanRequested = false;
            }
            else
            {
                State.StillIdle();
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }
        }

        private async Task<bool> GetIsPlayerInventoryValid(uint playerOfs, CancellationToken token)
        {
            var (ofs, len) = InventoryValidator.GetOffsetLength(playerOfs);
            var inventory = await Connection.ReadBytesAsync(ofs, len, token).ConfigureAwait(false);

            return InventoryValidator.ValidateItemBinary(inventory);
        }

        private async Task<int> DropItems(ItemRequest drop, CancellationToken token)
        {
            int dropped = 0;
            bool first = true;
            foreach (var item in drop.Items)
            {
                await DropItem(item, first, token).ConfigureAwait(false);
                first = false;
                dropped++;
            }
            return dropped;
        }

        private async Task<(bool success, byte[] data)> ReadValidate(CancellationToken token)
        {
            var data = await Connection.ReadBytesAsync((uint)(Config.Offset + shift), size, token).ConfigureAwait(false);
            return (Validate(data), data);
        }

        private async Task DropItem(Item item, bool first, CancellationToken token)
        {
            // Exit out of any menus.
            if (first)
            {
                for (int i = 0; i < 3; i++)
                    await Click(SwitchButton.B, 0_400, token).ConfigureAwait(false);
            }

            var itemName = GameInfo.Strings.GetItemName(item);
            LogUtil.LogInfo($"Injecting Item: {item.DisplayItemId:X4} ({itemName}).", Config.IP);

            // Inject item into entire inventory
            (bool success, byte[] data) = await ReadValidate(token).ConfigureAwait(false);

            var Items = new Item[40];
            for (int i = 0; i < 40; ++i)
                Items[i] = item;

            var pocket2 = Items.Take(20).ToArray();
            var pocket1 = Items.Skip(20).ToArray();
            var p1 = Item.SetArray(pocket1);
            var p2 = Item.SetArray(pocket2);

            p1.CopyTo(data, 0);
            p2.CopyTo(data, pocket + 0x18);

            var poke = SwitchCommand.Poke((uint)(Config.Offset + shift), data);
            await Connection.SendAsync(poke, token).ConfigureAwait(false);
            await Task.Delay(0_300, token).ConfigureAwait(false);

            // Open player inventory and open the currently selected item slot -- assumed to be the config offset.
            await Click(SwitchButton.X, 1_100, token).ConfigureAwait(false);
            await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);

            // Navigate down to the "drop item" option.
            var downCount = item.GetItemDropOption();
            for (int i = 0; i < downCount; i++)
                await Click(SwitchButton.DDOWN, 0_400, token).ConfigureAwait(false);

            // Drop item, close menu.
            await Click(SwitchButton.A, 0_400, token).ConfigureAwait(false);
            await Click(SwitchButton.X, 0_400, token).ConfigureAwait(false);

            // Exit out of any menus (fail-safe)
            for (int i = 0; i < 2; i++)
                await Click(SwitchButton.B, 0_400, token).ConfigureAwait(false);
        }

        private async Task CleanUp(int count, CancellationToken token)
        {
            LogUtil.LogInfo("Picking up leftover items during idle time.", Config.IP);

            // Exit out of any menus.
            for (int i = 0; i < 3; i++)
                await Click(SwitchButton.B, 0_400, token).ConfigureAwait(false);

            // Pick up and delete.
            for (int i = 0; i < count; i++)
            {
                await Click(SwitchButton.Y, 2_000, token).ConfigureAwait(false);
                var poke = SwitchCommand.Poke(Config.Offset, Item.NONE.ToBytes());
                await Connection.SendAsync(poke, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }
        }

        public bool Validate(byte[] data)
        {
            return InventoryValidator.ValidateItemBinary(data);
        }

        private async Task ViewPlayerVectors(CancellationToken token)
        {
            ulong offset = await Loopers.GetCoordinateAddress(Config.CoordinatePointer, token).ConfigureAwait(false);
            int index = 0;
            byte[] a1 = new byte[2]; byte[] b1 = new byte[2];
            while (!token.IsCancellationRequested)
            {
                var bytesPos = await Connection.ReadBytesAbsoluteAsync(offset, 0xA, token).ConfigureAwait(false);
                var bytesRot = await Connection.ReadBytesAbsoluteAsync(offset + 0x3A, 0x4, token).ConfigureAwait(false);
                Console.WriteLine("Byte array 1: ");
                foreach (var b in bytesPos)
                    Console.Write($"{b}, ");
                Console.WriteLine();
                Console.WriteLine("Byte array 2: ");
                foreach (var b in bytesRot)
                    Console.Write($"{b}, ");
                Console.WriteLine();

                var rot = BitConverter.ToSingle(bytesRot, 0);
                Console.WriteLine($"Parsed second value: {rot}");

                await Task.Delay(0_500, token).ConfigureAwait(false);

                if (index == 0)
                {
                    a1 = bytesPos;
                    b1 = bytesRot;
                }
                if (index == 10)
                {
                    index = 0;
                    await Connection.WriteBytesAbsoluteAsync(a1, offset, token);
                    await Connection.WriteBytesAbsoluteAsync(b1, offset + 0x3A, token);
                }
                index++;
            }
        }
    }
}
