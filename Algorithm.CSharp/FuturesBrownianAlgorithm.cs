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
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Indicators;
using QuantConnect.Data.Consolidators;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// The idea here is to pick up a few pips (high probability, 1:5 risk ratio
    /// - eventually add some stuff here using brownian motion modeling.
    /// //for now, we'll just look at recent HV and trade in calm market.
    public class FuturesBrownianAlgorithm : QCAlgorithm
    {
        private const int _fastPeriod = 60;

        private ExponentialMovingAverage _fast;
        private ExponentialMovingAverage _slow;

        private Symbol _Symbol;
        private OrderTicket _pendingEntry = null;
        private decimal _entrySpread = 0.5m;
        private decimal _profitSpread = 2;
        private decimal _lossSpread = 10;
        private MeanAbsoluteDeviation _devIndicator;
        private bool _hasInitialized = false;

        //public bool IsReady { get { return _fast.IsReady && _slow.IsReady; } }
       //public bool IsUpTrend { get { return IsReady && _fast > _slow * (1 + _tolerance); } }
        //public bool IsDownTrend { get { return IsReady && _fast < _slow * (1 + _tolerance); } }

        public override void Initialize()
        {
            SetStartDate(2016, 1, 1);
            SetEndDate(2016, 8, 18);
            SetCash(100000);
            //SetWarmUp(Math.Max(_fastPeriod, _slowPeriod));

            // Adds SPY to be used in our EMA indicators
            /*
            var equity = AddEquity("SPY", Resolution.Daily);
            _fast = EMA(equity.Symbol, _fastPeriod, Resolution.Daily);
            _slow = EMA(equity.Symbol, _slowPeriod, Resolution.Daily);
            */

            // Adds the future that will be traded and
            // set our expiry filter for this futures chain
            var future = AddFuture(Futures.Indices.SP500EMini);
            future.SetFilter(TimeSpan.Zero, TimeSpan.FromDays(180));

            //SetBenchmark(x => 0);
        }

        void Consolidator_DataConsolidated(object sender, Data.Market.QuoteBar e)
        {
            Console.WriteLine("PRICE=" + e.Price);

            if (Portfolio.GetHoldingsQuantity(_Symbol) == 0)
            {
                if (_pendingEntry != null)
                {
                    Transactions.CancelOrder(_pendingEntry.OrderId);
                    _pendingEntry = null;
                }

                Console.WriteLine("dev=" + _devIndicator.Current.Value);
                if (_devIndicator.Current.Value < 10)
                {
                    this.SetupOrder(e);
                }
            }
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            Log(orderEvent.ToString());

            if (orderEvent.Status == OrderStatus.Filled)
            {
                if (_pendingEntry != null && orderEvent.OrderId == _pendingEntry.OrderId)
                {
                    Console.WriteLine("entry fill! " + orderEvent.FillQuantity);
                    //submit bracket orders
                    SetupBracket(orderEvent);
                }
                else
                {
                    Console.WriteLine("bracket fill! " + orderEvent.FillQuantity);
                    Transactions.CancelOpenOrders(_Symbol);
                }
            }
        }

        public override void OnData(Slice slice)
        {
            Console.WriteLine("log_ondata" + slice.Time.ToString() + " " + slice.FutureChains.Count);

            if (!_hasInitialized)
            {
                foreach(var chain in slice.FutureChains)
                {
                    Console.WriteLine("has chain");

                    // find the front contract expiring no earlier than in 90 days
                    var contract = (
                        from futuresContract in chain.Value.OrderBy(x => x.Expiry)
                        where futuresContract.Expiry > Time.Date.AddDays(10)
                        select futuresContract
                        ).FirstOrDefault();

                    _Symbol = contract.Symbol;

                    var consolidator = new QuoteBarConsolidator(TimeSpan.FromMinutes(1));
                    consolidator.DataConsolidated += Consolidator_DataConsolidated;
                    SubscriptionManager.AddConsolidator(contract.Symbol, consolidator);

                    _devIndicator = MAD(_Symbol, _fastPeriod, Resolution.Minute);

                    Log("Added new consolidator for " + _Symbol.Value);
                    _hasInitialized = true;
                }
            }
        }


        public void SetupBracket(OrderEvent orderEvent)
        {
            int quantity = 0;
            decimal profit = 0; decimal loss = 0;

            if (orderEvent.Direction == OrderDirection.Buy)
            {
                quantity = 1;

                profit = orderEvent.FillPrice + _profitSpread;
                loss = orderEvent.FillPrice - _lossSpread;
            }
            else if (orderEvent.Direction == OrderDirection.Sell)
            {
                quantity = -1;

                profit = orderEvent.FillPrice - _profitSpread;
                loss = orderEvent.FillPrice + _lossSpread;
            }

            this.StopLimitOrder(_Symbol, -quantity, loss, loss);
            this.LimitOrder(_Symbol, -quantity, profit);
        }

        public void SetupOrder(Data.Market.QuoteBar bar)
        {
            //MarketOrder(contract.Symbol, 1);
            _pendingEntry = this.LimitOrder(_Symbol, 1, bar.Bid.Close - _entrySpread);
        }

        /*
        public override void OnEndOfDay()
        {
            Plot("Indicator Signal", "EOD", IsDownTrend ? -1 : IsUpTrend ? 1 : 0);
        }
        */
    }
}