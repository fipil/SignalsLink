using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SignalsLink.src.signals.behaviours
{
    public interface ITemporalChargeHolder
    {
        float GetCurrentCharge();
        void SetCurrentCharge(float charge);
        float GetOperationalVolume(); // Pro výpočet spotřeby
    }
}
