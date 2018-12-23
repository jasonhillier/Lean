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
    /// Provides an implementation of <see cref="IPortfolioConstructionModel"/> that gives equal weighting to all
    /// securities. The target percent holdings of each security is 1/N where N is the number of securities. For
    /// insights of direction <see cref="InsightDirection.Up"/>, long targets are returned and for insights of direction
    /// <see cref="InsightDirection.Down"/>, short targets are returned.
    /// </summary>
    public abstract class BaseOptionPortfolioModel : BasePortfolioModel
    {
        public int PositionSize = 1; //TODO make smarter
		public bool PrintPosition = false;
        protected Func<OptionFilterUniverse,OptionFilterUniverse> _OptionFilter;

		public sealed override List<IPortfolioTarget> CloseTargetsFromInsight(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
		{
			//do nothing
			return null;
		}

		public sealed override List<IPortfolioTarget> OpenTargetsFromInsight(QCAlgorithmFramework algorithm, Symbol baseSymbol, Insight insight)
		{
			var currentHoldings = GetOptionHoldings(algorithm, baseSymbol);
			var pendingOrderCount = OptionTools.GetOpenOrderQuantity(algorithm, baseSymbol, true, true);

			if (this.PrintPosition)
			{
				algorithm.Log("pending orders: " + pendingOrderCount);
				foreach(var holding in currentHoldings)
				{
					algorithm.Log("holding: " + holding.Symbol + "\t" + holding.Quantity + "\t" + holding.UnrealizedProfit);
				}
			}

			if (currentHoldings.Count() > 0 ||
				pendingOrderCount > 0)
			{
                this.SanityCheckHoldings(algorithm, currentHoldings);

				if (insight.Direction == InsightDirection.Flat)
				{
					//create a target to close holdings
					return this.LiquidateOptions(algorithm, currentHoldings);
				}
				else
				{
					//TODO: close pending orders too???
					var closingTargets = PossiblyCloseCurrentTargets(algorithm, baseSymbol, currentHoldings, insight);
					if (closingTargets == null || closingTargets.Count > 0)
						return closingTargets;
					
					//if we aren't closing anything, then add to open position
					return this.IncrementOptionPositions(algorithm, baseSymbol, currentHoldings, insight);
				}
			}
			else
			{
				return FindPotentialOptions(algorithm, baseSymbol, insight);
			}
		}

		public abstract List<IPortfolioTarget> PossiblyCloseCurrentTargets(QCAlgorithmFramework algorithm, Symbol symbol, IEnumerable<OptionHolding> holdings, Insight insight);
		public abstract List<IPortfolioTarget> FindPotentialOptions(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight);

		protected virtual List<IPortfolioTarget> LiquidateOptions(QCAlgorithmFramework algorithm, IEnumerable<OptionHolding> holdings)
		{
			var targets = new List<IPortfolioTarget>();

			foreach(var h in holdings)
			{
				targets.Add(new PortfolioTarget(h.Symbol, 0));
			}

			return targets;
		}

		protected virtual List<IPortfolioTarget> IncrementOptionPositions(QCAlgorithmFramework algorithm, Symbol baseSymbol, IEnumerable<OptionHolding> holdings, Insight insight)
		{
			//don't touch anything if there are orders still pending
			if (OptionTools.GetOpenOrderQuantity(algorithm, baseSymbol, true, true) > 0)
				return null;

			var targets = new List<IPortfolioTarget>();
			holdings.All(p =>
			{
				targets.Add(new PortfolioTarget(p.Symbol, p.Quantity + this.PositionSize));
				return true;
			});
			return targets;
		}

		public IEnumerable<OptionHolding> GetOptionHoldings(QCAlgorithmFramework algorithm, Symbol underlyingSymbol)
        {
            List<OptionHolding> holdings = new List<OptionHolding>();

            algorithm.Portfolio.All(p =>
            {
                if (!p.Value.Invested || !(p.Key.SecurityType == SecurityType.Option))
                    return true;

                if (p.Key.Underlying.Value == underlyingSymbol.Value)
                {
                    holdings.Add((OptionHolding)p.Value);
                }

                return true;
            });

            return holdings;
        }

        public bool TryGetOptionChain(QCAlgorithmFramework algorithm, Symbol underlyingSymbol, out OptionChain chain)
        {
            return OptionTools.TryGetOptionChain(algorithm, underlyingSymbol, out chain);
        }

        public void SanityCheckHoldings(QCAlgorithmFramework algorithm, IEnumerable<OptionHolding> holdings)
        {
            holdings.All(o =>
            {
                if ((o.Symbol.ID.Date - algorithm.Time).TotalDays < -5)
                {
                    throw new Exception("Sanity check: Invalid option holding!!");
                    //algorithm.Log("Sanity check: Invalid option holding!!");
                    //this.LiquidateOptions(algorithm, holdings);
                }
                return true;
            });
        }

        /// <summary>
        /// Get ITM options for nearest available expiration.
        /// </summary>
        protected IOrderedEnumerable<OptionContract> GetITM(QCAlgorithmFramework algorithim, Symbol underlyingSymbol, OptionRight right, int expiryDistance = 0)
        {
            IOrderedEnumerable<OptionContract> selected = OptionTools.GetOptionsForExpiry(algorithim, underlyingSymbol, expiryDistance);
            if (selected == null)
                return null;

            return selected
                .Where(o => o.Right == right)
                //check if ITM
                .Where(o => (right == OptionRight.Put ? o.Strike > o.UnderlyingLastPrice : o.Strike < o.UnderlyingLastPrice))
                //sort by distance from itm
                .OrderBy(o => Math.Abs(o.Strike - o.UnderlyingLastPrice));
        }

		/// <summary>
		/// Get GetATM options for nearest available expiration.
		/// </summary>
		protected Tuple<OptionContract, OptionContract> GetATM(QCAlgorithmFramework algorithim, Symbol underlyingSymbol, int expiryDistance = 0)
		{
			IOrderedEnumerable<OptionContract> selected = OptionTools.GetOptionsForExpiry(algorithim, underlyingSymbol, expiryDistance);
			if (selected == null)
				return null;

			var put = selected
				.Where(o => o.Right == OptionRight.Put &&
					o.Strike - 0.5m < o.UnderlyingLastPrice)
				//sort by distance from atm
				.OrderBy(o => Math.Abs(o.UnderlyingLastPrice - o.Strike)).FirstOrDefault();
			if (put == null) return null;


			var call = selected
				.Where(o => o.Right == OptionRight.Call &&
					o.Strike == put.Strike).FirstOrDefault();
			if (call == null) return null;

			return new Tuple<OptionContract, OptionContract>(call, put);
		}

		/// <summary>
		/// Get OTM options for nearest available expiration.
		/// </summary>
		protected IOrderedEnumerable<OptionContract> GetOTM(QCAlgorithmFramework algorithim, Symbol underlyingSymbol, OptionRight right, int expiryDistance = 0)
        {
            IOrderedEnumerable<OptionContract> selected = OptionTools.GetOptionsForExpiry(algorithim, underlyingSymbol, expiryDistance);
            if (selected == null)
                return null;

            return selected
                .Where(o => o.Right == right)
                //check if OTM
                .Where(o => (right == OptionRight.Put ? o.Strike < o.UnderlyingLastPrice : o.Strike > o.UnderlyingLastPrice))
                //sort by distance from atm
                .OrderBy(o => Math.Abs(o.UnderlyingLastPrice - o.Strike));
        }

		public virtual Tuple<OptionContract, OptionContract> GetOTMSpread(QCAlgorithmFramework algorithm, Symbol symbol, int spread = 0, int expiryDistance = 0)
		{
			var targets = new List<IPortfolioTarget>();

			var calls = this.GetOTM(algorithm, symbol, OptionRight.Call, expiryDistance);
			var puts = this.GetOTM(algorithm, symbol, OptionRight.Put, expiryDistance);

			if (calls.Count() > spread && puts.Count() > spread)
			{
				var call = calls.Skip(spread).FirstOrDefault();
				var put = puts.Skip(spread).FirstOrDefault();

				if (call == null || put == null) return null;

				return new Tuple<OptionContract, OptionContract>(call, put);
			}

			return null;
		}
	}
}