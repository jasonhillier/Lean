using System;
using System.Collections.Generic;
using QuantConnect.Algorithm.Framework.Alphas;

namespace QuantConnect.Algorithm.Framework.Portfolio
{
    public class BagHolderModel : BasePortfolioModel
    {
        public BagHolderModel()
        {
        }

        public override List<IPortfolioTarget> OpenTargetsFromInsight(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
        {
            var targets = new List<IPortfolioTarget>();
            int mag = insight.Magnitude == null ? 1 : (int)insight.Magnitude;

            targets.Add(new PortfolioTarget(symbol, 100 * mag));

            return targets;
        }
    }
}
