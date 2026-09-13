using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SignalsLink.src;
using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals.behaviours;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// Holds chunk columns loaded around itself, so that machines keep working with no player
    /// anywhere near. Which columns is the player's choice, made on the map in the anchor's dialog.
    ///
    /// It pays for the privilege in temporal gears, and the bill follows what it holds - see
    /// <see cref="AnchorCensus"/>. It also holds one spare gear in a slot, which a chute can refill
    /// and a player can take back out; when both the charge and the slot run dry the anchor lets
    /// everything go and stays down until somebody brings it a gear.
    ///
    /// The selection is saved HERE, with the block, and claimed again on every load. The claims
    /// themselves live in <see cref="ChunkAnchors"/> and are saved there too - not because the two
    /// disagree, but because the ModSystem has to know them before any chunk is loaded, including
    /// the one this block stands in.
    /// </summary>
    public class BEChunkAnchor : BlockEntity, IBlockEntityContainer, ITemporalChargeHolder, IBESignalReceptor
    {
        /// <summary>How often the bill is worked out, in in-game hours.</summary>
        private const double BillEveryHours = 1.0;

        /// <summary>How far the map reaches, in columns; also the limit on what may be claimed.</summary>
        public int MapRadius { get; private set; } = AnchorArea.WindowRadius;

        private readonly HashSet<long> columns = new HashSet<long>();

        private InventoryGeneric gearSlot;
        private BlockBehaviorTemporalCharge chargeBehavior;

        private float charge;
        private double lastBilledHours;

        /// <summary>
        /// Whether anything has been counted since this block entity came up.
        ///
        /// The census used to run only when the bill was worked out, so a freshly loaded anchor
        /// showed zero of everything for up to an in-game hour - which reads as "this is free"
        /// exactly when the player is looking to find out what it costs.
        /// </summary>
        private bool counted;

        /// <summary>
        /// What the last census found, kept for the dialog and the info panel.
        ///
        /// "Active blocks" rather than machines: this is every block that carries state of its own
        /// - chests, firepits, doors, signs, barrels, the piles on a storage yard - and calling
        /// them machines would have the player hunting for machinery that is not there.
        /// </summary>
        public int ActiveBlocks { get; private set; }
        public int Creatures { get; private set; }

        /// <summary>False once it has run out and let go. It will not claim again unaided.</summary>
        public bool Alive { get; private set; } = true;

        /// <summary>
        /// Switched off by the player. Different from being out of charge: an anchor that is off
        /// holds nothing and costs nothing, and stays that way until it is switched back on - so
        /// there is a way to stop the bill without breaking the block and losing the selection.
        /// </summary>
        public bool SwitchedOn { get; private set; } = true;

        private static SignalsLinkConfig Config => SignalsLinkConfigLoader.Current;

        // ------------------------------------------------------------------ the wake cycle

        /// <summary>Which pin is which. The node boxes come first, so these are also box indices.</summary>
        public const int InputPin = 0;
        public const int OutputPin = 1;

        /// <summary>
        /// How long between waking, in in-game hours. Zero means it never sleeps.
        ///
        /// In-game and not real time, and that is not a detail: an empty server suspends its ticks,
        /// so a real-time interval would fire nothing at all while nobody is on and then owe a
        /// hundred wake-ups at once. Everything the anchor is waiting for - a train, a factory
        /// eating its stock - runs on the same clock.
        /// </summary>
        public double WakeIntervalHours { get; private set; }

        /// <summary>
        /// How long it stays up before it looks at the pin and decides to go back down.
        ///
        /// It has to cover more than the glance: the factory's own sensors need to load and work
        /// out whether they have anything to say before the pin means anything.
        /// </summary>
        public double WakeWindowHours { get; private set; } = 0.5;

        private byte inputSignal;
        private byte outputSignal;
        private byte? lastPushedOutput;

        private double awakeSinceHours;
        private SignalNetworkMod signalMod;

        /// <summary>True while a signal on the input pin is holding it up past its window.</summary>
        public bool HeldAwake => inputSignal > 0;

        /// <summary>
        /// Also come up when somebody logs in to a server that has been standing empty.
        ///
        /// An idle server suspends its ticks, so no interval passes while nobody is on: without
        /// this, the factory a player left running last night is still asleep when they come back.
        /// </summary>
        public bool WakeOnPlayerJoin { get; private set; } = true;

        /// <summary>The columns this anchor is set to hold.</summary>
        public IReadOnlyCollection<long> Columns => columns;

        public IInventory Inventory => gearSlot;

        public string InventoryClassName => "signalslinkanchor";

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            MapRadius = Block?.Attributes?["mapRadius"].AsInt(AnchorArea.WindowRadius)
                ?? AnchorArea.WindowRadius;

            chargeBehavior = Block?.GetBehavior<BlockBehaviorTemporalCharge>();

            gearSlot ??= NewGearSlot(api);
            gearSlot.LateInitialize(InventoryClassName + "-" + Pos, api);

            if (api is not ICoreServerAPI) return;

            // A newly placed anchor holds the ground it stands on and nothing else, so that the
            // first thing it costs is nothing and the player goes to the map to decide the rest.
            if (columns.Count == 0)
            {
                (int cx, int cz) = Anchors?.ColumnAt(Pos) ?? (0, 0);
                foreach (long key in AnchorArea.JustTheAnchor(cx, cz)) columns.Add(key);
            }

            lastBilledHours = api.World.Calendar.TotalHours;
            awakeSinceHours = lastBilledHours;

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod?.RegisterSignalTickListener(PushOutput);

            // Nothing is held for free. A freshly placed anchor has no charge, so it holds nothing
            // at all until somebody feeds it - it used to hold everything until the first hourly
            // bill caught up with it, which is an hour of ground kept loaded for nothing.
            if (SwitchedOn && charge <= 0) TakeSpareGear();

            Alive = SwitchedOn && charge > 0;

            if (Alive) Anchors?.SetColumns(Pos, columns);

            RegisterGameTickListener(OnTick, 5000);
        }

        private InventoryGeneric NewGearSlot(ICoreAPI api)
        {
            // One slot, and one is enough: the charge itself is the reserve, so a chute has the
            // whole life of a gear to bring the next one.
            return new InventoryGeneric(1, InventoryClassName + "-" + Pos, api,
                (id, inv) => new ItemSlotGear(inv, chargeBehavior?.ChargeItemCode ?? "gear-temporal"));
        }

        // ------------------------------------------------------------------ the bill

        private void OnTick(float dt)
        {
            if (Api is not ICoreServerAPI) return;

            // First count as soon as there is something to count. Deliberately on a tick and not
            // in Initialize: the held columns are still being force-loaded at that moment, so
            // counting there would report whatever happened to have arrived.
            if (!counted) { Census(); counted = true; ShowState(); MarkDirty(); }

            if (!SwitchedOn) return;

            double now = Api.World.Calendar.TotalHours;
            double elapsed = now - lastBilledHours;

            // The BILL is worked out by the hour - a smaller step would be arithmetic noise. The
            // wake window and the output pin are NOT: a fifteen-minute window checked once an hour
            // is not a fifteen-minute window, and a charge readout an hour behind is no use to a
            // chute that is meant to notice the anchor running low.
            ReportCharge();
            MaybeSleep(now);

            // A jump means the world was away, not that a century of rent is due.
            if (elapsed < 0 || elapsed > 24) { lastBilledHours = now; return; }
            if (elapsed < BillEveryHours) return;

            lastBilledHours = now;

            Census();

            // Standing next to it costs nothing: those chunks would be loaded anyway, so the anchor
            // is doing no work and has nothing to charge for.
            if (PlayerNear()) { MarkDirty(); return; }

            Spend((float)elapsed);
        }

        // ------------------------------------------------------------------ pins

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != InputPin || inputSignal == value) return;

            inputSignal = value;

            // A signal arriving is also a reason to come back: it means whatever the anchor was
            // waiting for has happened, and waiting out the rest of the interval would waste it.
            if (value > 0) Anchors?.WakeNow(Pos);

            MarkDirty();
        }

        /// <summary>The output reports how full the anchor is, 0-15, so a chute can keep it fed.</summary>
        private void ReportCharge()
        {
            byte level = (byte)(ChargePercent * 15 / 100);

            if (level > 15) level = 15;
            if (outputSignal == level) return;

            outputSignal = level;
            MarkDirty();
        }

        private void PushOutput()
        {
            if (lastPushedOutput == outputSignal) return;

            BEBehaviorSignalConnector connector = GetBehavior<BEBehaviorSignalConnector>();
            ISignalNode node = connector?.GetNodeAt(new NodePos(Pos, OutputPin));
            if (node == null) return;

            signalMod.netManager.UpdateSource(node, outputSignal);
            lastPushedOutput = outputSignal;
        }

        /// <summary>
        /// Puts it down for its interval once the window has run out - unless the input pin is
        /// holding it up, which is the whole point of that pin: a factory that still has work says
        /// so, and the anchor stays with it.
        /// </summary>
        private void MaybeSleep(double now)
        {
            if (WakeIntervalHours <= 0 || !Alive || HeldAwake) return;
            if (now - awakeSinceHours < WakeWindowHours) return;

            Alive = false;
            ShowState();

            Anchors?.Sleep(Pos, columns, now + WakeIntervalHours, WakeOnPlayerJoin);
        }

        /// <summary>What the player picked in the dialog.</summary>
        public void SetWakeCycle(double intervalHours, double windowHours, bool onPlayerJoin)
        {
            if (Api is not ICoreServerAPI) return;

            WakeIntervalHours = intervalHours < 0 ? 0 : intervalHours;
            WakeWindowHours = windowHours < 0.05 ? 0.05 : windowHours;
            WakeOnPlayerJoin = onPlayerJoin;

            awakeSinceHours = Api.World.Calendar.TotalHours;

            MarkDirty();
        }

        private void Spend(float hours)
        {
            if (chargeBehavior == null) return;

            // Only while it is actually holding. An anchor that has let go - asleep, or out of
            // charge - pays nothing, and it can still be ticking at that moment: its own column is
            // released but the chunk lingers until the last player walks out of view, and longer
            // still if another anchor holds the same ground.
            if (Alive)
            {
                float perHour = chargeBehavior.GearTotalCharge / (100f * 24f)
                    * (GetOperationalVolume() / chargeBehavior.ReferenceVolume)
                    * chargeBehavior.BaseConsumptionFactor;

                charge -= perHour * hours;
            }

            while (charge <= 0 && TakeSpareGear()) { }

            if (charge <= 0)
            {
                charge = 0;
                Die();
                return;
            }

            if (!Alive) Revive();

            MarkDirty();
        }

        /// <summary>
        /// Takes a gear out of the player's hand. Straight into the charge when the anchor is down,
        /// otherwise into the slot as the spare - so one click revives it and the next stocks it.
        /// </summary>
        public bool TryFeed(ItemSlot from)
        {
            if (Api is not ICoreServerAPI || chargeBehavior == null) return false;

            string path = from?.Itemstack?.Collectible?.Code?.Path;
            if (path == null || !path.Contains(chargeBehavior.ChargeItemCode)) return false;

            if (charge <= 0)
            {
                from.TakeOut(1);
                from.MarkDirty();

                charge = chargeBehavior.GearTotalCharge;
                counted = false;
                Revive();

                return true;
            }

            ItemSlot spare = gearSlot?[0];
            if (spare == null || !spare.Empty) return false;

            from.TryPutInto(Api.World, spare, 1);
            spare.MarkDirty();
            MarkDirty();

            return true;
        }

        /// <summary>Swallows the spare gear, if there is one. The slot is then free to be refilled.</summary>
        private bool TakeSpareGear()
        {
            ItemSlot slot = gearSlot?[0];
            if (slot == null || slot.Empty || chargeBehavior == null) return false;

            slot.TakeOut(1);
            slot.MarkDirty();

            charge += chargeBehavior.GearTotalCharge;

            return true;
        }

        /// <summary>
        /// Swaps the block for the variant that matches what it is doing, so a glance across the
        /// yard says whether an anchor is holding or lying idle.
        ///
        /// An exchange rather than a mesh swap: the block entity, its inventory and its selection
        /// boxes all survive it, and the signal pins keep the positions Signals reads out of the
        /// block's attributes. A second variant group costs nothing here because the block is not
        /// in the creative inventory anyway.
        /// </summary>
        private void ShowState()
        {
            if (Api is not ICoreServerAPI) return;

            // Read the block out of the WORLD, not from this entity's own Block property. That
            // property is set when the entity comes up and ExchangeBlock does not refresh it, so
            // after the first swap it describes a block that is no longer there and every later
            // comparison is made against the wrong variant.
            Block standing = Api.World.BlockAccessor.GetBlock(Pos);
            if (standing?.Code == null) return;

            string wanted = Alive ? "on" : "off";
            if (standing.Variant["state"] == wanted) return;

            Block swapped = Api.World.GetBlock(standing.CodeWithVariant("state", wanted));
            if (swapped == null) return;

            Api.World.BlockAccessor.ExchangeBlock(swapped.BlockId, Pos);
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }

        private void Die()
        {
            if (!Alive) return;

            Alive = false;
            Anchors?.Release(Pos);
            ShowState();

            Api.World.Logger.Notification("[SignalsLink] anchor dead at " + Pos
                + "; out of charge and no spare gear, everything let go.");

            MarkDirty();
        }

        private void Revive()
        {
            if (!SwitchedOn) return;

            // A sleeping anchor is down on purpose and its wake-up is the scheduler's business.
            // Without this it would stand straight back up on the first tick after going to sleep,
            // because from here "not alive but has charge" looks exactly like "ready to work".
            if (Anchors?.IsAsleep(Pos) == true) return;

            Alive = true;
            Anchors?.SetColumns(Pos, columns);
            ShowState();

            MarkDirty();
        }

        /// <summary>
        /// Counts what is being kept alive. Walks the held columns rather than a box around the
        /// anchor, because those are exactly the chunks being paid for.
        /// </summary>
        private void Census()
        {
            ActiveBlocks = 0;
            Creatures = 0;

            if (Api is not ICoreServerAPI sapi) return;

            int size = GlobalConstants.ChunkSize;
            int levels = sapi.WorldManager.MapSizeY / size;

            foreach (long key in columns)
            {
                (int cx, int cz) = AnchorArea.Of(key);

                for (int cy = 0; cy < levels; cy++)
                {
                    IWorldChunk chunk = sapi.WorldManager.GetChunk(cx, cy, cz);
                    if (chunk == null) continue;

                    if (chunk.BlockEntities != null) ActiveBlocks += chunk.BlockEntities.Count;

                    if (chunk.Entities == null) continue;

                    for (int i = 0; i < chunk.EntitiesCount; i++)
                    {
                        if (IsChargeable(chunk.Entities[i])) Creatures++;
                    }
                }
            }
        }

        /// <summary>
        /// Creatures only. Dropped items and projectiles are transient and not the player's doing,
        /// and charging for them would be a tax on untidiness rather than on load.
        /// </summary>
        private static bool IsChargeable(Entity entity)
        {
            // Not the player. It is their world, and the ground they are standing on would be
            // loaded whether the anchor paid for it or not.
            return entity is EntityAgent && entity is not EntityPlayer && entity.Alive;
        }

        private bool PlayerNear()
        {
            if (Api?.World?.AllOnlinePlayers == null) return false;

            double reach = (MapRadius + 1) * GlobalConstants.ChunkSize;

            foreach (IPlayer player in Api.World.AllOnlinePlayers)
            {
                EntityPos at = player?.Entity?.Pos;
                if (at == null) continue;

                if (Math.Abs(at.X - Pos.X) <= reach && Math.Abs(at.Z - Pos.Z) <= reach) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ charge holder

        public float GetCurrentCharge() => charge;

        public void SetCurrentCharge(float value)
        {
            charge = value;

            if (charge > 0 && !Alive) Revive();

            MarkDirty();
        }

        /// <summary>
        /// The census as a "volume", which is the only thing the shared charge behaviour knows how
        /// to bill. All of the anchor's own pricing lives in <see cref="AnchorCensus"/>.
        /// </summary>
        public float GetOperationalVolume()
        {
            float reference = chargeBehavior?.ReferenceVolume ?? 100f;

            return AnchorCensus.EffectiveVolume(
                AnchorCensus.Units(ActiveBlocks, Creatures, columns.Count, Config.AnchorCreatureWeight, Config.AnchorColumnWeight),
                reference, Config.AnchorReferenceLoad, Config.AnchorPriceExponent);
        }

        // ------------------------------------------------------------------ the map

        /// <summary>
        /// What the player picked on the map. Whatever arrives is cut down to the window first -
        /// see <see cref="AnchorArea.Sanitise"/>; a packet is not to be believed.
        /// </summary>
        public void SetColumns(IEnumerable<long> wanted)
        {
            if (Api is not ICoreServerAPI) return;

            (int cx, int cz) = Anchors?.ColumnAt(Pos) ?? (0, 0);

            HashSet<long> clean = AnchorArea.Sanitise(wanted, cx, cz, MapRadius, Config.AnchorMaxColumns);

            columns.Clear();
            foreach (long key in clean) columns.Add(key);

            if (Alive) Anchors?.SetColumns(Pos, columns);

            // Straight away, not at the next hourly bill: the player has just changed the
            // selection and is looking at the block info to see what it did.
            Census();

            MarkDirty();
        }

        /// <summary>
        /// Turns the anchor on or off. Off lets everything go at once - waiting for the hourly bill
        /// would keep charging for ground the player has just said they no longer want held.
        /// </summary>
        public void SetSwitchedOn(bool on)
        {
            if (Api is not ICoreServerAPI || on == SwitchedOn) return;

            SwitchedOn = on;

            if (on)
            {
                if (charge <= 0) TakeSpareGear();
                if (charge > 0) Revive();
            }
            else
            {
                Alive = false;
                Anchors?.Release(Pos);
            }

            ShowState();
            MarkDirty();
        }

        public const int PacketIdSetSwitch = 1044;

        /// <summary>How full the anchor is, 0-100, for the dialog.</summary>
        public int ChargePercent => chargeBehavior == null || chargeBehavior.GearTotalCharge <= 0
            ? 0 : (int)(charge / chargeBehavior.GearTotalCharge * 100f);

        /// <summary>How long one gear lasts at the present census, in in-game days.</summary>
        public double DaysPerGear()
        {
            if (chargeBehavior == null) return double.PositiveInfinity;

            return AnchorCensus.DaysPerGear(
                AnchorCensus.Units(ActiveBlocks, Creatures, columns.Count, Config.AnchorCreatureWeight, Config.AnchorColumnWeight),
                chargeBehavior.GearTotalCharge, chargeBehavior.ReferenceVolume,
                chargeBehavior.BaseConsumptionFactor,
                Config.AnchorReferenceLoad, Config.AnchorPriceExponent);
        }

        /// <summary>Opens the map the columns are picked on. Client side only.</summary>
        public void OpenMap()
        {
            if (Api is not ICoreClientAPI capi) return;

            // The slot has to be open for the player before its grid will accept anything.
            capi.World.Player.InventoryManager.OpenInventory(gearSlot);

            new GuiDialogChunkAnchor(capi, Pos, GlobalConstants.ChunkSize, columns, MapRadius,
                SwitchedOn, Config.AnchorMaxColumns,
                SendColumnsToServer, SendSwitchToServer, SendCycleToServer, this).TryOpen();
        }

        public const int PacketIdSetColumns = 1043;

        private void SendColumnsToServer(IReadOnlyCollection<long> wanted)
        {
            if (Api is not ICoreClientAPI capi) return;

            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);

            writer.Write(wanted.Count);
            foreach (long key in wanted) writer.Write(key);

            capi.Network.SendBlockEntityPacket(Pos, PacketIdSetColumns, stream.ToArray());
        }

        public const int PacketIdSetCycle = 1046;

        private void SendCycleToServer(double interval, double window, bool onPlayerJoin)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);

            writer.Write(interval);
            writer.Write(window);
            writer.Write(onPlayerJoin);

            (Api as ICoreClientAPI)?.Network.SendBlockEntityPacket(Pos, PacketIdSetCycle, stream.ToArray());
        }

        private void SendSwitchToServer(bool on)
        {
            (Api as ICoreClientAPI)?.Network.SendBlockEntityPacket(Pos, PacketIdSetSwitch,
                new[] { (byte)(on ? 1 : 0) });
        }

        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            // Everything under 1000 is the inventory talking to itself - the slot grid's own
            // traffic, with ids the grid chose. 1001 is the dialog saying it has closed.
            if (packetid < 1000)
            {
                gearSlot?.InvNetworkUtil?.HandleClientPacket(fromPlayer, packetid, data);
                Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
                return;
            }

            if (packetid == PacketIdSetCycle)
            {
                using MemoryStream cycle = new MemoryStream(data);
                using BinaryReader read = new BinaryReader(cycle);

                SetWakeCycle(read.ReadDouble(), read.ReadDouble(), read.ReadBoolean());
                return;
            }

            if (packetid == (int)EnumBlockEntityPacketId.Close)
            {
                fromPlayer?.InventoryManager?.CloseInventory(gearSlot);
                return;
            }

            if (packetid == PacketIdSetSwitch)
            {
                SetSwitchedOn(data != null && data.Length > 0 && data[0] != 0);
                return;
            }

            if (packetid != PacketIdSetColumns)
            {
                base.OnReceivedClientPacket(fromPlayer, packetid, data);
                return;
            }

            using MemoryStream stream = new MemoryStream(data);
            using BinaryReader reader = new BinaryReader(stream);

            int count = reader.ReadInt32();
            List<long> wanted = new List<long>(count);

            for (int i = 0; i < count; i++) wanted.Add(reader.ReadInt64());

            SetColumns(wanted);
        }

        // ------------------------------------------------------------------ housekeeping

        /// <summary>
        /// Broken by a player. Unloading is deliberately NOT done when the block entity merely
        /// unloads - that happens on shutdown, and there is nothing left to hold by then.
        /// </summary>
        public override void OnBlockRemoved()
        {
            signalMod?.DisposeSignalTickListener(PushOutput);
            Anchors?.Release(Pos);

            base.OnBlockRemoved();
        }

        /// <summary>The spare gear comes back out; losing it to a pickaxe would be a quiet theft.</summary>
        public void DropContents(Vec3d atPos)
        {
            gearSlot?.DropAll(atPos ?? Pos.ToVec3d());
        }

        /// <summary>Nothing here rots away on its own, so there is never anything to notice.</summary>
        public void CheckInventoryClearedMidTick()
        {
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            DropContents(Pos.ToVec3d().Add(0.5, 0.5, 0.5));

            base.OnBlockBroken(byPlayer);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            columns.Clear();

            if (tree["columns"] is LongArrayAttribute saved && saved.value != null)
            {
                foreach (long key in saved.value) columns.Add(key);
            }

            WakeIntervalHours = tree.GetDouble("wakeinterval", 0);
            WakeOnPlayerJoin = tree.GetBool("wakeonjoin", true);
            WakeWindowHours = tree.GetDouble("wakewindow", 0.5);
            inputSignal = (byte)tree.GetInt("inputsignal", 0);
            outputSignal = (byte)tree.GetInt("outputsignal", 0);

            charge = tree.GetFloat("charge", 0f);
            Alive = tree.GetBool("alive", true);
            SwitchedOn = tree.GetBool("switchedon", true);
            ActiveBlocks = tree.GetInt("activeblocks", 0);
            Creatures = tree.GetInt("creatures", 0);

            gearSlot ??= NewGearSlot(worldForResolving?.Api);
            gearSlot.FromTreeAttributes(tree.GetTreeAttribute("gear") ?? new TreeAttribute());
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            long[] keys = new long[columns.Count];
            columns.CopyTo(keys);

            tree["columns"] = new LongArrayAttribute(keys);

            tree.SetDouble("wakeinterval", WakeIntervalHours);
            tree.SetBool("wakeonjoin", WakeOnPlayerJoin);
            tree.SetDouble("wakewindow", WakeWindowHours);
            tree.SetInt("inputsignal", inputSignal);
            tree.SetInt("outputsignal", outputSignal);

            tree.SetFloat("charge", charge);
            tree.SetBool("alive", Alive);
            tree.SetBool("switchedon", SwitchedOn);
            tree.SetInt("activeblocks", ActiveBlocks);
            tree.SetInt("creatures", Creatures);

            TreeAttribute gear = new TreeAttribute();
            gearSlot?.ToTreeAttributes(gear);
            tree["gear"] = gear;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);

            sb.AppendLine(Lang.Get("signalslink:chunkanchor-holding", columns.Count));

            // The price is shown WHATEVER state it is in. It used to be hidden while the anchor was
            // out of charge, which is precisely the moment the player is deciding whether feeding
            // it is worth a gear.
            float units = AnchorCensus.Units(ActiveBlocks, Creatures, columns.Count, Config.AnchorCreatureWeight, Config.AnchorColumnWeight);

            sb.AppendLine(Lang.Get("signalslink:chunkanchor-census", ActiveBlocks, Creatures, (int)units));

            if (chargeBehavior != null)
            {
                double days = AnchorCensus.DaysPerGear(units, chargeBehavior.GearTotalCharge,
                    chargeBehavior.ReferenceVolume, chargeBehavior.BaseConsumptionFactor,
                    Config.AnchorReferenceLoad, Config.AnchorPriceExponent);

                sb.AppendLine(double.IsInfinity(days)
                    ? Lang.Get("signalslink:chunkanchor-free")
                    : Lang.Get("signalslink:chunkanchor-rate", days.ToString("0.#")));

                sb.AppendLine(Lang.Get("signalslink:chunkanchor-charge",
                    (int)(charge / chargeBehavior.GearTotalCharge * 100f)));

                sb.AppendLine(Lang.Get("signalslink:chunkanchor-spare",
                    gearSlot?[0]?.Empty == false ? 1 : 0));
            }

            if (!SwitchedOn)
            {
                sb.AppendLine(Lang.Get("signalslink:chunkanchor-off"));
                return;
            }

            double? wakes = Anchors?.WakesAt(Pos);

            if (wakes != null)
            {
                sb.AppendLine(Lang.Get("signalslink:chunkanchor-asleep",
                    (wakes.Value - Api.World.Calendar.TotalHours).ToString("0.#")));
            }
            else if (!Alive)
            {
                sb.AppendLine(Lang.Get("signalslink:chunkanchor-dead"));
            }
            else if (HeldAwake)
            {
                sb.AppendLine(Lang.Get("signalslink:chunkanchor-heldawake"));
            }
        }

        private ChunkAnchors Anchors => Api?.ModLoader?.GetModSystem<ChunkAnchors>();
    }

    /// <summary>A slot that only takes temporal gears. Anything may be taken back out of it.</summary>
    public class ItemSlotGear : ItemSlot
    {
        private readonly string code;

        public ItemSlotGear(InventoryBase inventory, string code) : base(inventory)
        {
            this.code = code;
        }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            string path = sourceSlot?.Itemstack?.Collectible?.Code?.Path;

            return path != null && path.Contains(code) && base.CanHold(sourceSlot);
        }
    }
}
