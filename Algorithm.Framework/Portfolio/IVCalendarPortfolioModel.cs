/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Securities;
using QuantConnect.Securities.Option;

namespace QuantConnect.Algorithm.Framework.Portfolio
{
    /// <summary>
    /// Shorts front month (higher-IV), longs next month (lower-IV)
    /// </summary>
    public class IVCalendarPortfolioModel : SyntheticPortfolioModel
    {
		public override List<IPortfolioTarget> PossiblyCloseCurrentTargets(QCAlgorithmFramework algorithm, Symbol symbol, IEnumerable<OptionHolding> holdings, Insight insight)
		{
			if (insight.Direction == InsightDirection.Down)
			{
				return this.LiquidateOptions(algorithm, holdings);
			}

			return null;
		}

		public override List<IPortfolioTarget> FindSyntheticTargets(QCAlgorithmFramework algorithm, Symbol symbol, bool isLong, int mag = 1, int expiryDistance = 0)
        {
            var targets = new List<IPortfolioTarget>();

			if (!isLong) return targets;

            //short BOTH front-month
            var synthetic = this.GetATM(algorithm, symbol, expiryDistance);
			if (synthetic == null)
			{
				algorithm.Log("No front-month options found!");
				return new List<IPortfolioTarget>(); //abort/not-found
			}

            targets.Add(new PortfolioTarget(synthetic.Item1.Symbol, -this.PositionSize * mag));
            targets.Add(new PortfolioTarget(synthetic.Item2.Symbol, -this.PositionSize * mag));

            //long BOTH back month
            synthetic = this.GetATM(algorithm, symbol, expiryDistance+1);
            if (synthetic == null)
			{
				algorithm.Log("No back-month options found!");
				return new List<IPortfolioTarget>(); //abort/not-found
			}

            targets.Add(new PortfolioTarget(synthetic.Item1.Symbol, this.PositionSize * mag));
            targets.Add(new PortfolioTarget(synthetic.Item2.Symbol, this.PositionSize * mag));

            return targets;
        }

		protected override List<IPortfolioTarget> IncrementOptionPositions(QCAlgorithmFramework algorithm, Symbol baseSymbol, IEnumerable<OptionHolding> holdings, Insight insight)
		{
			return null;
		}

	}
}