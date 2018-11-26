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
using QuantConnect.Securities;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    /// <summary>
    /// Uses Wilder's RSI to create insights. Using default settings, a cross over below 30 or above 70 will
    /// trigger a new insight.
    /// </summary>
    public class MaxPainStdAlphaModel : OptionAlphaModel
    {
        private readonly Dictionary<Symbol, SymbolData> _symbolDataBySymbol = new Dictionary<Symbol, SymbolData>();

        private readonly int _period;
        private readonly TimeSpan _resolution;
        private readonly double _threshold;
        private readonly double _lowThreshold;
        private readonly double _step;
        private readonly bool _inverted;

        /// <summary>
        /// Initializes a new instance of the <see cref="RsiAlphaModel"/> class
        /// </summary>
        /// <param name="period">The RSI indicator period</param>
        /// <param name="resolution">The resolution of data sent into the RSI indicator</param>
        public MaxPainStdAlphaModel(
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

            Name = $"{nameof(MaxPainStdAlphaModel)}({_period},{_resolution})";
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
                OptionChain chain;
                if (!TryGetOptionChain(algorithm, symbol, out chain))
                    return insights;

				decimal max_pain_strike = FindMaxPain(algorithm, chain);

				if (max_pain_strike <= 0)
				{
					algorithm.Log("Insufficient data to compute max pain!");
					return insights;
				}
				//find the distance of price to the current max pain strike
				decimal max_pain_distance = chain.Underlying.Price - max_pain_strike;
				kvp.Value.Update(data.Time, max_pain_strike, max_pain_distance);

                var std = kvp.Value.STD;
                var previousState = kvp.Value.State;
                var previousMag = kvp.Value.Mag;
                double mag;
                var state = GetState(std, max_pain_distance, out mag); //get the STD (magnitude)

				if ((state != previousState || mag > previousMag) && std.IsReady)
                {
                    var insightPeriod = _resolution.Multiply(_period);

                    switch (state)
                    {
                        case State.Neutral:
                            insights.Add(Insight.Price(symbol, insightPeriod, InsightDirection.Flat));
                            break;
                        case State.TrippedHigh: //bullish
                            insights.Add(Insight.Price(symbol, insightPeriod, _inverted ? InsightDirection.Down : InsightDirection.Up, mag));
                            break;
                        case State.TrippedLow: //bearish
                            insights.Add(Insight.Price(symbol, insightPeriod, _inverted ? InsightDirection.Up : InsightDirection.Down, mag));
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
                        var symbolData = new SymbolData(added.Symbol, _period);
                        _symbolDataBySymbol[added.Symbol] = symbolData;
                        addedSymbols.Add(symbolData.Symbol);

						var chart = new Chart(added.Symbol + " - Max Pain");
						chart.AddSeries(symbolData.MaxPainSeries);
						chart.AddSeries(symbolData.MaxPainRelSTDSeries);
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
        private State GetState(StandardDeviation std, decimal max_pain_distance, out double mag)
        {
            mag = 0;

            if (std >= _threshold)
            {
                mag = Math.Round(1 + (((double)std.Current.Value - _threshold)) / _step);
				if (max_pain_distance > 0)
					return State.TrippedHigh;
				else
					return State.TrippedLow;
            }

            return State.Neutral;
        }

		public decimal FindMaxPain(QCAlgorithmFramework algorithm, OptionChain chain)
		{
			//get all ITM
			var options = GetOptionsForExpiry(algorithm, chain.Underlying.Symbol, 0);
			var calls = options.Where((o) => o.Right == OptionRight.Call && o.Strike < chain.Underlying.Price)
				.OrderBy((o) => o.Strike);
			var puts = options.Where((o) => o.Right == OptionRight.Put && o.Strike > chain.Underlying.Price)
				.OrderBy((o) => -o.Strike);

			//walk up the chain until both sides balance
			int openCalls = 0;
			int openPuts = 0;

			List<int> runningCalls = new List<int>();
			List<int> runningPuts = new List<int>();
			OptionContract largestBidOption = null;
			int pi = 0; //put incrementor
			for(int ci=0; ci<calls.Count() && pi<puts.Count(); ci++)
			{
				var call = calls.Skip(ci).First();
				var put = puts.Skip(pi++).First();

				if (Math.Round((put.Strike - chain.Underlying.Price)) > Math.Round((chain.Underlying.Price - call.Strike)))
				{
					//try again, re-align
					ci--;
					continue;
				}

				//TODO: OpenInterest????!!!!!!!!!!!!!!!!!
				openCalls += (int)call.BidSize;
				openPuts += (int)put.BidSize;

				runningCalls.Add(openCalls);
				runningPuts.Add(openPuts);

				//for now, doing a hack where it finds the option contract with the most interest (largest bid size)
				if (largestBidOption == null)
					largestBidOption = call;
				if (call.BidSize > largestBidOption.BidSize)
					largestBidOption = call;
				if (put.BidSize > largestBidOption.BidSize)
					largestBidOption = put;
			}

			if (largestBidOption == null)
				return 0;

			return largestBidOption.Strike;
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
		/// Contains data specific to a symbol required by this model
		/// </summary>
		private class SymbolData
        {
            public Symbol Symbol { get; }
            public State State { get; set; }
            public StandardDeviation STD { get; }
            public double Mag { get; set; }
			public Series MaxPainSeries { get; }
			public Series MaxPainRelSTDSeries { get; }

			public SymbolData(Symbol symbol, int period)
            {
                Symbol = symbol;
                STD = new StandardDeviation(period);
                State = State.Neutral;

				MaxPainSeries = new Series("Max Pain Strike", SeriesType.Line);
				MaxPainRelSTDSeries = new Series("Relative STD", SeriesType.Line);
			}

            public void Update(DateTime time, decimal max_pain_strike, decimal max_pain_distance)
            {
				MaxPainSeries.AddPoint(time, max_pain_strike);

				var point = new IndicatorDataPoint();
                point.Time = time;
				point.Value = Math.Abs(max_pain_distance);
				STD.Update(point);

				MaxPainRelSTDSeries.AddPoint(time, STD.Current.Value);
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