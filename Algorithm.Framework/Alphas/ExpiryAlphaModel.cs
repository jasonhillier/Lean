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
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Securities.Option;
using QuantConnect.Util;
using System.Linq;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    /// <summary>
    /// Uses Wilder's RSI to create insights. Using default settings, a cross over below 30 or above 70 will
    /// trigger a new insight.
    /// </summary>
    public class ExpiryAlphaModel : OptionAlphaModel
    {
        private readonly Dictionary<Symbol,bool> _symbolDataBySymbol = new Dictionary<Symbol,bool>();

        private readonly int _period;
        private readonly TimeSpan _resolution;
        private readonly bool _inverted;
        private Option _hackOptionSymbol;
        private bool _timeTrigger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RsiAlphaModel"/> class
        /// </summary>
        /// <param name="period">The RSI indicator period</param>
        /// <param name="resolution">The resolution of data sent into the RSI indicator</param>
        public ExpiryAlphaModel(
            TimeSpan resolution,
            int period = 0,
            double threshold = 0,
            double step = 0,
            bool inverted = false
            )
        {
            //TODO: clean up CTOR for dynamic ^^

            _period = period;
            _resolution = resolution;
            _inverted = inverted;

            Name = $"{nameof(ExpiryAlphaModel)}({_period},{_resolution})";
        }

        /// <summary>
        /// Updates this alpha model with the latest data from the algorithm.
        /// This is called each time the algorithm receives data for subscribed securities
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="data">The new data available</param>
        /// <returns>The new insights generated</returns>
        public override IEnumerable<Insight> Update(QCAlgorithmFramework algorithm, Slice data)
        {
            var insights = new List<Insight>();

            foreach (var symbol in _symbolDataBySymbol.Keys.ToList())
            {
                OptionChain chain;
                if (!TryGetOptionChain(algorithm, symbol, out chain))
                    return insights;

                var options = chain.Where((o) => Math.Abs((o.Expiry - data.Time).TotalDays) <= _period);
                if (options.Count()>0)
                {
                    if (!_symbolDataBySymbol[symbol])
                    {
                        _symbolDataBySymbol[symbol] = true; //debounce
                        //indicate a 'short' signal for expiring options
                        insights.Add(Insight.Price(symbol, TimeSpan.FromDays(_period + 1), _inverted ? InsightDirection.Up : InsightDirection.Down, 1));
                    }
                }
                else if (_symbolDataBySymbol[symbol])
                {
                    _symbolDataBySymbol[symbol] = false; //debounce
                    insights.Add(Insight.Price(symbol, TimeSpan.FromDays(_period + 1), InsightDirection.Flat));
                }
            }

            return insights;
        }

        /// <summary>
        /// Cleans out old security data and initializes the RSI for any newly added securities.
        /// This functional also seeds any new indicators using a history request.
        /// </summary>
        /// <param name="algorithm">The algorithm instance that experienced the change in securities</param>
        /// <param name="changes">The security additions and removals from the algorithm</param>
        public override void OnSecuritiesChanged(QCAlgorithmFramework algorithm, SecurityChanges changes)
        {
            // clean up data for removed securities
            if (changes.RemovedSecurities.Count > 0)
            {
                var removed = changes.RemovedSecurities.ToHashSet(x => x.Symbol);
                foreach (var subscription in algorithm.SubscriptionManager.Subscriptions)
                {
                    if (removed.Contains(subscription.Symbol))
                    {
                        _symbolDataBySymbol.Remove(subscription.Symbol);
                        subscription.Consolidators.Clear();
                    }
                }
            }

            // initialize data for added securities
            var addedSymbols = new List<Symbol>();
            foreach (var added in changes.AddedSecurities)
            {
                if (!_symbolDataBySymbol.ContainsKey(added.Symbol))
                {
                    if (!added.Symbol.HasUnderlying)
                    {
                        _symbolDataBySymbol.Add(added.Symbol, false);

                        //var consolidator = algorithm.ResolveConsolidator(added.Symbol, _resolution);
                        //consolidator.DataConsolidated += Consolidator_DataConsolidated;
                    }
                }
            }

            if (addedSymbols.Count > 0)
            {
                // warmup our indicators by pushing history through the consolidators
                /*
                algorithm.History(addedSymbols, _resolution.Multiply(_period))
                    .PushThrough(data =>
                    {
                        SymbolData symbolData;
                        if (_symbolDataBySymbol.TryGetValue(data.Symbol, out symbolData))
                        {
                            symbolData.RSI.Update(data.EndTime, data.Value);
                        }
                    });
                    */
            }
        }
    }
}