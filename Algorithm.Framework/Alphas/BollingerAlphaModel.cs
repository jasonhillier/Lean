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
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Securities;
using QuantConnect.Data.Market;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    /// <summary>
    /// Alpha model that uses an EMA cross to create insights
    /// </summary>
    public class BollingerAlphaModel : AlphaModel
    {
        private readonly int _period;
        private readonly decimal _threshold;
		private readonly Resolution _resolution = Resolution.Minute;
		private readonly bool _inverted;
        private readonly Dictionary<Symbol, SymbolData> _symbolDataBySymbol;

		/// <summary>
		/// Initializes a new instance of the <see cref="BollingerAlphaModel"/> class
		/// </summary>
		public BollingerAlphaModel(
            int period = 9,
            int threshold = 1,
			bool inverted = false
            )
        {
			_period = period;
			_threshold = threshold;
			_inverted = inverted;
			_symbolDataBySymbol = new Dictionary<Symbol, SymbolData>();
            Name = $"{nameof(BollingerAlphaModel)}({period},{threshold})";
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
            foreach (var symbolData in _symbolDataBySymbol.Values)
            {
                if (data.ContainsKey(symbolData.Symbol) && symbolData.BB.IsReady)
                {
					var bar = ((TradeBar)data[symbolData.Symbol]);
					if (bar == null) return insights;

					symbolData.Update(bar);

					var price = bar.Price;
					var insightPeriod = _resolution.ToTimeSpan().Multiply(_period);
					InsightDirection? direction = null;

					if (price <= symbolData.BB.LowerBand.Current.Price)
                    {
						direction = InsightDirection.Down;
                    }
					else if (price >= symbolData.BB.UpperBand.Current.Price)
					{
						direction = InsightDirection.Up;
					}
					else
					{
						if (symbolData.LastDirection == InsightDirection.Up)
						{
							if (price <= symbolData.BB.MiddleBand.Current.Price)
							{
								direction = InsightDirection.Flat;
							}
						}
						if (symbolData.LastDirection == InsightDirection.Down)
						{
							if (price >= symbolData.BB.MiddleBand.Current.Price)
							{
								direction = InsightDirection.Flat;
							}
						}
					}

					if (direction != null && direction != symbolData.LastDirection)
					{
						symbolData.LastDirection = (InsightDirection)direction;

						if (direction == InsightDirection.Down)
							insights.Add(Insight.Price(symbolData.Symbol, insightPeriod, _inverted ? InsightDirection.Down : InsightDirection.Up));
						else if (direction == InsightDirection.Up)
							insights.Add(Insight.Price(symbolData.Symbol, insightPeriod, _inverted ? InsightDirection.Up : InsightDirection.Down));
						else
							insights.Add(Insight.Price(symbolData.Symbol, insightPeriod, InsightDirection.Flat));
					}
                }
            }

            return insights;
        }

        /// <summary>
        /// Event fired each time the we add/remove securities from the data feed
        /// </summary>
        /// <param name="algorithm">The algorithm instance that experienced the change in securities</param>
        /// <param name="changes">The security additions and removals from the algorithm</param>
        public override void OnSecuritiesChanged(QCAlgorithmFramework algorithm, SecurityChanges changes)
        {
            foreach (var added in changes.AddedSecurities)
            {
				if (!added.Symbol.HasUnderlying) continue;

                SymbolData symbolData;
                if (!_symbolDataBySymbol.TryGetValue(added.Symbol, out symbolData))
                {
                    // create fast/slow EMAs
                    var bb = algorithm.BB(added.Symbol, _period, _threshold);
					symbolData = _symbolDataBySymbol[added.Symbol] = new SymbolData
					{
						Security = added,
						BB = bb,
						LastDirection = InsightDirection.Flat,
						Price = new Series("Price", SeriesType.Line),
						Upper = new Series("Upper", SeriesType.Line),
						Middle = new Series("Middle", SeriesType.Line),
						Lower = new Series("Lower", SeriesType.Line),
					};

					var chart = new Chart(added.Symbol.Value + " - Bollinger Bands");
					chart.AddSeries(symbolData.Price);
					chart.AddSeries(symbolData.Upper);
					chart.AddSeries(symbolData.Middle);
					chart.AddSeries(symbolData.Lower);
					algorithm.AddChart(chart);
				}
                else
                {
                    // a security that was already initialized was re-added, reset the indicators
                    symbolData.BB.Reset();
                }
            }
        }

		/// <summary>
		/// Contains data specific to a symbol required by this model
		/// </summary>
		private class SymbolData
		{
			public Security Security { get; set; }
			public Symbol Symbol => Security.Symbol;
			public BollingerBands BB { get; set; }
			public InsightDirection LastDirection { get; set; }
			public Series Price {get;set;} //TODO: this shouldn't be here
			public Series Upper { get; set; }
			public Series Lower { get; set; }
			public Series Middle { get; set; }

			public void Update(TradeBar currentBar)
			{
				Price.AddPoint(currentBar.EndTime, currentBar.Close);
				Upper.AddPoint(BB.UpperBand.Current.EndTime, BB.UpperBand.Current.Price);
				Middle.AddPoint(BB.MiddleBand.Current.EndTime, BB.MiddleBand.Current.Price);
				Lower.AddPoint(BB.LowerBand.Current.EndTime, BB.LowerBand.Current.Price);
			}
		}
    }
}