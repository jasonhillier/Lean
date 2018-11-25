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
        protected Func<OptionFilterUniverse,OptionFilterUniverse> _OptionFilter;

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

        /// <summary>
        /// Get ITM options for nearest available expiration.
        /// </summary>
        public IOrderedEnumerable<OptionContract> GetITM(QCAlgorithmFramework algorithim, Symbol underlyingSymbol, OptionRight right, int expiryDistance = 0)
        {
            IOrderedEnumerable<OptionContract> selected = this.GetOptionsForExpiry(algorithim, underlyingSymbol, expiryDistance);
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
        /// Get OTM options for nearest available expiration.
        /// </summary>
        public IOrderedEnumerable<OptionContract> GetOTM(QCAlgorithmFramework algorithim, Symbol underlyingSymbol, OptionRight right, int expiryDistance = 0)
        {
            IOrderedEnumerable<OptionContract> selected = this.GetOptionsForExpiry(algorithim, underlyingSymbol, expiryDistance);
            if (selected == null)
                return null;

            return selected
                .Where(o => o.Right == right)
                //check if OTM
                .Where(o => (right == OptionRight.Put ? o.Strike < o.UnderlyingLastPrice : o.Strike > o.UnderlyingLastPrice))
                //sort by distance from atm
                .OrderBy(o => Math.Abs(o.UnderlyingLastPrice - o.Strike));
        }

        /// <summary>
        /// Get all available options for target expiration.
        /// </summary>
        public IOrderedEnumerable<OptionContract> GetOptionsForExpiry(QCAlgorithmFramework algorithim, Symbol underlyingSymbol, int expiryDistance)
        {
            OptionChain chain;
            if (!this.TryGetOptionChain(algorithim, underlyingSymbol, out chain))
            {
                return null;
            }

            List<DateTime> expirations = new List<DateTime>();

            var options = chain.All((o) =>
            {
                if (!expirations.Contains(o.Expiry))
                expirations.Add(o.Expiry);
                return true;
            });

            expirations.OrderBy((i) => i);

            if (expirations.Count <= expiryDistance)
                return null;

            var targetExpiry = expirations[expiryDistance];

            //select only expiry
            return chain.Where(x => (x.Expiry == targetExpiry))
                        .OrderBy(x => x.Expiry);
        }

        /// <summary>
        /// Get long put and short call
        /// </summary>
        public Tuple<OptionContract, OptionContract> GetSyntheticShort(QCAlgorithmFramework algorithm, Symbol underlyingSymbol, int expiryDistance = 0)
        {
            var puts = this.GetITM(algorithm, underlyingSymbol, OptionRight.Put, expiryDistance);
            var calls = this.GetOTM(algorithm, underlyingSymbol, OptionRight.Call, expiryDistance);

            if (puts == null || calls == null || puts.Count() < 1 || calls.Count() < 1 || puts.First().Strike != calls.First().Strike)
            {
                algorithm.Log("Option alignment error!");
                return null;
            }

            return new Tuple<OptionContract, OptionContract>(puts.First(), calls.First());
        }

        /// <summary>
        /// Get long call and short put
        /// </summary>
        public Tuple<OptionContract, OptionContract> GetSyntheticLong(QCAlgorithmFramework algorithm, Symbol underlyingSymbol, int expiryDistance = 0)
        {
            var calls = this.GetITM(algorithm, underlyingSymbol, OptionRight.Call, expiryDistance);
            var puts = this.GetOTM(algorithm, underlyingSymbol, OptionRight.Put, expiryDistance);

            if (puts == null || calls == null || puts.Count() < 1 || calls.Count() < 1 || puts.First().Strike != calls.First().Strike)
            {
                algorithm.Log("Option alignment error!");
                return null;
            }

            return new Tuple<OptionContract, OptionContract>(calls.First(), puts.First());
        }
    }
}