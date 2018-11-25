using System;
using QuantConnect.Indicators;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    public class RSIStdAlphaModel : IndicatorThresholdAlphaModel<RelativeStrengthIndex, IndicatorDataPoint>
    {
        public RSIStdAlphaModel(TimeSpan resolution, int period = 20, double threshold = 0.2, double step = 0, bool inverted = false)
            : base(resolution, period, threshold, step, inverted)
        {
        }

        public override bool IsPercent { get { return false; } }
    }
}
