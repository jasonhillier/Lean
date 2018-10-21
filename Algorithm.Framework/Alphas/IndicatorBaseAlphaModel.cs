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
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Util;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    /// <summary>
    /// Uses Wilder's RSI to create insights. Using default settings, a cross over below 30 or above 70 will
    /// trigger a new insight.
    /// </summary>
    public abstract class IndicatorThresholdAlphaModel<T> : AlphaModel
        where T : TradeBarIndicator
    {
        private readonly Dictionary<Symbol, SymbolData> _symbolDataBySymbol = new Dictionary<Symbol, SymbolData>();

        private readonly int _period;
        private readonly TimeSpan _resolution;
		private readonly double _threshold;
        private readonly double _step;
		private readonly bool _inverted;

		/// <summary>
		/// Initializes a new instance of the <see cref="StdDevAlphaModel"/> class
		/// </summary>
        public IndicatorThresholdAlphaModel(
			TimeSpan resolution,
			int period = 20,
            double threshold = 0.2d,
            double step = 0,
			bool inverted = false
            )
        {
            _period = period;
            _resolution = resolution;
			_threshold = threshold;
            _step = step == 0 ? threshold/2 : step;
			_inverted = inverted;
            Name = $"{nameof(IndicatorThresholdAlphaModel<T>)}({_period},{_resolution})";
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
                var previousMag = kvp.Value.Mag;
                double mag;
                var state = GetState(std, out mag);

                if ((state != previousState || mag > previousMag) && std.IsReady)
                {
                    var insightPeriod = _resolution.Multiply(_period);

                    switch (state)
                    {
						case State.Neutral:
							insights.Add(Insight.Price(symbol, insightPeriod, InsightDirection.Flat));
							break;
						case State.TrippedHigh:
                            insights.Add(Insight.Price(symbol, insightPeriod, _inverted ? InsightDirection.Up : InsightDirection.Down, mag));
                            break;
                        case State.TrippedLow:
                            insights.Add(Insight.Price(symbol, insightPeriod, _inverted ? InsightDirection.Down : InsightDirection.Up, mag));
                            break;
					}
                }

                kvp.Value.State = state;
                kvp.Value.Mag = mag;
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
                    if (!added.Symbol.HasUnderlying) //ignore derivatives
                    {
                        var symbolData = new SymbolData(algorithm, added.Symbol, _resolution, _period);
                        _symbolDataBySymbol[added.Symbol] = symbolData;
                        addedSymbols.Add(symbolData.Symbol);
                    }
                }
            }

            if (addedSymbols.Count > 0)
            {
                // warmup our indicators by pushing history through the consolidators
                algorithm.History(addedSymbols, _resolution.Multiply(_period))
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
        /// Determines the new state.
        /// </summary>
        private State GetState(T std, out double mag)
        {
            mag = 0;

            if (std >= _threshold)
            {
                mag = Math.Round(1 + (((double)std.Current.Value - _threshold)) / _step);
                return State.TrippedHigh;
            }
            else if (std <= -_threshold)
            {
                mag = Math.Round(1 + (((double)std.Current.Value + _threshold)) / -_step);
                return State.TrippedLow;
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
            public T STD { get; }
            public double Mag { get; set; }

            public SymbolData(QCAlgorithmFramework algorithm, Symbol symbol, TimeSpan resolution, int period)
            {
                Symbol = symbol;
                //TODO: make this magical autp-detecting
                STD = (T)Activator.CreateInstance(typeof(T), period);
                Mag = 0;
				algorithm.RegisterIndicator(Symbol, STD, resolution);//, null); //, Consolidator);

                State = State.Neutral;
            }
        }

        /// <summary>
        /// Defines the state. This is used to prevent signal spamming and aid in bounce detection.
        /// </summary>
        private enum State
        {
            TrippedLow,
            Neutral,
            TrippedHigh
		}
    }
}