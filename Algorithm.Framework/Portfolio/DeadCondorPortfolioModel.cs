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
    /// Build an iron condor as price moves around
    /// </summary>
    public class DeadCondorPortfolioModel : BaseOptionPortfolioModel
    {
        private readonly int _distance;
        private readonly int _spread;

        public DeadCondorPortfolioModel()
			: this(4,2,2,30)
		{
		}

        public DeadCondorPortfolioModel(int distance, int spread, int MinDaysRemaining, int MaxDaysRemaining)
		{
            _distance = distance;
			_spread = spread;

			//TODO: strike selector?
			this._OptionFilter = (o) => {
				return o.Expiration(TimeSpan.FromDays(MinDaysRemaining), TimeSpan.FromDays(MaxDaysRemaining));
			};
		}

		/// <summary>
		/// When a new insight comes in, close down anything that might be open.
		/// </summary>
		public override List<IPortfolioTarget> PossiblyCloseCurrentTargets(QCAlgorithmFramework algorithm, Symbol symbol, IEnumerable<OptionHolding> optionHoldings, Insight insight)
		{
			//check to see which direction our existing holdings are,
			// and filter out any insights that would duplicate that leg.
			InsightDirection direction = insight.Direction;
			foreach (var holding in optionHoldings)
			{
				if (holding.Symbol.ID.OptionRight == OptionRight.Put)
				{
					if (direction == InsightDirection.Up)
					{
						//abort -- don't duplicate leg!!
						return null;
					}

					direction = InsightDirection.Down;
				}
				if (holding.Symbol.ID.OptionRight == OptionRight.Call)
				{
					if (direction == InsightDirection.Down)
					{
						//abort -- don't duplicate leg!!
						return null;
					}

					direction = InsightDirection.Up;
				}
			}

			return FindPotentialOptions(algorithm, symbol, direction);
		}

		protected virtual List<IPortfolioTarget> IncrementOptionPositions(QCAlgorithmFramework algorithm, Symbol baseSymbol, IEnumerable<OptionHolding> holdings, Insight insight)
		{
			return null;
		}

		/// <summary>
		/// Establish one leg of an iron condor according to the insight.
		/// </summary>
		public override List<IPortfolioTarget> FindPotentialOptions(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
		{
			return FindPotentialOptions(algorithm, symbol, insight.Direction);
		}

		public List<IPortfolioTarget> FindPotentialOptions(QCAlgorithmFramework algorithm, Symbol symbol, InsightDirection direction)
        {
			if (direction == InsightDirection.Flat)
				return null;

			IOrderedEnumerable<OptionContract> options;

            if (direction == InsightDirection.Down)
			{
                options = this.GetOTM(algorithm, symbol, OptionRight.Put);
            }
            else
            {
                options = this.GetOTM(algorithm, symbol, OptionRight.Call);
            }

            if (options == null || options.Count() <= (_distance+_spread))
            {
                algorithm.Log("WARN: No options matching criteria found!");
                return null;
            }

            var innerOption = options.ElementAt(_distance);
            var outerOption = options.ElementAt(_distance+_spread);

            return new List<IPortfolioTarget> {
                new PortfolioTarget(innerOption.Symbol, this.PositionSize),
                new PortfolioTarget(outerOption.Symbol, -this.PositionSize)
            };
        }
    }
}