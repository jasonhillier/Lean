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
    public class SyntheticPortfolioModel : BaseOptionPortfolioModel
    {
		/// <summary>
		/// When a new insight comes in, close down anything that might be open.
		/// </summary>
		public override List<IPortfolioTarget> PossiblyCloseCurrentTargets(QCAlgorithmFramework algorithm, Symbol symbol, IEnumerable<OptionHolding> holdings, Insight insight)
        {
            List<IPortfolioTarget> closingTargets = new List<IPortfolioTarget>();
			holdings.All(p =>
            {
                if (insight.Direction != InsightDirection.Down)
                {
                    //if NOT going down, close any synthetic shorts
                    if (p.Symbol.ID.OptionRight == OptionRight.Put && p.IsLong)
                        closingTargets.Add(new PortfolioTarget(p.Symbol, 0)); //0=close out whatever outstanding quantity
                    if (p.Symbol.ID.OptionRight == OptionRight.Call && p.IsShort)
                        closingTargets.Add(new PortfolioTarget(p.Symbol, 0)); //0=close out whatever outstanding quantity
                }
                else if (insight.Direction != InsightDirection.Up)
                {
                    //if NOT going up, close any synthetic longs
                    if (p.Symbol.ID.OptionRight == OptionRight.Put && p.IsShort)
                        closingTargets.Add(new PortfolioTarget(p.Symbol, 0)); //0=close out whatever outstanding quantity
                    if (p.Symbol.ID.OptionRight == OptionRight.Call && p.IsLong)
                        closingTargets.Add(new PortfolioTarget(p.Symbol, 0)); //0=close out whatever outstanding quantity
                }
                return true;
            });

            return closingTargets;
        }

        public sealed override List<IPortfolioTarget> FindPotentialOptions(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
        {
            if (insight.Direction == InsightDirection.Flat)
                return null;

            int mag = (int)(insight.Magnitude == null ? 1 : Math.Round((decimal)insight.Magnitude));

            return FindSyntheticTargets(algorithm, symbol, insight.Direction == InsightDirection.Up, mag);
        }

        public virtual List<IPortfolioTarget> FindSyntheticTargets(QCAlgorithmFramework algorithm, Symbol symbol, bool isLong, int mag = 1, int expiryDistance = 0)
        {
            var targets = new List<IPortfolioTarget>();

            if (!isLong)
            {
                var synthetic = this.GetSyntheticShort(algorithm, symbol, expiryDistance);
                if (synthetic == null)
                    return targets; //abort/not-found

                //long call, short put
                targets.Add(new PortfolioTarget(synthetic.Item1.Symbol, this.PositionSize * mag));
                targets.Add(new PortfolioTarget(synthetic.Item2.Symbol, -this.PositionSize * mag));
            }
            else
            {
                var synthetic = this.GetSyntheticLong(algorithm, symbol, expiryDistance);
                if (synthetic == null)
                    return targets; //abort/not-found

                //long put, short call
                targets.Add(new PortfolioTarget(synthetic.Item1.Symbol, this.PositionSize * mag));
                targets.Add(new PortfolioTarget(synthetic.Item2.Symbol, -this.PositionSize * mag));
            }

            return targets;
        }
    }
}