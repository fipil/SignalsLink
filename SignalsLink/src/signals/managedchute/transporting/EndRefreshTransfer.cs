using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Wraps a transfer with an end that must be told after a move (a bucket stand, a registered
    /// end): the block then persists and redraws, a cleared condenser spout loses its ghost bucket.
    /// </summary>
    public sealed class EndRefreshTransfer : IItemTransfer
    {
        private readonly ICoreAPI api;
        private readonly ITransferEnd end;

        public IItemTransfer Inner { get; }

        public EndRefreshTransfer(ICoreAPI api, IItemTransfer inner, ITransferEnd end)
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
