using System;
using QuantConnect.Indicators;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    public class ROCStdAlphaModel : IndicatorThresholdAlphaModel<RateOfChange,IndicatorDataPoint>
    {
        public ROCStdAlphaModel(TimeSpan resolution, int period = 20, double threshold = 0.2, double step = 0, bool inverted = false)
            : base(resolution, period, threshold, step, inverted)
        {
        }

        public override bool IsPercent { get { return true; } }
    }
}
