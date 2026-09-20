namespace SignalsLink.src.signals.managedchute.transporting
{
    public readonly struct TransferOperationResult
    {
        public static readonly TransferOperationResult None = new TransferOperationResult(0m, 0, false);

        public decimal MovedAmount { get; }
        public int TriggerCost { get; }
        public bool IsLiquid { get; }
        public bool ActionPerformed { get; }
        public decimal CreditCost => IsLiquid ? MovedAmount : TriggerCost;
        public bool Success => ActionPerformed || (MovedAmount > 0m && TriggerCost > 0);
        public static TransferOperationResult ActionOnly => new TransferOperationResult(0, 1, false, true);

        public TransferOperationResult(decimal movedAmount, int triggerCost, bool isLiquid, bool actionPerformed = false)
        {
            ActionPerformed = actionPerformed;
            MovedAmount = movedAmount;
            TriggerCost = triggerCost;
            IsLiquid = isLiquid;
        }
    }
}
