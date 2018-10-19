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
        protected Dictionary<Symbol, Option> _OptionSymbols = new Dictionary<Symbol, Option>();

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
            chain = null;
            Option optionSymbol;
            if (!_OptionSymbols.TryGetValue(underlyingSymbol, out optionSymbol))
                return false;

            if (algorithm.CurrentSlice == null ||
                algorithm.CurrentSlice.OptionChains == null)
                return false;

            return algorithm.CurrentSlice.OptionChains.TryGetValue(optionSymbol.Symbol.Value, out chain);
        }

        public override void OnSecuritiesChanged(QCAlgorithmFramework algorithm, SecurityChanges changes)
        {
            base.OnSecuritiesChanged(algorithm, changes);
            //TODO: auto-detect option selector?
            foreach (var s in changes.AddedSecurities)
            {
                if (s.Symbol.SecurityType == SecurityType.Equity)
                {
                    _OptionSymbols[s.Symbol] = algorithm.AddOption(s.Symbol, s.Resolution);
                }
            }
        }
    }
}