﻿/*
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
    /// </summary>
    public class IVPortfolioModel : BaseOptionPortfolioModel
    {
        /// <summary>
        /// When a new insight comes in, close down anything that might be open.
        /// </summary>
		/*
        public override List<IPortfolioTarget> CloseTargetsFromInsight(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
        {
            var optionHoldings = GetOptionHoldings(algorithm, symbol);

            List<IPortfolioTarget> closingTargets = new List<IPortfolioTarget>();
            optionHoldings.All(p =>
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
		*/

        public override List<IPortfolioTarget> OpenTargetsFromInsight(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
        {
            if (insight.Direction == InsightDirection.Flat)
                return null;

            int mag = (int)(insight.Magnitude == null ? 1 : Math.Round((decimal)insight.Magnitude));

            return FindATMTargets(algorithm, symbol, insight.Direction == InsightDirection.Up, mag);
        }

        public virtual List<IPortfolioTarget> FindATMTargets(QCAlgorithmFramework algorithm, Symbol symbol, bool isLong, int mag = 1, int expiryDistance = 0)
        {
            var targets = new List<IPortfolioTarget>();

			var calls = this.GetOTM(algorithm, symbol, OptionRight.Call, 0);
			var puts = this.GetOTM(algorithm, symbol, OptionRight.Put, 0);

			if (calls.Count() > 0 && puts.Count() > 0)
			{
				int quantity = this.PositionSize * mag;
				if (!isLong)
					quantity = -quantity;

				targets.Add(new PortfolioTarget(calls.First().Symbol, quantity));
				targets.Add(new PortfolioTarget(puts.First().Symbol, quantity));
			}

            return targets;
        }
    }
}