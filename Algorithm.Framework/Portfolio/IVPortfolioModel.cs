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
    /// Goes long or short volatility (i.e. ATM options)
	///  - can be used to create straddle, strangle
    /// </summary>
    public class IVPortfolioModel : BaseOptionPortfolioModel
    {
		public int spread { get; set; }

		/// <summary>
		/// When a new insight comes in, close down anything that might be open.
		/// </summary>
        public override List<IPortfolioTarget> PossiblyCloseCurrentTargets(QCAlgorithmFramework algorithm, Symbol symbol, IEnumerable<OptionHolding> optionHoldings, Insight insight)
        {
			bool shouldBeLong = insight.Direction == InsightDirection.Up;
			bool liquidate = false;

			optionHoldings.All(p =>
            {
                if (shouldBeLong && p.Quantity < 0)
				{
					liquidate = true;
				}
				else if (!shouldBeLong && p.Quantity > 0)
				{
					liquidate = true;
				}

				return true;
			});

			if (liquidate)
			{
				return this.LiquidateOptions(algorithm, optionHoldings);
			}

			return null;
        }

		public override List<IPortfolioTarget> FindPotentialOptions(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
        {
			var targets = new List<IPortfolioTarget>();

			if (insight.Direction == InsightDirection.Flat) return targets;

            int mag = (int)(insight.Magnitude == null ? 1 : Math.Round((decimal)insight.Magnitude));
			mag = insight.Direction == InsightDirection.Up ? mag : -mag;

			Tuple<OptionContract, OptionContract> contracts = null;

			if (this.spread == 0)
			{
				contracts = this.GetATM(algorithm, symbol, 0);
			}
			else
			{
				contracts = this.GetOTMSpread(algorithm, symbol, this.spread);
			}

			if (contracts != null)
			{
				targets.Add(new PortfolioTarget(contracts.Item1.Symbol, this.PositionSize * mag));
				targets.Add(new PortfolioTarget(contracts.Item2.Symbol, this.PositionSize * mag));
			}

			return targets;

		}
    }
}