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
 *
*/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Data.Consolidators;
using QuantConnect.Interfaces;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// This example demonstrates how to add options for a given underlying equity security.
    /// It also shows how you can prefilter contracts easily based on strikes and expirations, and how you
    /// can inspect the option chain to pick a specific option contract to trade.
    /// </summary>
    /// <meta name="tag" content="using data" />
    /// <meta name="tag" content="options" />
    /// <meta name="tag" content="filter selection" />
    public class BasicTemplateOptionsAlgorithm : QCAlgorithm, IRegressionAlgorithmDefinition
    {
        private const Resolution RESOLUTION = Resolution.Minute;
		private const int MINUTE_RATE = 15;
		private string UnderlyingTicker;
        private int _ROC_THRESHOLD = 1;
		private decimal _TargetProfit = .2m;
		public Symbol Underlying;
		//public readonly Symbol OptionSymbol = QuantConnect.Symbol.Create(UnderlyingTicker, SecurityType.Option, Market.USA);
        private OptionStrategy _Strategy;
        private RateOfChangePercent _rocp;
		private Slice _lastSlice;

		private int _PositionSizeStart = 3;
		private int _MinDaysRemaining = 10;
		private int _MaxTiers = 3;
		private int _ItmDepth = 3;
		private int _RocPeriod = 9;

		public override void Initialize()
        {
			UnderlyingTicker = GetParameter("symbol");
			Underlying = QuantConnect.Symbol.Create(UnderlyingTicker, SecurityType.Equity, Market.USA);
			//AAPL
			//SetStartDate(2014, 06, 06);
			//SetEndDate(2014, 06, 06);

			DateTime startDate = DateTime.Parse(GetParameter("start-date"));
			DateTime endDate = DateTime.Parse(GetParameter("end-date"));

			_ROC_THRESHOLD = parseInt(GetParameter("roc"), _ROC_THRESHOLD);
			_RocPeriod = parseInt(GetParameter("roc-period"), _RocPeriod);
			_TargetProfit = parseDec(GetParameter("target-profit"), _TargetProfit);
			_PositionSizeStart = parseInt(GetParameter("position-size"), _PositionSizeStart);
			_MinDaysRemaining = parseInt(GetParameter("min-days"), _MinDaysRemaining);
			_MaxTiers = parseInt(GetParameter("max-tiers"), _MaxTiers);
			_ItmDepth = parseInt(GetParameter("itm-depth"), _ItmDepth);

			//
			SetStartDate(startDate);
			SetEndDate(endDate);
			//SetStartDate(2015, 01, 01);
			//SetStartDate(2018, 02, 15);
			//SetEndDate(2018, 03, 09);
			SetCash(20000);

			//this.

            var equity = AddEquity(UnderlyingTicker, RESOLUTION);
			var option = AddOption(UnderlyingTicker, RESOLUTION);

            // use the underlying equity as the benchmark
            SetBenchmark(equity.Symbol);

			var consolidator = new TradeBarConsolidator(TimeSpan.FromMinutes(MINUTE_RATE));
			consolidator.DataConsolidated += Consolidator_DataConsolidated;
			_rocp = new RateOfChangePercent(_RocPeriod);
			RegisterIndicator(Underlying, _rocp, consolidator);
			SubscriptionManager.AddConsolidator(Underlying, consolidator);

			// init strategy
			_Strategy = new OptionStrategy(this, option, _MaxTiers, _PositionSizeStart, _ItmDepth, _MinDaysRemaining);
        }

		private int parseInt(string val, int defaultValue)
		{
			int v;
			if (!int.TryParse(val, out v))
				v = defaultValue;
			return v;
		}

		private decimal parseDec(string val, decimal defaultValue)
		{
			decimal v;
			if (!decimal.TryParse(val, out v))
				v = defaultValue;
			return v;
		}

		private void Consolidator_DataConsolidated(object sender, TradeBar e)
		{
			if (IsMarketOpen(Underlying) && _lastSlice != null)
			{
				/*
				if (this.Portfolio.TotalHoldingsValue == 0 &&
					_rocp.Current.Value >= _ROC_THRESHOLD)
				{
					this.MarketOrder(Underlying, -100);
					_quantity = -100;
				}
				else
				{
					if (this.Portfolio.TotalUnrealizedProfit > (this.Portfolio.TotalAbsoluteHoldingsCost * 0.05m))
					{
						this.Liquidate();
					}
					else if (this.Portfolio.TotalUnrealizedProfit < (this.Portfolio.TotalAbsoluteHoldingsCost * -0.05m))
					{
						_quantity *= 2;
						this.MarketOrder(Underlying, _quantity);
					}
				}
				*/

				if (!_Strategy.IsInvested() && _rocp.Current.Value >= _ROC_THRESHOLD)
				{
					Console.WriteLine("<{0}>\tROCP= {1:00}", e.Time.ToString(), _rocp.Current.Value);

					_Strategy.MarketBuyNextTierOptions(_lastSlice);
				}

				if (_Strategy.AggregateProfitPercentage(_lastSlice) > _TargetProfit)
				{
					_Strategy.CloseAll();
				}
				else
				{
					var otmOptions = _Strategy.GetOTMPositions();
					if (otmOptions.Count > 0)
					{
						_Strategy.MarketBuyNextTierOptions(_lastSlice);
						_Strategy.ClosePosition(otmOptions[0]);
					}
				}

				_Strategy.RolloverBeforeExpiry(_lastSlice);
				
				/*else if (_Strategy.AggregateProfitPercentage(_lastSlice) < -.1m)
				{
					//if (_Strategy.AverageBasePrice(slice);
					_Strategy.MarketBuyNextTierOptions(_lastSlice);
				}
				*/
			}
		}

		/// <summary>
		/// Event - v3.0 DATA EVENT HANDLER: (Pattern) Basic template for user to override for receiving all subscription data in a single event
		/// </summary>
		/// <param name="slice">The current slice of data keyed by symbol string</param>
		public override void OnData(Slice slice)
		{
			_lastSlice = slice;

			//Console.WriteLine("{0} Options: {1}", _lastSlice[Underlying].EndTime, _lastSlice.OptionChains.Count);
		}

		public override void OnEndOfAlgorithm()
		{
			//Print stats
			Log("==== FINAL OPTION STRATEGY STATS ====");
			_Strategy.PrintStats();

			base.OnEndOfAlgorithm();
		}

		/// <summary>
		/// Order fill event handler. On an order fill update the resulting information is passed to this method.
		/// </summary>
		/// <param name="orderEvent">Order event details containing details of the evemts</param>
		/// <remarks>This method can be called asynchronously and so should only be used by seasoned C# experts. Ensure you use proper locks on thread-unsafe objects</remarks>
		public override void OnOrderEvent(OrderEvent orderEvent)
        {
            Log(orderEvent.ToString());
        }

        /// <summary>
        /// This is used by the regression test system to indicate if the open source Lean repository has the required data to run this algorithm.
        /// </summary>
        public bool CanRunLocally { get; } = true;

        /// <summary>
        /// This is used by the regression test system to indicate which languages this algorithm is written in.
        /// </summary>
        public Language[] Languages { get; } = { Language.CSharp, Language.Python };

        /// <summary>
        /// This is used by the regression test system to indicate what the expected statistics are from running the algorithm
        /// </summary>
        public Dictionary<string, string> ExpectedStatistics => new Dictionary<string, string>
        {
            {"Total Trades", "2"},
            {"Average Win", "0%"},
            {"Average Loss", "-0.28%"},
            {"Compounding Annual Return", "-78.105%"},
            {"Drawdown", "0.300%"},
            {"Expectancy", "-1"},
            {"Net Profit", "-0.280%"},
            {"Sharpe Ratio", "0"},
            {"Loss Rate", "100%"},
            {"Win Rate", "0%"},
            {"Profit-Loss Ratio", "0"},
            {"Alpha", "0"},
            {"Beta", "0"},
            {"Annual Standard Deviation", "0"},
            {"Annual Variance", "0"},
            {"Information Ratio", "0"},
            {"Tracking Error", "0"},
            {"Treynor Ratio", "0"},
            {"Total Fees", "$0.50"}
        };
    }
}
