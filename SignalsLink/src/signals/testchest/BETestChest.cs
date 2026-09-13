using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.testchest;

public class BETestChest : BlockEntityGenericTypedContainer
{
    private ITreeAttribute savedStock;
    private GuiDialogRecycler settingsDialog;
    private const int ConfigurePacket = 21401;
    public ChestStock Stock { get; private set; }
    public bool Infinite => Block?.FirstCodePart() == "bottomlesschest";
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side != EnumAppSide.Server) return;
        Stock = new ChestStock(Inventory, api.World, Infinite, () => MarkDirty());
        Stock.Read(savedStock);
        if (!Infinite) RegisterGameTickListener(dt =>
        {
            if (!Stock.Draining) return;
            Stock.Tick(dt);
            // Persist the remaining delay without sending unchanged inventory packets four times a second.
            Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
        }, 250);
    }
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        savedStock = tree.GetTreeAttribute("signalslinkStock");
        Stock?.Read(savedStock);
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var state = new TreeAttribute();
        if (Stock != null) Stock.Write(state);
        else if (savedStock != null) state = (TreeAttribute)savedStock.Clone();
        tree["signalslinkStock"] = state;
    }
    public override bool OnPlayerRightClick(IPlayer player, BlockSelection selection)
    {
        if (!Infinite && player.Entity.Controls.Sneak)
        {
            if (Api is ICoreClientAPI client)
            {
                settingsDialog?.TryClose(); settingsDialog?.Dispose();
                int seconds = savedStock?.GetInt("intervalSeconds", 5) ?? 5;
                bool half = savedStock?.GetBool("startAtHalf") ?? false;
                settingsDialog = new GuiDialogRecycler(client, seconds, half, (interval, startAtHalf) =>
                    client.Network.SendBlockEntityPacket(Pos, ConfigurePacket, new[] {(byte)interval, (byte)(startAtHalf ? 1 : 0)}));
                settingsDialog.TryOpen();
            }
            return true;
        }
        // Explicit gesture separates withdrawing an inexhaustible stack from deleting its recipe.
        if (Infinite && player.Entity.Controls.Sneak && player.Entity.Controls.Sprint
            && player.InventoryManager.ActiveHotbarSlot.Empty)
        {
            if (Api.Side == EnumAppSide.Server) Stock?.Clear();
            return true;
        }
        return base.OnPlayerRightClick(player, selection);
    }
    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        if (packetid != ConfigurePacket) { base.OnReceivedClientPacket(player, packetid, data); return; }
        if (Api.Side != EnumAppSide.Server || Infinite || Stock == null || player?.Entity == null
            || data?.Length != 2 || !ChestStock.ValidInterval(data[0]) || data[1] > 1
            || player.Entity.Pos.AsBlockPos.dimension != Pos.dimension
            || player.Entity.Pos.SquareDistanceTo(Pos.ToVec3d()) > 100
            || !Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return;
        Stock.Configure(data[0], data[1] == 1);
        MarkDirty();
    }
    protected override void OnTick(float dt)
    {
        // A supply template must not turn into rot. Recycler contents retain vanilla transitions.
        if (!Infinite) base.OnTick(dt);
    }
    public override void GetBlockInfo(IPlayer player, StringBuilder text)
    {
        base.GetBlockInfo(player, text);
        text.AppendLine(Lang.Get(Infinite ? "signalslink:bottomlesschest-help" : "signalslink:recyclerchest-help"));
    }
    public override void OnBlockRemoved() { settingsDialog?.TryClose(); settingsDialog?.Dispose(); Stock?.Dispose(); base.OnBlockRemoved(); }
    public override void OnBlockUnloaded() { settingsDialog?.TryClose(); settingsDialog?.Dispose(); Stock?.Dispose(); base.OnBlockUnloaded(); }
}
