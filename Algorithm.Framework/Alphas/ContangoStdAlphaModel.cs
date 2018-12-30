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
    public class ContangoStdAlphaModel : OptionAlphaModel
    {
        private readonly Dictionary<Symbol, SymbolData> _symbolDataBySymbol = new Dictionary<Symbol, SymbolData>();

        private readonly int _period;
        private readonly TimeSpan _resolution;
        private readonly double _threshold;
        private readonly double _lowThreshold;
        private readonly double _step;
        private readonly bool _inverted;
        private Option _hackOptionSymbol;

        /// <summary>
        /// Initializes a new instance of the <see cref="RsiAlphaModel"/> class
        /// </summary>
        /// <param name="period">The RSI indicator period</param>
        /// <param name="resolution">The resolution of data sent into the RSI indicator</param>
        public ContangoStdAlphaModel(
            TimeSpan resolution,
            int period = 14,
            double threshold = 0.7,
            double step = 0,
            bool inverted = false
            )
        {
            _period = period;
            _resolution = resolution;
            _threshold = threshold;
            _lowThreshold = Math.Abs(1 - _threshold);
            _step = (step == 0) ? threshold / 2 : step;
            _inverted = inverted;

            Name = $"{nameof(ContangoStdAlphaModel)}({_period},{_resolution})";
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
				try
				{
					var frontOptions = OptionTools.GetOptionsForExpiry(algorithm, symbol, 0);
					var backOptions = OptionTools.GetOptionsForExpiry(algorithm, symbol, 1);

					var frontiv = AverageIV(algorithm, frontOptions);
					var backiv = AverageIV(algorithm, backOptions);

					kvp.Value.Update(data.Time, frontiv, backiv);
				} catch (Exception ex)
				{
					algorithm.Log("Failed to compute IV!");
					continue;
				}

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

					kvp.Value.State = state;
					kvp.Value.Mag = mag;
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
                        //algorithm.Consolidate(added.Symbol, _resolution, HandleAction);

                        //var consolidator = algorithm.ResolveConsolidator(added.Symbol, _resolution);
                        //consolidator.DataConsolidated += Consolidator_DataConsolidated;

                        var symbolData = new SymbolData(added.Symbol, _period);
                        _symbolDataBySymbol[added.Symbol] = symbolData;
                        addedSymbols.Add(symbolData.Symbol);

						var chart = new Chart(added.Symbol.Value + " - Options Contango");
						chart.AddSeries(symbolData.FrontIVSeries);
						chart.AddSeries(symbolData.BackIVSeries);
						chart.AddSeries(symbolData.STDSeries);
						algorithm.AddChart(chart);
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


        /// <summary>
        /// Determines the new state. This is basically cross-over detection logic that
        /// includes considerations for bouncing using the configured bounce tolerance.
        /// </summary>
        private State GetState(StandardDeviation std, out double mag)
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
            public StandardDeviation STD { get; }
            public double Mag { get; set; }
			public Series FrontIVSeries { get; }
			public Series BackIVSeries { get; }
			public Series STDSeries { get; }

			public SymbolData(Symbol symbol, int period)
            {
                Symbol = symbol;
                STD = new StandardDeviation(period);
                State = State.Neutral;

				FrontIVSeries = new Series("Front IV", SeriesType.Line);
				BackIVSeries = new Series("Back IV", SeriesType.Line);
				STDSeries = new Series("Std Dev Contango", SeriesType.Line);
			}

            public void Update(DateTime time, decimal frontiv, decimal backiv)
            {
				FrontIVSeries.AddPoint(time, frontiv);
				BackIVSeries.AddPoint(time, backiv);

				var point = new IndicatorDataPoint();
                point.Time = time;
                point.Value = (backiv*100) - (frontiv*100); //IV given in percent terms

				STD.Update(point);
				STDSeries.AddPoint(time, STD.Current.Value);
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