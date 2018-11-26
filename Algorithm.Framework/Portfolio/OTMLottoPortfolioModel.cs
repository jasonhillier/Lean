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
    public class OTMLottoPortfolioModel : BaseOptionPortfolioModel
    {
        private readonly int _distance;

		public OTMLottoPortfolioModel()
			: this(2,2,30)
		{
		}

		public OTMLottoPortfolioModel(int distance, int MinDaysRemaining, int MaxDaysRemaining)
		{
            _distance = distance;

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
            List<IPortfolioTarget> closingTargets = new List<IPortfolioTarget>();
            optionHoldings.All(p =>
            {
                //puts close if not going down
                if (p.Symbol.ID.OptionRight == OptionRight.Put && insight.Direction != InsightDirection.Down)
                    closingTargets.Add(new PortfolioTarget(p.Symbol, 0)); //0=close out whatever outstanding quantity
                //calls close if not going up
                if (p.Symbol.ID.OptionRight == OptionRight.Call && insight.Direction != InsightDirection.Up)
                    closingTargets.Add(new PortfolioTarget(p.Symbol, 0)); //0=close out whatever outstanding quantity
                return true;
            });

            return closingTargets;
        }

        public override List<IPortfolioTarget> FindPotentialOptions(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
        {
            if (insight.Direction == InsightDirection.Flat)
                return null;

            int mag = (int)(insight.Magnitude == null ? 1 : Math.Round((decimal)insight.Magnitude));

            var right = insight.Direction == InsightDirection.Down ? OptionRight.Put : OptionRight.Call;

            var options = this.GetOTM(algorithm, symbol, OptionRight.Put);
            if (options == null || options.Count() <= _distance)
            {
                algorithm.Log("WARN: No options matching criteria found!");
                return null;
            }

            var option = options.ElementAt(_distance);

            return new List<IPortfolioTarget> { new PortfolioTarget(option.Symbol, this.PositionSize * mag) };
        }
    }
}