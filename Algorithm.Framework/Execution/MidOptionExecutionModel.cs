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
using System.Linq;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Orders;

namespace QuantConnect.Algorithm.Framework.Execution
{
    /// <summary>
    /// Provides an implementation of <see cref="IExecutionModel"/> that immediately submits
    /// market orders to achieve the desired portfolio targets
    /// </summary>
    public class MidOptionExecutionModel : ExecutionModel
    {
        private readonly PortfolioTargetCollection _targetsCollection = new PortfolioTargetCollection();

        /// <summary>
        /// Immediately submits orders for the specified portfolio targets.
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="targets">The portfolio targets to be ordered</param>
        public override void Execute(QCAlgorithmFramework algorithm, IPortfolioTarget[] targets)
        {
            _targetsCollection.AddRange(targets);

            foreach (var target in _targetsCollection.OrderByMarginImpact(algorithm))
            {
                var existing = algorithm.Securities[target.Symbol].Holdings.Quantity
                    + algorithm.Transactions.GetOpenOrders(target.Symbol).Sum(o => o.Quantity);
                var quantity = target.Quantity - existing;

                if (quantity != 0)
                {
                    if (target.Quantity == 0)
                    {
                        //CLOSING a position, we want to do so immediately
                        algorithm.MarketOrder(target.Symbol, quantity);
                    }
                    else
                    {
                        //Adding or entering new position
                        QuoteBar quote = algorithm.CurrentSlice[target.Symbol];
                        algorithm.LimitOrder(target.Symbol, quantity, Math.Round(quote.Bid.Close + (quote.Ask.Close - quote.Bid.Close) / 2, 2));
                    }
                }
            }

            //TODO: try resubmitting the order to closer to the spread as time goes by before cancelling/market ordering

            //convert to market order within 30 mins
            foreach(var order in algorithm.Transactions.GetOpenOrders(o => (algorithm.CurrentSlice.Time - o.CreatedTime).TotalMinutes >= 30))
            {
                algorithm.Transactions.CancelOrder(order.Id);
                algorithm.MarketOrder(order.Symbol, order.Quantity);
            }

            _targetsCollection.Clear();
        }

        /// <summary>
        /// Event fired each time the we add/remove securities from the data feed
        /// </summary>
        /// <param name="algorithm">The algorithm instance that experienced the change in securities</param>
        /// <param name="changes">The security additions and removals from the algorithm</param>
        public override void OnSecuritiesChanged(QCAlgorithmFramework algorithm, SecurityChanges changes)
        {
        }
    }
}