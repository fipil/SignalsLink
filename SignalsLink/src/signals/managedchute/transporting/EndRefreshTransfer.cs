using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Wraps a transfer whose source is a bucket stand: after a successful move the stand is told,
    /// so its block redraws and a cleared condenser spout loses its ghost bucket on clients.
    /// </summary>
    public sealed class EndRefreshTransfer : IItemTransfer
    {
        private readonly ICoreAPI api;
        private readonly IBucketSlotEnd end;

        public IItemTransfer Inner { get; }

        public EndRefreshTransfer(ICoreAPI api, IItemTransfer inner, IBucketSlotEnd end)
        {
            this.api = api;
            Inner = inner;
            this.end = end;
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            TransferOperationResult result = Inner.TryMove(opTemplate);
            if (result.MovedAmount > 0) end.Changed(api);
            return result;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate) => (int)TryMove(opTemplate).MovedAmount;

        public void EvaluateOutputs() => Inner.EvaluateOutputs();
    }
}
