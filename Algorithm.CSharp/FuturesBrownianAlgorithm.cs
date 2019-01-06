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

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// The idea here is to pick up a few pips (high probability, 1:5 risk ratio
    /// - eventually add some stuff here using brownian motion modeling.
    /// //for now, we'll just look at recent HV and trade in calm market.
    public class FuturesBrownianAlgorithm : QCAlgorithm
    {
        private const decimal _tolerance = 0.001m;
        private const int _fastPeriod = 20;
        private const int _slowPeriod = 60;

        private ExponentialMovingAverage _fast;
        private ExponentialMovingAverage _slow;

        private Securities.Future.Future _Symbol;
        private int _pendingOrderId = 0;
        private decimal _profitSpread = 2;
        private decimal _lossSpread = 10;
        private MeanAbsoluteDeviation _devIndicator;

        public bool IsReady { get { return _fast.IsReady && _slow.IsReady; } }
        public bool IsUpTrend { get { return IsReady && _fast > _slow * (1 + _tolerance); } }
        public bool IsDownTrend { get { return IsReady && _fast < _slow * (1 + _tolerance); } }

        public override void Initialize()
        {
            SetStartDate(2016, 1, 1);
            SetEndDate(2016, 8, 18);
            SetCash(100000);
            SetWarmUp(Math.Max(_fastPeriod, _slowPeriod));

            // Adds SPY to be used in our EMA indicators
            /*
            var equity = AddEquity("SPY", Resolution.Daily);
            _fast = EMA(equity.Symbol, _fastPeriod, Resolution.Daily);
            _slow = EMA(equity.Symbol, _slowPeriod, Resolution.Daily);
            */

            // Adds the future that will be traded and
            // set our expiry filter for this futures chain
            _Symbol = AddFuture(Futures.Indices.SP500EMini, Resolution.Minute);
            _Symbol.SetFilter(TimeSpan.FromDays(10), TimeSpan.FromDays(45));
            var con = ResolveConsolidator(_Symbol.Symbol, new TimeSpan(0, 1, 0));
            con.DataConsolidated += Con_DataConsolidated;

            _devIndicator = MAD(_Symbol.Symbol, _fastPeriod, Resolution.Minute);
        }

        void Con_DataConsolidated(object sender, IBaseData consolidated)
        {
            Console.WriteLine("log" + consolidated.Time.ToString());
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            Log(orderEvent.ToString());

            if (orderEvent.OrderId == _pendingOrderId &&
                orderEvent.Status == OrderStatus.Filled)
            {
                Console.WriteLine("entry fill! " + orderEvent.FillQuantity);
                //submit bracket orders
                SetupBracket(orderEvent);
            }
        }

        public override void OnData(Slice slice)
        {
            Console.WriteLine("log_ondata" + slice.Time.ToString() + " " + slice.FutureChains.Count);

            if (!Portfolio.Invested)
            {
                Console.WriteLine("dev=" + _devIndicator.Current.Value);
                if (_devIndicator.Current.Value < 10)
                {
                    this.SetupOrder();
                }
            }
            /*
            if (!Portfolio.Invested) // && IsUpTrend)
            {
                foreach (var chain in slice.FutureChains)
                {
                    // find the front contract expiring no earlier than in 90 days
                    var contract = (
                        from futuresContract in chain.Value.OrderBy(x => x.Expiry)
                        where futuresContract.Expiry > Time.Date.AddDays(90)
                        select futuresContract
                        ).FirstOrDefault();

                    // if found, trade it
                    if (contract != null)
                    {
                        MarketOrder(contract.Symbol, 1);
                    }
                }
            }

            if (Portfolio.Invested && IsDownTrend)
            {
                Liquidate();
            }
            */
        }

        public void SetupBracket(OrderEvent orderEvent)
        {
            int quantity = 1;
            decimal profit = 0; decimal loss = 0;

            if (orderEvent.Direction == OrderDirection.Buy)
            {
                profit = orderEvent.FillPrice + _profitSpread;
                loss = orderEvent.FillPrice - _lossSpread;
            }
            else if (orderEvent.Direction == OrderDirection.Sell)
            {
                profit = orderEvent.FillPrice - _profitSpread;
                loss = orderEvent.FillPrice + _lossSpread;
            }

            this.StopLimitOrder(_Symbol.Symbol, quantity, loss, loss);
            this.LimitOrder(_Symbol.Symbol, quantity, profit);
        }

        public void SetupOrder()
        {
            var chain = this.CurrentSlice.FutureChains.First();

            // find the front contract expiring no earlier than in 90 days
            var contract = (
                from futuresContract in chain.Value.OrderBy(x => x.Expiry)
                where futuresContract.Expiry > Time.Date.AddDays(90)
                select futuresContract
                ).FirstOrDefault();

            // if found, trade it
            if (contract != null)
            {
                //MarketOrder(contract.Symbol, 1);
                var order = this.LimitOrder(_Symbol.Symbol, 1, contract.BidPrice - 1);
                _pendingOrderId = order.OrderId;
            }
        }

        public override void OnEndOfDay()
        {
            Plot("Indicator Signal", "EOD", IsDownTrend ? -1 : IsUpTrend ? 1 : 0);
        }
    }
}