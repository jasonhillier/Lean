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
    /// Opens an OTM put if going down, or an OTM call if going up.
    /// </summary>
    public class CalendarSpreadPortfolioModel : BaseOptionPortfolioModel
    {
        private readonly int _distance;
        private readonly int _spread;
        private readonly int _expirationSpread;

        public CalendarSpreadPortfolioModel()
            : this(0,0,-1)
		{
		}

        public CalendarSpreadPortfolioModel(int distance, int spread, int expirationSpread)
		{
            _distance = distance;
            _spread = spread;
            if (_distance != 0 &&
                _spread != 0)
            {
                //TODO: i'm lazy right now
                throw new Exception("Parameters not supported yet!");
            }

            _expirationSpread = expirationSpread;
		}

		/// <summary>
		/// When a new insight comes in, close down anything that might be open.
		/// </summary>
		public override List<IPortfolioTarget> PossiblyCloseCurrentTargets(QCAlgorithmFramework algorithm, Symbol symbol, IEnumerable<OptionHolding> optionHoldings, Insight insight)
		{
			if (insight.Direction == InsightDirection.Down)
				return null;

            //TODO: close when one leg gets closed.

			return this.LiquidateOptions(algorithm, optionHoldings);
        }

		protected virtual List<IPortfolioTarget> IncrementOptionPositions(QCAlgorithmFramework algorithm, Symbol baseSymbol, IEnumerable<OptionHolding> holdings, Insight insight)
		{
			return FindPotentialOptions(algorithm, baseSymbol, insight);
		}


		public override List<IPortfolioTarget> FindPotentialOptions(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
        {
			if (insight.Direction != InsightDirection.Down)
				return null;

            int mag = (int)(insight.Magnitude == null ? 1 : Math.Round((decimal)insight.Magnitude));

            IOrderedEnumerable<OptionContract> options;

            options = this.GetITM(algorithm, symbol, OptionRight.Put);
            if (options.Count() == 0)
            {
                algorithm.Log("No valid options found!");
                return null;
            }
            var inside = options.ElementAt(0);

            options = this.GetITM(algorithm, symbol, OptionRight.Put, -_expirationSpread);
            if (options.Count() == 0)
            {
                algorithm.Log("No valid options found!");
                return null;
            }
            var outside = options.ElementAt(0);

            return new List<IPortfolioTarget> {
                new PortfolioTarget(inside.Symbol, this.PositionSize * mag),
                new PortfolioTarget(outside.Symbol, -this.PositionSize * mag)
            };
        }
    }
}