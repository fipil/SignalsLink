using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SignalsLink.src.signals.blocksensor.scanners
{
    public class ConditionalScanner
    {
        public static readonly byte USE_CONDITIONAL_SIGNAL = 14;

        protected PaperConditionsEvaluator conditionsEvaluator;

        public ConditionalScanner(PaperConditionsEvaluator conditionsEvaluator)
        {
            this.conditionsEvaluator = conditionsEvaluator;
        }
    }
}
