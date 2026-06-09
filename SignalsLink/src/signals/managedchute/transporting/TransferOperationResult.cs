namespace SignalsLink.src.signals.managedchute.transporting
{
    public readonly struct TransferOperationResult
    {
        public static readonly TransferOperationResult None = new TransferOperationResult(0m, 0, false);

        public decimal MovedAmount { get; }
        public int TriggerCost { get; }
        public bool IsLiquid { get; }
        public bool Success => MovedAmount > 0m && TriggerCost > 0;

        public TransferOperationResult(decimal movedAmount, int triggerCost, bool isLiquid)
        {
            MovedAmount = movedAmount;
            TriggerCost = triggerCost;
            IsLiquid = isLiquid;
        }
    }
}
