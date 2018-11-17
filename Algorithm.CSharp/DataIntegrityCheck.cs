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
using QuantConnect.Indicators;
using QuantConnect.Interfaces;
using System.Linq;
using QuantConnect.Securities.Option;
using QuantConnect.Data.Market;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// Basic template algorithm simply initializes the date range and cash. This is a skeleton
    /// framework you can use for designing an algorithm.
    /// </summary>
    /// <meta name="tag" content="using data" />
    /// <meta name="tag" content="using quantconnect" />
    /// <meta name="tag" content="trading and orders" />
    public class DataIntegrityCheck : QCAlgorithm, IRegressionAlgorithmDefinition
    {
        private Symbol _spy = QuantConnect.Symbol.Create("SPY", SecurityType.Equity, Market.USA);
        private Option _option;
        private Chart _optionMap;
        private Series _putCount;
        private Series _callCount;

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {
            DateTime startDate = DateTime.Parse(GetParameter("start-date"));
            DateTime endDate = DateTime.Parse(GetParameter("end-date"));

            SetStartDate(startDate);  //Set Start Date
            SetEndDate(endDate);    //Set End Date
            SetCash(100000);             //Set Strategy Cash

            // Find more symbols here: http://quantconnect.com/data
            // Forex, CFD, Equities Resolutions: Tick, Second, Minute, Hour, Daily.
            // Futures Resolution: Tick, Second, Minute
            // Options Resolution: Minute Only.
            AddEquity("SPY", Resolution.Minute, Market.USA, false);
            _option = AddOption("SPY", Resolution.Minute, Market.USA, false);

            _optionMap = new Chart();
            _optionMap.Name = "OptionMap";
            _putCount = new Series("putCount", SeriesType.Line);
            _callCount = new Series("callCount", SeriesType.Line);
            _optionMap.AddSeries(_putCount);
            _optionMap.AddSeries(_callCount);

            AddChart(_optionMap);

            Consolidate(_spy, new TimeSpan(0, 15, 0),HandleAction);
        }

        private Slice _lastSlice;
        void HandleAction(Data.Market.TradeBar bar)
        {
            if (_lastSlice != null)
            {
                OptionChain chain;
                int calls = 0; int puts = 0;
                if (_lastSlice.OptionChains.TryGetValue(_option.Symbol.Value, out chain))
                {
                    calls = _lastSlice.OptionChains[_option.Symbol.Value].Select((o) => o.Right == OptionRight.Call).Count();
                    puts = _lastSlice.OptionChains[_option.Symbol.Value].Select((o) => o.Right == OptionRight.Put).Count();
                }

                _callCount.AddPoint(_lastSlice.Time, calls);
                _putCount.AddPoint(_lastSlice.Time, puts);

                Console.WriteLine("{0}\t{1}, {2}", _lastSlice.Time, calls, puts);
            }
        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// </summary>
        /// <param name="data">Slice object keyed by symbol containing the stock data</param>
        public override void OnData(Slice data)
        {
            _lastSlice = data;
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
            {"Total Trades", "1"},
            {"Average Win", "0%"},
            {"Average Loss", "0%"},
            {"Compounding Annual Return", "263.153%"},
            {"Drawdown", "2.200%"},
            {"Expectancy", "0"},
            {"Net Profit", "1.663%"},
            {"Sharpe Ratio", "4.41"},
            {"Loss Rate", "0%"},
            {"Win Rate", "0%"},
            {"Profit-Loss Ratio", "0"},
            {"Alpha", "0.007"},
            {"Beta", "76.118"},
            {"Annual Standard Deviation", "0.192"},
            {"Annual Variance", "0.037"},
            {"Information Ratio", "4.354"},
            {"Tracking Error", "0.192"},
            {"Treynor Ratio", "0.011"},
            {"Total Fees", "$3.26"}
        };
    }
}
