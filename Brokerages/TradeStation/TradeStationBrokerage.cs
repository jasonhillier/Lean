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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Securities.Equity;
using QuantConnect.Util;
using RestSharp;

namespace QuantConnect.Brokerages.TradeStation
{
    /// <summary>
    /// Tradier Class:
    ///  - Handle authentication.
    ///  - Data requests.
    ///  - Rate limiting.
    ///  - Placing orders.
    ///  - Getting user data.
    /// </summary>
    public partial class TradeStationBrokerage : Brokerage, IDataQueueHandler, IHistoryProvider, IDataQueueUniverseProvider
    {
        private readonly string _accountID;
        private List<string> _accountKeys;
        private readonly string _accessToken;
        private TradeStationClient _tradeStationClient;
        private bool _isConnected;

        // we're reusing the equity exchange here to grab typical exchange hours
        private static readonly EquityExchange Exchange =
            new EquityExchange(MarketHoursDatabase.FromDataFolder().GetExchangeHours(Market.USA, null, SecurityType.Equity));

        // polling timers for refreshing access tokens and checking for fill events
        private Timer _refreshTimer;
        private Timer _orderFillTimer;

        //Endpoints:
        private readonly IOrderProvider _orderProvider;
        private readonly ISecurityProvider _securityProvider;

        private readonly object _fillLock = new object();
        private readonly DateTime _initializationDateTime = DateTime.Now;
        //private readonly ConcurrentDictionary<long, TradierCachedOpenOrder> _cachedOpenOrdersByTradierOrderID;
        // this is used to block reentrance when doing look ups for orders with IDs we don't have cached
        private readonly HashSet<long> _reentranceGuardByTradierOrderID = new HashSet<long>();
        private readonly FixedSizeHashQueue<long> _filledTradierOrderIDs = new FixedSizeHashQueue<long>(10000);
        // this is used to handle the zero crossing case, when the first order is filled we'll submit the next order
        //private readonly ConcurrentDictionary<long, ContingentOrderQueue> _contingentOrdersByQCOrderID = new ConcurrentDictionary<long, ContingentOrderQueue>();
        // this is used to block reentrance when handling contingent orders
        private readonly HashSet<long> _contingentReentranceGuardByQCOrderID = new HashSet<long>();
        private readonly HashSet<long> _unknownTradierOrderIDs = new HashSet<long>();
        private readonly FixedSizeHashQueue<long> _verifiedUnknownTradierOrderIDs = new FixedSizeHashQueue<long>(1000);
        private readonly FixedSizeHashQueue<int> _cancelledQcOrderIDs = new FixedSizeHashQueue<int>(10000);

        /// <summary>
        /// Access Token Access:
        /// </summary>
        public string AccessToken { get; private set; }

        /// <summary>
        /// The QC User id, used for refreshing the session
        /// </summary>
        //public int UserId { get; private set; }

        /// <summary>
        /// Create a new Tradier Object:
        /// </summary>
        public TradeStationBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider, string accountID, string accessToken, bool simulation)
            : base("TradeStation Brokerage")
        {
            _orderProvider = orderProvider;
            _securityProvider = securityProvider;
            _accountID = accountID;
            _accountKeys = new List<string>() { accountID };
            _accessToken = accessToken;

            _tradeStationClient = new TradeStationClient();
            if (simulation)
                _tradeStationClient.BaseUrl = "https://sim-api.tradestation.com/v2";

            //_cachedOpenOrdersByTradierOrderID = new ConcurrentDictionary<long, TradierCachedOpenOrder>();
        }

		bool IsMarketHoursForSymbols(IEnumerable<Symbol> symbols)
		{
			foreach(var symbol in symbols)
			{
				var exchangeHours = MarketHoursDatabase.FromDataFolder()
							.GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType);

				var now = DateTime.UtcNow.ConvertFromUtc(exchangeHours.TimeZone);

				if (exchangeHours.IsOpen(now, false))
					return true;
			}

			return false; //nothing is open
		}

        #region IBrokerage implementation

        /// <summary>
        /// Returns true if we're currently connected to the broker
        /// </summary>
        public override bool IsConnected
        {
            get { return _isConnected; }
        }

        /// <summary>
        /// Gets all open orders on the account.
        /// NOTE: The order objects returned do not have QC order IDs.
        /// </summary>
        /// <returns>The open orders returned from IB</returns>
        public override List<Order> GetOpenOrders()
        {
            var orders = new List<Order>();
			
            var tOrders = _tradeStationClient.GetOrdersByAccountsAsync(_accessToken, null, _accountKeys, "10", "1").Result;
            foreach(var tOrder in tOrders)
            {
                var pOrder = ConvertOrder(tOrder);
                if (pOrder.Status == OrderStatus.Submitted ||
                    pOrder.Status == OrderStatus.PartiallyFilled ||
                    pOrder.Status == OrderStatus.New)
                {
                    orders.Add(pOrder);
                }
            }
			
            return orders;
        }

        /// <summary>
        /// Gets all holdings for the account
        /// </summary>
        /// <returns>The current holdings from the account</returns>
        public override List<Holding> GetAccountHoldings()
        {
            List<Holding> holdings = new List<Holding>();

            var tPositions = _tradeStationClient.GetPositionsByAccountsAsync(_accessToken, _accountKeys, null).Result;
            foreach (var tPos in tPositions)
            {
                holdings.Add(ConvertHolding(tPos));
                /*
                var holdings = GetPositions().Select(ConvertHolding).Where(x => x.Quantity != 0).ToList();
                var symbols = holdings.Select(x => x.Symbol.Value).ToList();
                var quotes = GetQuotes(symbols).ToDictionary(x => x.Symbol);
                foreach (var holding in holdings)
                {
                    TradierQuote quote;
                    if (quotes.TryGetValue(holding.Symbol.Value, out quote))
                    {
                        holding.MarketPrice = quote.Last;
                    }
                }
                */
            }
            return holdings;
        }

        public System.Collections.ObjectModel.ObservableCollection<Anonymous3> GetQuotes(List<string> symbols)
        {
            return _tradeStationClient.GetQuotesAsync(_accessToken, String.Join(",", symbols)).Result;
        }

        /// <summary>
        /// Gets the current cash balance for each currency held in the brokerage account
        /// </summary>
        /// <returns>The current cash balance for each currency available for trading</returns>
        public override List<Cash> GetCashBalance()
        {
            List<Cash> balances = new List<Cash>();
            var tBalances = _tradeStationClient.GetBalancesByAccountsAsync(_accessToken, _accountKeys).Result;
            foreach(var tBalance in tBalances)
            {
                balances.Add(new Cash("USD", (decimal)tBalance.BODNetCash, 1));
            }
            return balances;
        }

        /// <summary>
        /// Places a new order and assigns a new broker ID to the order
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <returns>True if the request for a new order has been placed, false otherwise</returns>
        public override bool PlaceOrder(Order order)
        {
            Log.Trace("TradeStation.PlaceOrder(): " + order);

            if (_cancelledQcOrderIDs.Contains(order.Id))
            {
                Log.Trace("TradeStation.PlaceOrder(): Cancelled Order: " + order.Id + " - " + order);
                return false;
            }

            var tsOrder = new OrderRequestDefinition();
            tsOrder.Symbol = ConvertToTradestationSymbol(order.Symbol);
            if (order.Symbol.SecurityType == SecurityType.Option)
                tsOrder.AssetType = OrderRequestDefinitionAssetType.OP;
            else
                tsOrder.AssetType = OrderRequestDefinitionAssetType.EQ;
            tsOrder.AccountKey = _accountKeys[0];
            tsOrder.Quantity = order.AbsoluteQuantity.ToString();
            if (order.Direction == OrderDirection.Buy)
                tsOrder.TradeAction = OrderRequestDefinitionTradeAction.BUY;
            else
                tsOrder.TradeAction = OrderRequestDefinitionTradeAction.SELL;
            tsOrder.OrderConfirmId = Guid.NewGuid().ToString();
            if (order.TimeInForce == TimeInForce.Day)
            {
                tsOrder.Duration = OrderRequestDefinitionDuration.DAY;
            }
            else
            {
                tsOrder.Duration = OrderRequestDefinitionDuration.GTC;
            }

            switch (order.Type)
            {
                case Orders.OrderType.Limit:
                    tsOrder.LimitPrice = ((LimitOrder)order).LimitPrice.ToString();
                    tsOrder.OrderType = OrderRequestDefinitionOrderType.Limit;
                    break;
                case Orders.OrderType.Market:
                    tsOrder.OrderType = OrderRequestDefinitionOrderType.Market;
                    break;
                default:
                    throw new Exception("Order type not supported!");
            }

            var response = _tradeStationClient.PostOrderAsync(_accessToken, tsOrder).Result;

            Log.Trace("TradeStation.PlaceOrder(): " + response.Message);

            return response.OrderStatus == OrderResponseDefinitionOrderStatus.Ok;

            /*

            // before doing anything, verify only one outstanding order per symbol
            var cachedOpenOrder = _cachedOpenOrdersByTradierOrderID.FirstOrDefault(x => x.Value.Order.Symbol == order.Symbol.Value).Value;
            if (cachedOpenOrder != null)
            {
                var qcOrder = _orderProvider.GetOrderByBrokerageId(cachedOpenOrder.Order.Id);
                if (qcOrder == null)
                {
                    // clean up our mess, this should never be encountered.
                    TradierCachedOpenOrder tradierOrder;
                    Log.Error("TradierBrokerage.PlaceOrder(): Unable to locate existing QC Order when verifying single outstanding order per symbol.");
                    _cachedOpenOrdersByTradierOrderID.TryRemove(cachedOpenOrder.Order.Id, out tradierOrder);
                }
                // if the qc order is still listed as open, then we have an issue, attempt to cancel it before placing this new order
                else if (qcOrder.Status.IsOpen())
                {
                    // let the world know what we're doing
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "OneOrderPerSymbol",
                        "Tradier Brokerage currently only supports one outstanding order per symbol. Canceled old order: " + qcOrder.Id)
                        );

                    // cancel the open order and clear out any contingents
                    ContingentOrderQueue contingent;
                    _contingentOrdersByQCOrderID.TryRemove(qcOrder.Id, out contingent);
                    // don't worry about the response here, if it couldn't be canceled it was
                    // more than likely already filled, either way we'll trust we're clean to proceed
                    // with this new order
                    CancelOrder(qcOrder);
                }
            }

            var holdingQuantity = _securityProvider.GetHoldingsQuantity(order.Symbol);

            var orderRequest = new TradierPlaceOrderRequest(order, TradierOrderClass.Equity, holdingQuantity);

            // do we need to split the order into two pieces?
            bool crossesZero = OrderCrossesZero(order);
            if (crossesZero)
            {
                // first we need an order to close out the current position
                var firstOrderQuantity = -holdingQuantity;
                var secondOrderQuantity = order.Quantity - firstOrderQuantity;

                orderRequest.Quantity = Math.Abs(firstOrderQuantity);

                // we actually can't place this order until the closingOrder is filled
                // create another order for the rest, but we'll convert the order type to not be a stop
                // but a market or a limit order
                var restOfOrder = new TradierPlaceOrderRequest(order, TradierOrderClass.Equity, 0) { Quantity = Math.Abs(secondOrderQuantity) };
                restOfOrder.ConvertStopOrderTypes();

                _contingentOrdersByQCOrderID.AddOrUpdate(order.Id, new ContingentOrderQueue(order, restOfOrder));

                // issue the first order to close the position
                var response = TradierPlaceOrder(orderRequest);
                bool success = response.Errors.Errors.IsNullOrEmpty();
                if (!success)
                {
                    // remove the contingent order if we weren't succesful in placing the first
                    ContingentOrderQueue contingent;
                    _contingentOrdersByQCOrderID.TryRemove(order.Id, out contingent);
                    return false;
                }

                var closingOrderID = response.Order.Id;
                order.BrokerId.Add(closingOrderID.ToString());
                return true;
            }
            else
            {
                var response = TradierPlaceOrder(orderRequest);
                if (!response.Errors.Errors.IsNullOrEmpty())
                {
                    return false;
                }
                order.BrokerId.Add(response.Order.Id.ToString());
                return true;
            }
            */
        }

        /// <summary>
        /// Updates the order with the same id
        /// </summary>
        /// <param name="order">The new order information</param>
        /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
        public override bool UpdateOrder(Order order)
        {
            return false;
            /*
            Log.Trace("TradierBrokerage.UpdateOrder(): " + order);

            if (!order.BrokerId.Any())
            {
                // we need the brokerage order id in order to perform an update
                Log.Trace("TradierBrokerage.UpdateOrder(): Unable to update order without BrokerId.");
                return false;
            }

            // there's only one active tradier order per qc order, find it
            var activeOrder = (
                from brokerId in order.BrokerId
                let id = long.Parse(brokerId)
                where _cachedOpenOrdersByTradierOrderID.ContainsKey(id)
                select _cachedOpenOrdersByTradierOrderID[id]
                ).SingleOrDefault();

            if (activeOrder == null)
            {
                Log.Trace("Unable to locate active Tradier order for QC order id: " + order.Id + " with Tradier ids: " + string.Join(", ", order.BrokerId));
                return false;
            }

            decimal quantity = activeOrder.Order.Quantity;

            // also sum up the contingent orders
            ContingentOrderQueue contingent;
            if (_contingentOrdersByQCOrderID.TryGetValue(order.Id, out contingent))
            {
                quantity = contingent.QCOrder.AbsoluteQuantity;
            }

            if (quantity != order.AbsoluteQuantity)
            {
                Log.Trace("TradierBrokerage.UpdateOrder(): Unable to update order quantity.");
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UpdateRejected", "Unable to modify Tradier order quantities."));
                return false;
            }

            // we only want to update the active order, and if successful, we'll update contingents as well in memory

            var orderType = ConvertOrderType(order.Type);
            var orderDuration = GetOrderDuration(order.Duration);
            var limitPrice = GetLimitPrice(order);
            var stopPrice = GetStopPrice(order);
            var response = ChangeOrder(_accountID, activeOrder.Order.Id,
                orderType,
                orderDuration,
                limitPrice,
                stopPrice
                );

            if (!response.Errors.Errors.IsNullOrEmpty())
            {
                string errors = string.Join(", ", response.Errors.Errors);
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UpdateFailed", "Failed to update Tradier order id: " + activeOrder.Order.Id + ". " + errors));
                return false;
            }

            // success
            OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0) { Status = OrderStatus.Submitted });

            // if we have contingents, update them as well
            if (contingent != null)
            {
                foreach (var orderRequest in contingent.Contingents)
                {
                    orderRequest.Type = orderType;
                    orderRequest.Duration = orderDuration;
                    orderRequest.Price = limitPrice;
                    orderRequest.Stop = stopPrice;
                    orderRequest.ConvertStopOrderTypes();
                }
            }

            return true;
            */
        }

        /// <summary>
        /// Cancels the order with the specified ID
        /// </summary>
        /// <param name="order">The order to cancel</param>
        /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
        public override bool CancelOrder(Order order)
        {
            var result = _tradeStationClient.CancelOrderAsync(_accessToken, order.Id.ToString()).Result;
            return (result.OrderStatus == OrderResponseDefinitionOrderStatus.Ok);
            /*
            Log.Trace("TradierBrokerage.CancelOrder(): " + order);

            if (!order.BrokerId.Any())
            {
                Log.Trace("TradierBrokerage.CancelOrder(): Unable to cancel order without BrokerId.");
                return false;
            }

            // remove any contingent orders
            ContingentOrderQueue contingent;
            _contingentOrdersByQCOrderID.TryRemove(order.Id, out contingent);

            // add this id to the cancelled list, this is to prevent resubmits of certain simulated order
            // types, such as market on close
            _cancelledQcOrderIDs.Add(order.Id);

            foreach (var orderID in order.BrokerId)
            {
                var id = long.Parse(orderID);
                var response = CancelOrder(_accountID, id);
                if (response == null)
                {
                    // this can happen if the order has already been filled
                    return false;
                }
                if (response.Errors.Errors.IsNullOrEmpty() && response.Order.Status == "ok")
                {
                    TradierCachedOpenOrder tradierOrder;
                    _cachedOpenOrdersByTradierOrderID.TryRemove(id, out tradierOrder);
                    const int orderFee = 0;
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, orderFee, "Tradier Fill Event") { Status = OrderStatus.Canceled });
                }
            }

            return true;
            */
        }

        /// <summary>
        /// Connects the client to the broker's remote servers
        /// </summary>
        public override void Connect()
        {
            _isConnected = true;
        }

        /// <summary>
        /// Disconnects the client from the broker's remote servers
        /// </summary>
        public override void Disconnect()
        {
            _isConnected = false;
        }

        /// <summary>
        /// Event invocator for the Message event
        /// </summary>
        /// <param name="e">The error</param>
        protected override void OnMessage(BrokerageMessageEvent e)
        {
            var message = e;
            if (Exchange.DateTimeIsOpen(DateTime.Now) && ErrorsDuringMarketHours.Contains(e.Code))
            {
                // elevate this to an error
                message = new BrokerageMessageEvent(BrokerageMessageType.Error, e.Code, e.Message);
            }
            base.OnMessage(message);
        }

        private readonly HashSet<string> ErrorsDuringMarketHours = new HashSet<string>
        {
            "CheckForFillsError", "UnknownIdResolution", "ContingentOrderError", "NullResponse", "PendingOrderNotReturned"
        };

        /*
        private TradierOrderResponse TradierPlaceOrder(TradierPlaceOrderRequest order)
        {
            const TradierOrderClass classification = TradierOrderClass.Equity;

            string stopLimit = string.Empty;
            if (order.Price != 0 || order.Stop != 0)
            {
                stopLimit = string.Format(" at{0}{1}",
                    order.Stop == 0 ? "" : " stop " + order.Stop,
                    order.Price == 0 ? "" : " limit " + order.Price
                    );
            }

            Log.Trace(string.Format("TradierBrokerage.TradierPlaceOrder(): {0} to {1} {2} units of {3}{4}",
                order.Type, order.Direction, order.Quantity, order.Symbol, stopLimit)
                );

            var response = PlaceOrder(_accountID,
                order.Classification,
                order.Direction,
                order.Symbol,
                order.Quantity,
                order.Price,
                order.Stop,
                order.OptionSymbol,
                order.Type,
                order.Duration
                );

            // if no errors, add to our open orders collection
            if (response != null && response.Errors.Errors.IsNullOrEmpty())
            {
                // send the submitted event
                const int orderFee = 0;
                order.QCOrder.PriceCurrency = "USD";
                OnOrderEvent(new OrderEvent(order.QCOrder, DateTime.UtcNow, orderFee) { Status = OrderStatus.Submitted });

                // mark this in our open orders before we submit so it's gauranteed to be there when we poll for updates
                UpdateCachedOpenOrder(response.Order.Id, new TradierOrderDetailed
                {
                    Id = response.Order.Id,
                    Quantity = order.Quantity,
                    Status = TradierOrderStatus.Submitted,
                    Symbol = order.Symbol,
                    Type = order.Type,
                    TransactionDate = DateTime.Now,
                    AverageFillPrice = 0m,
                    Class = classification,
                    CreatedDate = DateTime.Now,
                    Direction = order.Direction,
                    Duration = order.Duration,
                    LastFillPrice = 0m,
                    LastFillQuantity = 0m,
                    Legs = new List<TradierOrderLeg>(),
                    NumberOfLegs = 0,
                    Price = order.Price,
                    QuantityExecuted = 0m,
                    RemainingQuantity = order.Quantity,
                    StopPrice = order.Stop
                });
            }
            else
            {
                // invalidate the order, bad request
                const int orderFee = 0;
                OnOrderEvent(new OrderEvent(order.QCOrder, DateTime.UtcNow, orderFee) { Status = OrderStatus.Invalid });

                string message = _previousResponseRaw;
                if (response != null && response.Errors != null && !response.Errors.Errors.IsNullOrEmpty())
                {
                    message = "Order " + order.QCOrder.Id + ": " + string.Join(Environment.NewLine, response.Errors.Errors);
                    if (string.IsNullOrEmpty(order.QCOrder.Tag))
                    {
                        order.QCOrder.Tag = message;
                    }
                }

                // send this error through to the console
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "OrderError", message));

                // if we weren't given a broker ID, make an async request to fetch it and set the broker ID property on the qc order
                if (response == null || response.Order == null || response.Order.Id == 0)
                {
                    Task.Run(() =>
                    {
                        var orders = GetIntradayAndPendingOrders()
                            .Where(x => x.Status == TradierOrderStatus.Rejected)
                            .Where(x => DateTime.UtcNow - x.TransactionDate < TimeSpan.FromSeconds(2));

                        var recentOrder = orders.OrderByDescending(x => x.TransactionDate).FirstOrDefault(x => x.Symbol == order.Symbol && x.Quantity == order.Quantity && x.Direction == order.Direction && x.Type == order.Type);
                        if (recentOrder == null)
                        {
                            // without this we're going to corrupt the algorithm state
                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "OrderError", "Unable to resolve rejected Tradier order id for QC order: " + order.QCOrder.Id));
                            return;
                        }

                        order.QCOrder.BrokerId.Add(recentOrder.Id.ToString());
                        Log.Trace("TradierBrokerage.TradierPlaceOrder(): Successfully resolved missing order ID: " + recentOrder.Id);
                    });
                }
            }

            return response;
        }
        */

        /// <summary>
        /// Checks for fill events by polling FetchOrders for pending orders and diffing against the last orders seen
        /// </summary>
        private void CheckForFills()
        {
            //TODO
        }

        /*
        private bool IsUnknownOrderID(KeyValuePair<long, TradierOrder> x)
        {
            // we don't have it in our local cache
            return !_cachedOpenOrdersByTradierOrderID.ContainsKey(x.Key)
                // the transaction happened after we initialized, make sure they're in the same time zone
                && x.Value.TransactionDate.ToUniversalTime() > _initializationDateTime.ToUniversalTime()
                // we don't have a record of it in our last 10k filled orders
                && !_filledTradierOrderIDs.Contains(x.Key);
        }

        private void ProcessPotentiallyUpdatedOrder(TradierCachedOpenOrder cachedOrder, TradierOrder updatedOrder)
        {
            // check for fills or status changes, for either fire a fill event
            if (updatedOrder.RemainingQuantity != cachedOrder.Order.RemainingQuantity
             || ConvertStatus(updatedOrder.Status) != ConvertStatus(cachedOrder.Order.Status))
            {
                var qcOrder = _orderProvider.GetOrderByBrokerageId(updatedOrder.Id);
                qcOrder.PriceCurrency = "USD";
                const int orderFee = 0;
                var fill = new OrderEvent(qcOrder, DateTime.UtcNow, orderFee, "Tradier Fill Event")
                {
                    Status = ConvertStatus(updatedOrder.Status),
                    // this is guaranteed to be wrong in the event we have multiple fills within our polling interval,
                    // we're able to partially cope with the fill quantity by diffing the previous info vs current info
                    // but the fill price will always be the most recent fill, so if we have two fills with 1/10 of a second
                    // we'll get the latter fill price, so for large orders this can lead to inconsistent state
                    FillPrice = updatedOrder.LastFillPrice,
                    FillQuantity = (int)(updatedOrder.QuantityExecuted - cachedOrder.Order.QuantityExecuted)
                };

                // flip the quantity on sell actions
                if (IsShort(updatedOrder.Direction))
                {
                    fill.FillQuantity *= -1;
                }

                if (!cachedOrder.EmittedOrderFee)
                {
                    cachedOrder.EmittedOrderFee = true;
                    var security = _securityProvider.GetSecurity(qcOrder.Symbol);
                    fill.OrderFee = security.FeeModel.GetOrderFee(security, qcOrder);
                }

                // if we filled the order and have another contingent order waiting, submit it
                ContingentOrderQueue contingent;
                if (fill.Status == OrderStatus.Filled && _contingentOrdersByQCOrderID.TryGetValue(qcOrder.Id, out contingent))
                {
                    // prevent submitting the contingent order multiple times
                    if (_contingentReentranceGuardByQCOrderID.Add(qcOrder.Id))
                    {
                        var order = contingent.Next();
                        if (order == null || contingent.Contingents.Count == 0)
                        {
                            // we've finished with this contingent order
                            _contingentOrdersByQCOrderID.TryRemove(qcOrder.Id, out contingent);
                        }
                        // fire this off in a task so we don't block this thread
                        if (order != null)
                        {
                            // if we have a contingent that needs to be submitted then we can't respect the 'Filled' state from the order
                            // because the QC order hasn't been technically filled yet, so mark it as 'PartiallyFilled'
                            fill.Status = OrderStatus.PartiallyFilled;

                            Task.Run(() =>
                            {
                                try
                                {
                                    Log.Trace("TradierBrokerage.SubmitContingentOrder(): Submitting contingent order for QC id: " + qcOrder.Id);

                                    var response = TradierPlaceOrder(order);
                                    if (response.Errors.Errors.IsNullOrEmpty())
                                    {
                                        // add the new brokerage id for retrieval later
                                        qcOrder.BrokerId.Add(response.Order.Id.ToString());
                                    }
                                    else
                                    {
                                        // if we failed to place this order I don't know what to do, we've filled the first part
                                        // and failed to place the second... strange. Should we invalidate the rest of the order??
                                        Log.Error("TradierBrokerage.SubmitContingentOrder(): Failed to submit contingent order.");
                                        var message = string.Format("{0} Failed submitting contingent order for QC id: {1} Filled Tradier Order id: {2}", qcOrder.Symbol, qcOrder.Id, updatedOrder.Id);
                                        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "ContingentOrderFailed", message));
                                        OnOrderEvent(new OrderEvent(qcOrder, DateTime.UtcNow, orderFee) { Status = OrderStatus.Canceled });
                                    }
                                }
                                catch (Exception err)
                                {
                                    Log.Error(err);
                                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "ContingentOrderError", "An error ocurred while trying to submit an Tradier contingent order: " + err));
                                    OnOrderEvent(new OrderEvent(qcOrder, DateTime.UtcNow, orderFee) { Status = OrderStatus.Canceled });
                                }
                                finally
                                {
                                    _contingentReentranceGuardByQCOrderID.Remove(qcOrder.Id);
                                }
                            });
                        }
                    }
                }

                OnOrderEvent(fill);
            }

            // remove from open orders since it's now closed
            if (OrderIsClosed(updatedOrder))
            {
                _filledTradierOrderIDs.Add(updatedOrder.Id);
                _cachedOpenOrdersByTradierOrderID.TryRemove(updatedOrder.Id, out cachedOrder);
            }
        }

        private void UpdateCachedOpenOrder(long key, TradierOrder updatedOrder)
        {
            TradierCachedOpenOrder cachedOpenOrder;
            if (_cachedOpenOrdersByTradierOrderID.TryGetValue(key, out cachedOpenOrder))
            {
                cachedOpenOrder.Order = updatedOrder;
            }
            else
            {
                _cachedOpenOrdersByTradierOrderID[key] = new TradierCachedOpenOrder(updatedOrder);
            }
        }
        */

        #endregion

        #region Conversion routines

        /// <summary>
        /// Converts the specified tradier order into a qc order.
        /// The 'task' will have a value if we needed to issue a rest call for the stop price, otherwise it will be null
        /// </summary>
        protected Order ConvertOrder(Anonymous8 order)
        {
            Order qcOrder;
            qcOrder = new LimitOrder() {LimitPrice = (decimal)order.LimitPrice };
            //TODO
            /*
            switch (order.OrderType)
            {
                case Type2
                    qcOrder = new LimitOrder { LimitPrice = order.Price };
                    break;
                case TradierOrderType.Market:
                    qcOrder = new MarketOrder();
                    break;
                case TradierOrderType.StopMarket:
                    qcOrder = new StopMarketOrder { StopPrice = GetOrder(order.Id).StopPrice };
                    break;
                case TradierOrderType.StopLimit:
                    qcOrder = new StopLimitOrder { LimitPrice = order.Price, StopPrice = GetOrder(order.Id).StopPrice };
                    break;

                //case TradierOrderType.Credit:
                //case TradierOrderType.Debit:
                //case TradierOrderType.Even:
                default:
                    throw new NotImplementedException("The Tradier order type " + order.Type + " is not implemented.");
            }
            */
            qcOrder.Symbol = Symbol.Create(order.Symbol, SecurityType.Equity, Market.USA);
            qcOrder.Quantity = ConvertQuantity(order);
            qcOrder.Status = ConvertStatus(order.Status);
            qcOrder.Id = Int32.Parse(order.OrderID.ToString());
            //qcOrder.BrokerId.Add(order.OrderID.ToString());
            //qcOrder.ContingentId =
            qcOrder.Properties.TimeInForce = ConvertDuration(order.Duration);
            /*var orderByBrokerageId = _orderProvider.GetOrderByBrokerageId(order.OrderID.ToString());
            if (orderByBrokerageId != null)
            {
                qcOrder.Id = orderByBrokerageId.Id;
            }
            */
            qcOrder.Time = DateTime.Parse(order.TimeStamp); //TransactionDate;
            return qcOrder;
        }

        /// <summary>
        /// Converts the tradier order duration into a qc order duration
        /// </summary>
        protected TimeInForce ConvertDuration(string duration)
        {
            switch(duration)
            {
                case "Day":
                    return TimeInForce.Day;
                default:
                    return TimeInForce.GoodTilCanceled;
            }
        }

        /// <summary>
        /// Converts the tradier order status into a qc order status
        /// </summary>
        protected OrderStatus ConvertStatus(Status2 status)
        {
            //TODO
            switch (status)
            {
                case Status2.FLL:
                    return OrderStatus.Filled;

                case Status2.CAN:
                case Status2.OUT:
                    return OrderStatus.Canceled;

                //case Status2.OPN:
                case Status2.ACK:
                case Status2.DON:
                    return OrderStatus.Submitted;

                case Status2.Exp:
                case Status2.REJ:
                    return OrderStatus.Invalid;

                case Status2.OPN:
                    return OrderStatus.New;

                case Status2.FLP:
                    return OrderStatus.PartiallyFilled;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Converts the tradier order quantity into a qc quantity
        /// </summary>
        /// <remarks>
        /// Tradier quantities are always positive and use the direction to denote +/-, where as qc
        /// order quantities determine the direction
        /// </remarks>
        protected int ConvertQuantity(Anonymous8 order)
        {
            switch (order.Type)
            {
                case Type2.Buy:
                    return (int)order.Quantity;

                case Type2.Sell:
                    return -(int)order.Quantity;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected string ConvertToTradestationSymbol(Symbol pSymbol)
        {
            if (pSymbol.SecurityType == SecurityType.Option)
            {
                if (_optionNameResolver.ContainsKey(pSymbol))
                    return _optionNameResolver[pSymbol];

                //TODO: build converter
                throw new Exception("TradeStation.ConvertToTradestationSymbol: Could not resolve option symbol!");
            }
            else
            {
                return pSymbol.Value;
            }
        }

        protected Symbol ConvertSymbol(string pSymbolName, AssetType2 pAssetType, string pMarket = Market.USA, DateTime? pExpDate = null, OptionRight? pOptionRight = null, double pStrike = 0)
        {
            switch(pAssetType)
            {
                case AssetType2.Fu:
                    return Symbol.CreateFuture(pSymbolName, pMarket, (DateTime)pExpDate);

                case AssetType2.Op:
                    //TODO: need to convert
                    return Symbol.CreateOption(pSymbolName, pMarket, OptionStyle.American, (OptionRight)pOptionRight, (decimal)pStrike, (DateTime)pExpDate);

                case AssetType2.EQ:
                default:
                    return Symbol.Create(pSymbolName, SecurityType.Equity, pMarket);
            }
        }

        /// <summary>
        /// Converts the tradier position into a qc holding
        /// </summary>
        protected Holding ConvertHolding(Anonymous7 position)
        {
            DateTime expDate = DateTime.MinValue;
            DateTime.TryParse(position.ContractExpireDate, out expDate);

            Symbol symbol = _optionNameResolver.FirstOrDefault(x => x.Value == position.Symbol).Key;

            if (symbol == null)
            {
                symbol = ConvertSymbol(
                        position.Symbol,
                        position.AssetType,
                        Market.USA,
                        expDate,
                    OptionRight.Put, //TODO: resolve this
                    position.StrikePrice);
            }

            return new Holding
            {
                Symbol = symbol,
                Type = symbol.SecurityType,
                AveragePrice = (decimal)position.AccountTotalCost / (decimal)position.Quantity,
                ConversionRate = 1.0m,
                CurrencySymbol = "$",
                MarketPrice = (decimal)position.AveragePrice,
                Quantity = (decimal)position.Quantity
            };
        }

        #endregion
    }
}
