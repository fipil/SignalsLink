using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SignalsLink.src;
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
    public class BEChunkAnchor : BlockEntity, IBlockEntityContainer, ITemporalChargeHolder
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
            if (!counted) { Census(); counted = true; MarkDirty(); }

            if (!SwitchedOn) return;

            double now = Api.World.Calendar.TotalHours;
            double elapsed = now - lastBilledHours;

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

        private void Spend(float hours)
        {
            if (chargeBehavior == null) return;

            float perHour = chargeBehavior.GearTotalCharge / (100f * 24f)
                * (GetOperationalVolume() / chargeBehavior.ReferenceVolume)
                * chargeBehavior.BaseConsumptionFactor;

            charge -= perHour * hours;

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

        private void Die()
        {
            if (!Alive) return;

            Alive = false;
            Anchors?.Release(Pos);

            Api.World.Logger.Notification("[SignalsLink] anchor at " + Pos
                + " ran out of charge and let its columns go.");

            MarkDirty();
        }

        private void Revive()
        {
            if (!SwitchedOn) return;

            Alive = true;
            Anchors?.SetColumns(Pos, columns);

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
                AnchorCensus.Units(ActiveBlocks, Creatures, Config.AnchorCreatureWeight),
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

            HashSet<long> clean = AnchorArea.Sanitise(wanted, cx, cz, MapRadius);

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

            MarkDirty();
        }

        public const int PacketIdSetSwitch = 1044;

        /// <summary>Vanilla's own inventory traffic, so the slot behaves like any other.</summary>
        public const int PacketIdSlot = 1045;

        /// <summary>How long one gear lasts at the present census, in in-game days.</summary>
        public double DaysPerGear()
        {
            if (chargeBehavior == null) return double.PositiveInfinity;

            return AnchorCensus.DaysPerGear(
                AnchorCensus.Units(ActiveBlocks, Creatures, Config.AnchorCreatureWeight),
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
                SwitchedOn, SendColumnsToServer, SendSwitchToServer, this).TryOpen();
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

        private void SendSwitchToServer(bool on)
        {
            (Api as ICoreClientAPI)?.Network.SendBlockEntityPacket(Pos, PacketIdSetSwitch,
                new[] { (byte)(on ? 1 : 0) });
        }

        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            if (packetid == PacketIdSlot)
            {
                gearSlot?.InvNetworkUtil?.HandleClientPacket(fromPlayer, packetid, data);
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
            float units = AnchorCensus.Units(ActiveBlocks, Creatures, Config.AnchorCreatureWeight);

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

            if (!SwitchedOn) sb.AppendLine(Lang.Get("signalslink:chunkanchor-off"));
            else if (!Alive) sb.AppendLine(Lang.Get("signalslink:chunkanchor-dead"));
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
