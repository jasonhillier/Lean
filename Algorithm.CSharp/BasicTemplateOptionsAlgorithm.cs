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
 *
*/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders;

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
    public class BasicTemplateOptionsAlgorithm : QCAlgorithm
    {
        private const Resolution RESOLUTION = Resolution.Minute;
		private const int MINUTE_RATE = 15;
        private const string UnderlyingTicker = "VXX";
        private const int _ROC_THRESHOLD = 10;
        public readonly Symbol Underlying = QuantConnect.Symbol.Create(UnderlyingTicker, SecurityType.Equity, Market.USA);
        public readonly Symbol OptionSymbol = QuantConnect.Symbol.Create(UnderlyingTicker, SecurityType.Option, Market.USA);
        private OptionStrategy _Strategy;
        private RateOfChangePercent _rocp;

        public override void Initialize()
        {
            //
            SetStartDate(2015, 01, 01);
            SetEndDate(2018, 03, 09);
            SetCash(100000);

            var equity = AddEquity(UnderlyingTicker, RESOLUTION);
            //var option = AddOption(UnderlyingTicker, RESOLUTION);

            // use the underlying equity as the benchmark
            SetBenchmark(equity.Symbol);

			// init strategy
			//_Strategy = new OptionStrategy(this, option);

			_rocp = ROCP(Underlying, 9* MINUTE_RATE, RESOLUTION);
        }

        /// <summary>
        /// Event - v3.0 DATA EVENT HANDLER: (Pattern) Basic template for user to override for receiving all subscription data in a single event
        /// </summary>
        /// <param name="slice">The current slice of data keyed by symbol string</param>
        public override void OnData(Slice slice)
        {
            if (IsMarketOpen(OptionSymbol))
            {
                if (_rocp.Current.Value >= _ROC_THRESHOLD)
                {
                    Console.WriteLine("<{0}>\tROCP= {1:00}", slice.Time.ToString(), _rocp.Current.Value);
                    /*
                    if (_Strategy.AggregateProfitPercentage(slice) > .1m)
                        _Strategy.CloseAll();
                    else if (!_Strategy.IsInvested() ||
                             _Strategy.AggregateProfitPercentage(slice) < -.5m)
                    {
                        //if (_Strategy.AverageBasePrice(slice);
                        _Strategy.MarketBuyNextTierOptions(slice);
                    }
                    */
                }
            }
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
    }
}
