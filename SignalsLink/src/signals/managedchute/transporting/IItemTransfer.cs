using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    // Jeden krok pøenosu itemù (zdroj -> cíl).
    public interface IItemTransfer
    {
        // Provede jeden krok pøenosu a vrátí, kolik kusù bylo skuteènì pøesunuto.
        // Chute pak podle toho odeèítá remaining / flow.
        int TryMoveOneItem(ItemStackMoveOperation opTemplate);

        TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            int moved = TryMoveOneItem(opTemplate);
            return moved > 0 ? new TransferOperationResult(moved, moved, false) : TransferOperationResult.None;
        }
    }
}