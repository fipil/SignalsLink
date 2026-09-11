using System.Text;
using SignalsLink.src.signals.link;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.sleeve;

/// <summary>Only room sealing is stored here; the coupling has no inventory or transfer tick.</summary>
public class BESleeveWallCoupling : BlockEntity, IRetentionModeHost
{
    private int retentionMode = link.RetentionMode.Cooling;
    public bool SupportsRetentionMode => true;
    public int RetentionMode => retentionMode;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Publish();
    }

    public void CycleRetentionMode()
    {
        if (Api?.Side != EnumAppSide.Server) return;
        retentionMode = link.RetentionMode.Next(retentionMode);
        Publish();
        MarkDirty(true);
        Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
    }

    private void Publish() => Api?.ModLoader.GetModSystem<RetentionModeRegistry>()?.Publish(Pos, retentionMode);
    private void Withdraw() => Api?.ModLoader.GetModSystem<RetentionModeRegistry>()?.Withdraw(Pos);

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        int saved = tree.GetInt("retentionMode", link.RetentionMode.Cooling);
        retentionMode = saved >= 0 && saved < link.RetentionMode.Count ? saved : link.RetentionMode.Cooling;
        Publish();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("retentionMode", retentionMode);
    }

    public override void GetBlockInfo(IPlayer player, StringBuilder text)
    {
        base.GetBlockInfo(player, text);
        text.AppendLine(Lang.Get("signalslink:retention-label", Lang.Get(link.RetentionMode.LangKey(retentionMode))));
    }

    public override void OnBlockRemoved() { Withdraw(); base.OnBlockRemoved(); }
    public override void OnBlockUnloaded() { Withdraw(); base.OnBlockUnloaded(); }
}