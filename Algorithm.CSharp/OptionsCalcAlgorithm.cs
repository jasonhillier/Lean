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
    public class OptionsCalcAlgorithm : QCAlgorithm
    {
        private const Resolution RESOLUTION = Resolution.Minute;
		private const int MINUTE_RATE = 15;
        private const string UnderlyingTicker = "AAPL";
        private const int _ROC_THRESHOLD = 10;
        public readonly Symbol Underlying = QuantConnect.Symbol.Create(UnderlyingTicker, SecurityType.Equity, Market.USA);
        public readonly Symbol OptionSymbol = QuantConnect.Symbol.Create(UnderlyingTicker, SecurityType.Option, Market.USA);
        private OptionStatistics _Statistics;
        private RateOfChangePercent _rocp;
		private Slice _lastSlice;

		public override void Initialize()
        {
			//AAPL
			SetStartDate(2014, 06, 06);
			SetEndDate(2014, 06, 06);

			//
			//SetStartDate(2018, 03, 01);
			//SetEndDate(2018, 03, 01);
			//SetStartDate(2015, 01, 01);
			//SetStartDate(2018, 02, 15);
			//SetEndDate(2018, 03, 09);
			SetCash(100000);

            var equity = AddEquity(UnderlyingTicker, RESOLUTION);
            var option = AddOption(UnderlyingTicker, RESOLUTION);

            // use the underlying equity as the benchmark
            SetBenchmark(equity.Symbol);

			var consolidator = new TradeBarConsolidator(TimeSpan.FromMinutes(MINUTE_RATE));
			consolidator.DataConsolidated += Consolidator_DataConsolidated;
			_rocp = new RateOfChangePercent(9);
			RegisterIndicator(Underlying, _rocp, consolidator);
			SubscriptionManager.AddConsolidator(Underlying, consolidator);

			// init strategy
			_Statistics = new OptionStatistics(this, option);
        }

		private void Consolidator_DataConsolidated(object sender, TradeBar e)
		{
			if (IsMarketOpen(OptionSymbol))
			{
				if (_Statistics.ComputeChain(_lastSlice, e.EndTime))
				{
					_Statistics.Store();
				}
			}
		}

		/// <summary>
		/// Event - v3.0 DATA EVENT HANDLER: (Pattern) Basic template for user to override for receiving all subscription data in a single event
		/// </summary>
		/// <param name="slice">The current slice of data keyed by symbol string</param>
		public override void OnData(Slice slice)
		{
			_lastSlice = slice;
		}
    }
}
