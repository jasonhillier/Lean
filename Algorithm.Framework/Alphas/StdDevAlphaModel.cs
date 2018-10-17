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

using System.Collections.Generic;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Util;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    /// <summary>
    /// Uses Wilder's RSI to create insights. Using default settings, a cross over below 30 or above 70 will
    /// trigger a new insight.
    /// </summary>
    public class StdDevAlphaModel : AlphaModel
    {
        private readonly Dictionary<Symbol, SymbolData> _symbolDataBySymbol = new Dictionary<Symbol, SymbolData>();

        private readonly int _period;
        private readonly Resolution _resolution;
		private readonly decimal _threshold;
		private readonly decimal _step;
		private readonly bool _inverted;

		/// <summary>
		/// Initializes a new instance of the <see cref="StdDevAlphaModel"/> class
		/// </summary>
		public StdDevAlphaModel(
            int period = 200,
            Resolution resolution = Resolution.Minute,
			decimal threshold = 0.2m,
			decimal step = 0.1m,
			bool inverted = false
            )
        {
            _period = period;
            _resolution = resolution;
			_threshold = threshold;
			_step = step;
			_inverted = inverted;
			Name = $"{nameof(StdDevAlphaModel)}({_period},{_resolution})";
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
            foreach (var kvp in _symbolDataBySymbol)
            {
                var symbol = kvp.Key;
                var std = kvp.Value.STD;
                var previousState = kvp.Value.State;
                var state = GetState(std, previousState);

                if (state != previousState && std.IsReady)
                {
                    var insightPeriod = _resolution.ToTimeSpan().Multiply(_period);

                    switch (state)
                    {
						case State.Neutral:
							insights.Add(Insight.Price(symbol, insightPeriod, InsightDirection.Flat));
							break;
						case State.TrippedHigh:
                            insights.Add(Insight.Price(symbol, insightPeriod, _inverted ? InsightDirection.Up : InsightDirection.Down));
                            break;
						case State.TrippedExtraHigh:
							insights.Add(Insight.Price(symbol, insightPeriod, _inverted ? InsightDirection.Up : InsightDirection.Down));
							break;
					}
                }

                kvp.Value.State = state;
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
                    var std = algorithm.STD(added.Symbol, _period, _resolution);
                    var symbolData = new SymbolData(added.Symbol, std);
                    _symbolDataBySymbol[added.Symbol] = symbolData;
                    addedSymbols.Add(symbolData.Symbol);
                }
            }

            if (addedSymbols.Count > 0)
            {
                // warmup our indicators by pushing history through the consolidators
                algorithm.History(addedSymbols, _period, _resolution)
                    .PushThrough(data =>
                    {
                        SymbolData symbolData;
                        if (_symbolDataBySymbol.TryGetValue(data.Symbol, out symbolData))
                        {
                            symbolData.STD.Update(data.EndTime, data.Value);
                        }
                    });
            }
        }

        /// <summary>
        /// Determines the new state. This is basically cross-over detection logic that
        /// includes considerations for bouncing using the configured bounce tolerance.
        /// </summary>
        private State GetState(StandardDeviation std, State previous)
        {
            if (std >= _threshold)
            {
                return State.TrippedHigh;
            }
			if (std >= _threshold + _step)
			{
				return State.TrippedExtraHigh;
			}

			return State.Neutral;
        }

        /// <summary>
        /// Contains data specific to a symbol required by this model
        /// </summary>
        private class SymbolData
        {
            public Symbol Symbol { get; }
            public State State { get; set; }
            public StandardDeviation STD { get; }

            public SymbolData(Symbol symbol, StandardDeviation std)
            {
                Symbol = symbol;
                STD = std;
                State = State.Neutral;
            }
        }

        /// <summary>
        /// Defines the state. This is used to prevent signal spamming and aid in bounce detection.
        /// </summary>
        private enum State
        {
            Neutral,
            TrippedHigh,
			TrippedExtraHigh
		}
    }
}