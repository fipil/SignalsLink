using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public class InventorySourcedTransferBase
    {
        protected readonly IInventory sourceInv;
        protected readonly byte inputSlotSignal;

        public InventorySourcedTransferBase(IInventory sourceInv, byte inputSlotSignal)
        {
            this.sourceInv = sourceInv;
            this.inputSlotSignal = inputSlotSignal;
        }

        protected ItemSlot GetSourceSlot()
        {
            // 3) Konkrétní slot: 1–14 -> index (signal-1)
            if (inputSlotSignal > 0 && inputSlotSignal < 15)
            {
                int index = inputSlotSignal - 1;
                if (index >= 0 && index < sourceInv.Count)
                {
                    return sourceInv[index];
                }
                return null;
            }

            // 2) 15 -> vždy POSLEDNÍ slot inventáře
            if (inputSlotSignal == 15)
            {
                if (sourceInv.Count == 0) return null;
                return sourceInv[sourceInv.Count - 1];
            }

            // 1) 0 -> původní chování: „vysávej všechny sloty“ = první NEprázdný slot
            for (int i = 0; i < sourceInv.Count; i++)
            {
                if (!sourceInv[i].Empty)
                {
                    return sourceInv[i];
                }
            }

            // nic nenalezeno
            return null;
        }


    }
}
