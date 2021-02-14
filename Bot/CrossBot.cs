using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NHSE.Core;
using ACNHMobileSpawner;
using SysBot.Base;
using System.Text;
using System.IO;
using System.Collections.Generic;

namespace SysBot.ACNHOrders
{
    public sealed class CrossBot : SwitchRoutineExecutor<CrossBotConfig>
    {
        private const uint InventoryOffset = (uint)OffsetHelper.InventoryOffset;

        public readonly ConcurrentQueue<ItemRequest> Injections = new();
        public readonly ConcurrentQueue<OrderRequest<Item>> Orders = new();
        public readonly DodoPositionHelper DodoPosition;
        public readonly DropBotState State;
        public readonly AnchorHelper Anchors;
        public readonly DummyOrder<Item> DummyRequest = new();

        public MapTerrainLite Map { get; private set; } = new MapTerrainLite(new byte[MapGrid.MapTileCount32x32 * Item.SIZE]);
        public bool CleanRequested { private get; set; }
        public bool RestoreRestartRequested { private get; set; }
        public string DodoCode { get; set; } = "No code set yet.";
        public string LastArrival { get; private set; } = string.Empty;
        public ulong CurrentUserId { get; set; } = default!;
        public string CurrentUserName { get; set; } = string.Empty;
        public bool GameIsDirty { get; set; } = true; // Dirty if crashed or last user didn't arrive/leave correctly

        public CrossBot(CrossBotConfig cfg) : base(cfg)
        {
            State = new DropBotState(cfg.DropConfig);
            DodoPosition = new DodoPositionHelper(this);
            Anchors = new AnchorHelper(Config.AnchorFilename);
        }

        private const int pocket = Item.SIZE * 20;
        private const int size = (pocket + 0x18) * 2;
        private const int shift = -0x18 - (Item.SIZE * 20);

        public override void SoftStop() => Config.AcceptingCommands = false;

        protected override async Task MainLoop(CancellationToken token)
        {
            // Validate map spawn vector
            if (Config.MapPlaceX < 0 || Config.MapPlaceX >= (MapGrid.AcreWidth * 32))
            {
                LogUtil.LogInfo($"{Config.MapPlaceX} is not a valid value for {nameof(Config.MapPlaceX)}. Exiting!", Config.IP);
                return;
            }

            if (Config.MapPlaceY < 0 || Config.MapPlaceY >= (MapGrid.AcreHeight * 32))
            {
                LogUtil.LogInfo($"{Config.MapPlaceY} is not a valid value for {nameof(Config.MapPlaceY)}. Exiting!", Config.IP);
                return;
            }

            // Disconnect our virtual controller; will reconnect once we send a button command after a request.
            LogUtil.LogInfo("Detaching controller on startup as first interaction.", Config.IP);
            await Connection.SendAsync(SwitchCommand.DetachController(), token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);

            // For viewing player vectors
            //await ViewPlayerVectors(token).ConfigureAwait(false);

            // Validate inventory offset.
            LogUtil.LogInfo("Checking inventory offset for validity.", Config.IP);
            var valid = await GetIsPlayerInventoryValid(InventoryOffset, token).ConfigureAwait(false);
            if (!valid)
            {
                LogUtil.LogInfo($"Inventory read from {InventoryOffset} (0x{InventoryOffset:X8}) does not appear to be valid.", Config.IP);
                if (Config.RequireValidInventoryMetadata)
                {
                    LogUtil.LogInfo("Exiting!", Config.IP);
                    return;
                }
            }

            // Pull original map items and store them
            LogUtil.LogInfo("Reading original map status. Please wait...", Config.IP);
            var bytes = await Connection.ReadBytesLargeAsync((uint)OffsetHelper.FieldItemStart, MapGrid.MapTileCount32x32 * Item.SIZE, Config.MapPullChunkSize, token).ConfigureAwait(false);
            Map = new MapTerrainLite(bytes)
            {
                SpawnX = Config.MapPlaceX,
                SpawnY = Config.MapPlaceY
            };

            if (Config.ForceUpdateAnchors)
                LogUtil.LogInfo("Force update anchors set to true, not functionality will be activate", Config.IP);

            LogUtil.LogInfo("Successfully connected to bot. Starting main loop!", Config.IP);
            if (Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                LogUtil.LogInfo("Orders not accepted in dodo restore mode! Please ensure all joy-cons and controllers are docked!", Config.IP);
                while (!token.IsCancellationRequested)
                    await DodoRestoreLoop(false, token).ConfigureAwait(false);
            }

            while (!token.IsCancellationRequested)
                await OrderLoop(token).ConfigureAwait(false);
        }

        private async Task DodoRestoreLoop(bool immediateRestart, CancellationToken token)
        {
            await EnsureAnchorsAreInitialised(token);

            bool hardCrash = immediateRestart;
            if (!immediateRestart)
            {
                byte[] bytes = await Connection.ReadBytesAsync((uint)OffsetHelper.DodoAddress, 0x5, token).ConfigureAwait(false);
                DodoCode = Encoding.UTF8.GetString(bytes, 0, 5);

                if (DodoPosition.IsDodoValid(DodoCode) && Config.DodoModeConfig.EchoDodoChannels.Count > 0)
                    await AttemptEchoHook($"[{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] The Dodo code has updated, the new Dodo code is: {DodoCode}.", Config.DodoModeConfig.EchoDodoChannels, token).ConfigureAwait(false);

                await SaveDodoCodeToFile(token).ConfigureAwait(false);

                while ((await Connection.ReadBytesAsync((uint)OffsetHelper.OnlineSessionAddress, 0x1, token).ConfigureAwait(false))[0] == 1)
                {
                    await Task.Delay(2_000, token).ConfigureAwait(false);

                    if (RestoreRestartRequested)
                    {
                        RestoreRestartRequested = false;
                        await DodoRestoreLoop(true, token).ConfigureAwait(false);
                        return;
                    }

                    if (Config.DodoModeConfig.RefreshMap)
                        await ClearMapAndSpawnInternally(null, Map, token).ConfigureAwait(false);

                    if (Config.DodoModeConfig.MashB)
                        for (int i = 0; i < 5; ++i)
                            await Click(SwitchButton.B, 0_200, token).ConfigureAwait(false);

                    // Check for new arrivals
                    if (await IsArriverNew(token).ConfigureAwait(false))
                    {
                        if (Config.DodoModeConfig.EchoArrivalChannels.Count > 0)
                            await AttemptEchoHook($"[{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] {LastArrival} is joining the island.{(Config.DodoModeConfig.PostDodoCodeWithNewArrivals ? $" Dodo code is: {DodoCode}." : string.Empty)}", Config.DodoModeConfig.EchoArrivalChannels, token).ConfigureAwait(false);

                        await Task.Delay(60_000, token).ConfigureAwait(false);

                        // Clear username of last arrival
                        await Connection.WriteBytesAsync(new byte[0x14], (uint)OffsetHelper.ArriverNameLocAddress, token).ConfigureAwait(false);
                        LastArrival = string.Empty;
                    }
                }

                if (Config.DodoModeConfig.EchoDodoChannels.Count > 0)
                    await AttemptEchoHook($"[{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] Crash detected. Please wait while I get a new Dodo code.", Config.DodoModeConfig.EchoDodoChannels, token).ConfigureAwait(false);
                LogUtil.LogInfo($"Crash detected, awaiting overworld to fetch new dodo.", Config.IP);
                await Task.Delay(5_000, token).ConfigureAwait(false);

                // Clear dodo code
                await Connection.WriteBytesAsync(new byte[5], (uint)OffsetHelper.DodoAddress, token).ConfigureAwait(false);

                var startTime = DateTime.Now;
                // Wait for overworld
                LogUtil.LogInfo($"Begin overworld wait loop.", Config.IP);
                while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.Overworld)
                {
                    await Task.Delay(1_000, token).ConfigureAwait(false);
                    await Click(SwitchButton.B, 0_100, token).ConfigureAwait(false);
                    if (Math.Abs((DateTime.Now - startTime).TotalSeconds) > 45)
                    {
                        LogUtil.LogError($"Hard crash detected, restarting game.", Config.IP);
                        hardCrash = true;
                        break;
                    }
                }
                LogUtil.LogInfo($"End overworld wait loop.", Config.IP);
            }

            var result = await ExecuteOrderStart(DummyRequest, true, hardCrash, token).ConfigureAwait(false);

            if (result != OrderResult.Success)
            {
                LogUtil.LogError($"Dodo restore failed with error: {result}. Restarting game...", Config.IP);
                await DodoRestoreLoop(true, token).ConfigureAwait(false);
                return;
            }
            await SaveDodoCodeToFile(token).ConfigureAwait(false);

            LogUtil.LogError($"Dodo restore successful. New dodo is {DodoCode} and saved to {Config.DodoModeConfig.DodoRestoreFilename}.", Config.IP);
        }

        // hacked in discord forward, should really be a delegate or resusable forwarder
        private async Task AttemptEchoHook(string message, IReadOnlyCollection<ulong> channels, CancellationToken token)
        {
            foreach (var msgChannel in channels)
                if (!await Globals.Self.TrySpeakMessage(msgChannel, message).ConfigureAwait(false))
                    LogUtil.LogError($"Unable to post into channels: {msgChannel}.", Config.IP);
        }

        private async Task OrderLoop(CancellationToken token)
        {
            if (!Config.AcceptingCommands)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false);
                return;
            }

            await EnsureAnchorsAreInitialised(token);

            if (Orders.TryDequeue(out var item) && !item.SkipRequested)
            {
                var result = await ExecuteOrder(item, token).ConfigureAwait(false);
                
                // Cleanup
                LogUtil.LogInfo($"Exited order with result: {result}", Config.IP);
                CurrentUserId = default!;
                LastArrival = string.Empty;
                CurrentUserName = string.Empty;
            }
        }

        private async Task<OrderResult> ExecuteOrder(IACNHOrderNotifier<Item> order, CancellationToken token)
        {
            LogUtil.LogInfo($"Starting next order for: {order.VillagerName} ({order.UserGuid})", Config.IP);
            CurrentUserName = order.VillagerName;

            // Clear any lingering injections from the last user
            Injections.ClearQueue();

            int timeOut = (Config.OrderConfig.UserTimeAllowed + 360) * 1_000; // 360 seconds = 6 minutes
            var cts = new CancellationTokenSource(timeOut);
            var cToken = cts.Token; // tokens need combining, somehow & eventually
            OrderResult result = OrderResult.Faulted;
            var orderTask = GameIsDirty ? ExecuteOrderStart(order, false, true, cToken) : ExecuteOrderMidway(order, cToken);
            try
            {
                result = await orderTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException e)
            {
                LogUtil.LogInfo($"{order.VillagerName} ({order.UserGuid}) had their order timeout: {e.Message}.", Config.IP);
                order.OrderCancelled(this, "Unfortunately a game crash occured while your order was in progress. Sorry, your request has been removed.", true);
            }

            if (result == OrderResult.Success)
            {
                await CloseGate(token).ConfigureAwait(false);
                GameIsDirty = false;
            }
            else
            {
                await EndSession(token).ConfigureAwait(false);
                GameIsDirty = true;
            }

            // Clear username of last arrival
            await Connection.WriteBytesAsync(new byte[0x14], (uint)OffsetHelper.ArriverNameLocAddress, token).ConfigureAwait(false);

            return result;
        }

        // execute order directly after someone else's order
        private async Task<OrderResult> ExecuteOrderMidway(IACNHOrderNotifier<Item> order, CancellationToken token)
        {
            while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            order.OrderInitializing(this, string.Empty);

            // Setup order locally, clear map by puliing all and checking difference. Read is much faster than write
            await ClearMapAndSpawnInternally(order.Order, Map, token).ConfigureAwait(false);

            // inject order
            await InjectOrder(Map, token).ConfigureAwait(false);

            // Teleport to Orville, we should already be there but disconnects cause player to turn around (twice, in case we get pulled back)
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(3, token).ConfigureAwait(false);

            return await FetchDodoAndAwaitOrder(order, false, token).ConfigureAwait(false);
        }

        // execute order from scratch (press home, shutdown game, start over, usually due to "a connection error has occured")
        private async Task<OrderResult> ExecuteOrderStart(IACNHOrderNotifier<Item> order, bool ignoreInjection, bool fromRestart, CancellationToken token)
        {
            // Method:
            // 1) Restart the game. This is the most reliable way to do this if running endlessly atm. Dodo code offset shifts are bizarre and don't have good pointers.
            // 2) Wait for Isabelle's speech (if any), Notify player to be ready, teleport player into their airport then in front of orville, open gate & inform dodo code.
            // 3) Notify player to come now, teleport outside into drop zone, wait for drop command in their DMs, the config time or until the player leaves
            // 4) Once the timer runs out or the user leaves, start over with next user.

            if (fromRestart)
            {
                await RestartGame(token).ConfigureAwait(false);

                // Reset any sticks
                await SetStick(SwitchStick.LEFT, 0, 0, 0_500, token).ConfigureAwait(false);

                // Setup order locally, clear map by puliing all and checking difference. Read is much faster than write
                if (!ignoreInjection)
                    await ClearMapAndSpawnInternally(order.Order, Map, token).ConfigureAwait(false);

                // Press A on title screen
                await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);

                // Wait for the load time which feels like an age.
                // Wait for the game to teleport us from the "hell" position to our front door. Keep pressing A & B incase we're stuck at the day intro.
                int echoCount = 0;
                bool gameStarted = await EnsureAnchorMatches(0, 130_000, async () =>
                {
                    await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);
                    await Click(SwitchButton.B, 0_500, token).ConfigureAwait(false);
                    if (echoCount < 5)
                    {
                        if (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) == OverworldState.Overworld)
                        {
                            LogUtil.LogInfo("Reached overworld but Anchor 0 does not match.", Config.IP);
                            echoCount++;
                        }
                    }

                }, token);

                if (!gameStarted)
                {
                    var error = "Failed to reach the overworld.";
                    LogUtil.LogError($"{error} Trying next request.", Config.IP);
                    order.OrderCancelled(this, $"{error} Sorry, your request has been removed.", true);
                    return OrderResult.Faulted;
                }

                // inject order
                if (!ignoreInjection)
                {
                    await InjectOrder(Map, token).ConfigureAwait(false);

                    order.OrderInitializing(this, string.Empty);
                }
            }

            while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Delay for animation
            await Task.Delay(1_800, token).ConfigureAwait(false);
            // Unhold any held items
            await Click(SwitchButton.DDOWN, 0_300, token).ConfigureAwait(false);

            LogUtil.LogInfo($"Reached overworld, teleporting to the airport.", Config.IP);

            // Inject the airport entry anchor
            await SendAnchorBytes(2, token).ConfigureAwait(false);

            if (ignoreInjection)
            {
                await SendAnchorBytes(1, token).ConfigureAwait(false);
                LogUtil.LogInfo($"Checking for morning announcement", Config.IP);
                // We need to check for Isabelle's morning announcement
                for (int i = 0; i < 3; ++i)
                    await Click(SwitchButton.B, 0_400, token).ConfigureAwait(false);
                while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.Overworld)
                {
                    await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);
                    await Task.Delay(1_000, token).ConfigureAwait(false);
                }
            }

            // Get out of any calls, events, etc
            bool atAirport = await EnsureAnchorMatches(2, 10_000, async () =>
            {
                await Click(SwitchButton.A, 0_300, token).ConfigureAwait(false);
                await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);
                await SendAnchorBytes(2, token).ConfigureAwait(false);
            }, token);

            await Task.Delay(0_500, token).ConfigureAwait(false);

            LogUtil.LogInfo($"Entering airport.", Config.IP);

            await EnterAirport(token).ConfigureAwait(false);

            // Teleport to Orville (twice, in case we get pulled back)
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(3, token).ConfigureAwait(false);

            return await FetchDodoAndAwaitOrder(order, ignoreInjection, token).ConfigureAwait(false);
        }

        private async Task ClearMapAndSpawnInternally(Item[]? order, MapTerrainLite clearMap, CancellationToken token)
        {
            if (order != null)
            {
                clearMap.Spawn(MultiItem.DeepDuplicateItem(Item.NO_ITEM, 40)); // clear area
                clearMap.Spawn(order);
            }
            await Task.Delay(5_000, token).ConfigureAwait(false);
            if (order != null)
                LogUtil.LogInfo("Map clear has started.", Config.IP);
            var mapData = await Connection.ReadBytesLargeAsync((uint)OffsetHelper.FieldItemStart, MapTerrainLite.ByteSize, Config.MapPullChunkSize, token).ConfigureAwait(false);
            var offData = clearMap.GetDifferencePrioritizeStartup(mapData, Config.MapPullChunkSize, (uint)OffsetHelper.FieldItemStart);
            for (int i = 0; i < offData.Length; ++i)
                await Connection.WriteBytesAsync(offData[i].ToSend, offData[i].Offset, token).ConfigureAwait(false);
            if (order != null)
                LogUtil.LogInfo("Map clear has ended.", Config.IP);
        }

        private async Task<OrderResult> FetchDodoAndAwaitOrder(IACNHOrderNotifier<Item> order, bool ignoreInjection, CancellationToken token)
        {
            LogUtil.LogInfo($"Talking to Orville. Attempting to get Dodo code.", Config.IP);
            var coord = await DodoPosition.GetCoordinateAddress(Config.CoordinatePointer, token).ConfigureAwait(false);
            await DodoPosition.GetDodoCode(coord, (uint)OffsetHelper.DodoAddress, false, token).ConfigureAwait(false);

            // try again if we failed to get a dodo
            if (Config.OrderConfig.RetryFetchDodoOnFail && !DodoPosition.IsDodoValid(DodoPosition.DodoCode))
            {
                LogUtil.LogInfo("Failed to get a valid Dodo code. Trying again...", Config.IP);
                for (int i = 0; i < 8; ++i)
                    await Click(SwitchButton.B, 1_000, token).ConfigureAwait(false);
                await DodoPosition.GetDodoCode(coord, (uint)OffsetHelper.DodoAddress, true, token).ConfigureAwait(false);
            }

            if (!DodoPosition.IsDodoValid(DodoPosition.DodoCode))
            {
                var error = "Failed to connect to the internet and obtain a Dodo code.";
                LogUtil.LogError($"{error} Trying next request.", Config.IP);
                order.OrderCancelled(this, $"A connection error occured: {error} Sorry, your request has been removed.", true);
                return OrderResult.Faulted;
            }

            DodoCode = DodoPosition.DodoCode;
            if (!ignoreInjection)
                order.OrderReady(this, $"You have {(int)(Config.OrderConfig.WaitForArriverTime * 0.9f)} seconds to arrive. Your Dodo code is **{DodoCode}**");

            // Teleport to airport leave zone (twice, in case we get pulled back)
            await SendAnchorBytes(4, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(4, token).ConfigureAwait(false);

            // Walk out
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SetStick(SwitchStick.LEFT, 0, -20_000, 1_500, token).ConfigureAwait(false);
            await SetStick(SwitchStick.LEFT, 0, 0, 1_500, token).ConfigureAwait(false);

            while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Delay for animation
            await Task.Delay(1_200, token).ConfigureAwait(false);

            while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Teleport to drop zone (twice, in case we get pulled back)
            await SendAnchorBytes(1, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(1, token).ConfigureAwait(false);

            if (ignoreInjection)
                return OrderResult.Success;

            LogUtil.LogInfo($"Waiting for arrival.", Config.IP);
            var startTime = DateTime.Now;
            // Wait for arrival
            while (!await IsArriverNew(token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                if (Math.Abs((DateTime.Now - startTime).TotalSeconds) > Config.OrderConfig.WaitForArriverTime)
                {
                    var error = "Visitor failed to arrive.";
                    LogUtil.LogError($"{error}. Removed from queue, moving to next order.", Config.IP);
                    order.OrderCancelled(this, $"{error} Your request has been removed.", false);
                    return OrderResult.NoArrival;
                }
            }

            order.SendNotification(this, $"Visitor arriving: {LastArrival}. Your items will be in front of you once you land.");

            // Wait for arrival animation (flight board, arrival through gate, terrible dodo seaplane joke, etc)
            await Task.Delay(Config.OrderConfig.ArrivalTime * 1_000, token).ConfigureAwait(false);

            // Ensure we're on overworld before starting timer/drop loop
            while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Update current user Id such that they may use drop commands
            CurrentUserId = order.UserGuid;

            // We check if the user has left by checking whether or not someone hits the Arrive/Leaving state
            startTime = DateTime.Now;
            bool warned = false;
            while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.UserArriveLeaving)
            {
                await DropLoop(token).ConfigureAwait(false);
                await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);
                if (Math.Abs((DateTime.Now - startTime).TotalSeconds) > (Config.OrderConfig.UserTimeAllowed - 60) && !warned)
                {
                    order.SendNotification(this, "You have 60 seconds remaining before I start the next order. Please ensure you can collect your items and leave within that time.");
                    warned = true;
                }

                if (Math.Abs((DateTime.Now - startTime).TotalSeconds) > Config.OrderConfig.UserTimeAllowed)
                {
                    var error = "Visitor failed to leave.";
                    LogUtil.LogError($"{error}. Removed from queue, moving to next order.", Config.IP);
                    order.OrderCancelled(this, $"{error} Your request has been removed.", false);
                    return OrderResult.NoLeave;
                }
            }

            LogUtil.LogInfo($"Order completed. Notifying visitor of completion.", Config.IP);
            order.OrderFinished(this, Config.OrderConfig.CompleteOrderMessage);

            await Task.Delay(20_000, token).ConfigureAwait(false);

            // Ensure we're on overworld before exiting
            while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // finish "circle in" animation
            await Task.Delay(1_200, token).ConfigureAwait(false);
            return OrderResult.Success;
        }

        private async Task RestartGame(CancellationToken token)
        {
            // Close game
            await Click(SwitchButton.B, 0_500, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await Click(SwitchButton.HOME, 0_800, token).ConfigureAwait(false);

            await Click(SwitchButton.X, 0_500, token).ConfigureAwait(false);
            await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);

            // Wait for "closing software" wheel
            await Task.Delay(1_000, token).ConfigureAwait(false);

            // Start game
            for (int i = 0; i < 15; ++i)
                await Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
        }

        private async Task EndSession(CancellationToken token)
        {
            for (int i = 0; i < 5; ++i)
                await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);

            await Task.Delay(0_500, token).ConfigureAwait(false);
            await Click(SwitchButton.MINUS, 0_500, token).ConfigureAwait(false);

            // End session or close gate or close game
            for (int i = 0; i < 5; ++i)
                await Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);

            await Task.Delay(14_000, token).ConfigureAwait(false);
        }

        private async Task EnterAirport(CancellationToken token)
        {
            await Task.Delay(0_200, token).ConfigureAwait(false);

            int tries = 0;
            while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) is OverworldState.Overworld or OverworldState.Null)
            {
                // Go into airport
                await SetStick(SwitchStick.LEFT, 20_000, 20_000, 0_400, token).ConfigureAwait(false);
                await Task.Delay(0_500, token).ConfigureAwait(false);
                await SetStick(SwitchStick.LEFT, 0, 0, 1_500, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);

                tries++;
                if (tries > 5)
                    break;
            }

            while (await DodoPosition.GetOverworldState(Config.CoordinatePointer, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Delay for animation
            await Task.Delay(1_200, token).ConfigureAwait(false);
        }

        private async Task InjectOrder(MapTerrainLite updatedMap, CancellationToken token)
        {
            // Inject order onto map
            var mapChunks = updatedMap.GenerateReturnBytes(Config.MapPullChunkSize, (uint)OffsetHelper.FieldItemStart);
            for (int i = 0; i < mapChunks.Length; ++i)
                await Connection.WriteBytesAsync(mapChunks[i].ToSend, mapChunks[i].Offset, token).ConfigureAwait(false);
        }

        private async Task CloseGate(CancellationToken token)
        {
            // Teleport to airport entry anchor  (twice, in case we get pulled back)
            await SendAnchorBytes(2, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(2, token).ConfigureAwait(false);

            // Enter airport
            await EnterAirport(token).ConfigureAwait(false);

            // Teleport to Orville (twice, in case we get pulled back)
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(3, token).ConfigureAwait(false);

            // Close gate (fail-safe without checking if open)
            await DodoPosition.CloseGate((uint)OffsetHelper.DodoAddress, token).ConfigureAwait(false);
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
                    await Task.Delay(0_500, token).ConfigureAwait(false);
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

            ulong offset = await DodoPosition.GetCoordinateAddress(Config.CoordinatePointer, token).ConfigureAwait(false);
            await Connection.WriteBytesAbsoluteAsync(anchors[index].Anchor1, offset, token).ConfigureAwait(false);
            await Connection.WriteBytesAbsoluteAsync(anchors[index].Anchor2, offset + 0x3A, token).ConfigureAwait(false);

            return true;
        }

        public async Task SendAnchorRaw(PosRotAnchor anchor, CancellationToken token)
        {
            ulong offset = await DodoPosition.GetCoordinateAddress(Config.CoordinatePointer, token).ConfigureAwait(false);
            await Connection.WriteBytesAbsoluteAsync(anchor.Anchor1, offset, token).ConfigureAwait(false);
            await Connection.WriteBytesAbsoluteAsync(anchor.Anchor2, offset + 0x3A, token).ConfigureAwait(false);
        }

        private async Task<PosRotAnchor> ReadAnchor(CancellationToken token)
        {
            ulong offset = await DodoPosition.GetCoordinateAddress(Config.CoordinatePointer, token).ConfigureAwait(false);
            var bytesA = await Connection.ReadBytesAbsoluteAsync(offset, 0xA, token).ConfigureAwait(false);
            var bytesB = await Connection.ReadBytesAbsoluteAsync(offset + 0x3A, 0x4, token).ConfigureAwait(false);
            var sequentinalAnchor = bytesA.Concat(bytesB).ToArray();
            return new PosRotAnchor(sequentinalAnchor);
        }

        private async Task<bool> IsArriverNew(CancellationToken token)
        {
            var data = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverNameLocAddress, 0x14, token).ConfigureAwait(false);
            var arriverName = System.Text.Encoding.Unicode.GetString(data).TrimEnd('\0'); // only remove null values off end
            if (arriverName != string.Empty && arriverName != LastArrival)
            {
                LogUtil.LogInfo($"{arriverName} is arriving!", Config.IP);
                LastArrival = arriverName;
                return true;
            }
            return false;
        }

        private async Task SaveDodoCodeToFile(CancellationToken token)
        {
            byte[] encodedText = Encoding.ASCII.GetBytes(DodoCode);

            using (FileStream sourceStream = new FileStream(Config.DodoModeConfig.DodoRestoreFilename,
                FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true))
            {
                await sourceStream.WriteAsync(encodedText, 0, encodedText.Length, token).ConfigureAwait(false);
            };
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
            var data = await Connection.ReadBytesAsync((uint)(InventoryOffset + shift), size, token).ConfigureAwait(false);
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

            var poke = SwitchCommand.Poke((uint)(InventoryOffset + shift), data);
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

            var poke = SwitchCommand.Poke(InventoryOffset, Item.NONE.ToBytes());
            await Connection.SendAsync(poke, token).ConfigureAwait(false);

            // Pick up and delete.
            for (int i = 0; i < count; i++)
            {
                await Click(SwitchButton.Y, 2_000, token).ConfigureAwait(false);
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
            //int index = 0;
            byte[] a1 = new byte[2]; byte[] b1 = new byte[2];
            while (!token.IsCancellationRequested)
            {
                ulong offset = await DodoPosition.GetCoordinateAddress(Config.CoordinatePointer, token).ConfigureAwait(false);
                LogUtil.LogInfo($"Pointer solved: {offset}", Config.IP);
                var bytesPos = await Connection.ReadBytesAbsoluteAsync(offset, 0xA, token).ConfigureAwait(false);
                var bytesRot = await Connection.ReadBytesAbsoluteAsync(offset + 0x3A, 0x4, token).ConfigureAwait(false);
                var state = await DodoPosition.GetOverworldState(offset, token).ConfigureAwait(false);
                LogUtil.LogInfo($"State: {state}", Config.IP);
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

                /*if (index == 0)
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
                index++;*/
            }
        }

        // Additional
        private readonly byte[] MaxTextSpeed = new byte[4] { 0, 0, 0, 3 };
        public async Task FinishConversation(int delay, CancellationToken token)
        {
            await Connection.WriteBytesAsync(MaxTextSpeed, (int)OffsetHelper.TextSpeedAddress, token).ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
    }
}
