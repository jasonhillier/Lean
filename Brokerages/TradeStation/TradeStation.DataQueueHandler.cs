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
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodaTime;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using QuantConnect.Packets;
using QuantConnect.Securities;
using RestSharp;
using Timer = System.Timers.Timer;

namespace QuantConnect.Brokerages.TradeStation
{
    /// <summary>
    /// Tradier Class: IDataQueueHandler implementation
    /// </summary>
    public partial class TradeStationBrokerage
    {
        #region IDataQueueHandler implementation

        private volatile bool _refresh = true;
        private Timer _refreshDelay = new Timer();
        private readonly ConcurrentDictionary<Symbol, string> _subscriptions = new ConcurrentDictionary<Symbol, string>();
		private Dictionary<Symbol, string> _optionNameResolver = new Dictionary<Symbol, string>();
        private Stream _tradestationStream;

        /// <summary>
        /// Get a stream of ticks from the brokerage
        /// </summary>
        /// <returns>IEnumerable of BaseData</returns>
        public IEnumerable<BaseData> GetNextTicks()
        {
            IEnumerator<QuoteStreamDefinition> pipe = null;
            do
            {
                if (_subscriptions.IsEmpty)
                {
                    Thread.Sleep(100);
                    continue;
                }

                //If there's been an update to the subscriptions list; recreate the stream.
                if (_refresh)
                {
                    _refresh = false;
                    var stream = Stream();
                    pipe = stream.GetEnumerator();
                }

				if (pipe != null && !pipe.MoveNext())
				{
					//if we ran out, we need to restart the stream
					_refresh = true;

					if (this.IsMarketHoursForSymbols(_subscriptions.Keys))
					{
						//to avoid spamming the server if it is rejecting us, then
						Thread.Sleep(10000);
					}
					else
					{
						Log.Trace("Market is closed, will reconnect in 20 mins.");
						Thread.Sleep(20 * 60 * 1000);
                        //Thread.Sleep(10000);
					}
				}

                if (pipe != null && pipe.Current != null)
                {
                    var tsd = pipe.Current;
                    //send quotes and trade ticks
                    var tick = CreateTick(tsd);
                    if (tick != null)
                    {
                        yield return tick;
                    }
                    //send quotes and trade ticks
                    tick = CreateDerivativeTick(tsd, SecurityType.Option);
                    if (tick != null)
                    {
                        yield return tick;
                    }
                }

            } while (_isConnected);
        }

		public IEnumerable<Symbol> LookupSymbols(string lookupName, SecurityType securityType, string securityCurrency = null, string securityExchange = null)
		{
			var baseSymbol = Symbol.Create(lookupName, securityType, "USA");
			return LookupSymbols(baseSymbol);
		}

		public IEnumerable<Symbol> LookupSymbols(Symbol lookupSymbol)
        {
            string contractName = lookupSymbol.ID.Symbol;
            Symbol baseEquitySymbol = Symbol.Create(contractName, SecurityType.Equity, Market.USA);

			string criteria = "";
			switch(lookupSymbol.SecurityType)
			{
				case SecurityType.Equity:
                    criteria = "N=" + contractName;
					criteria += "&C=Stock";
					break;
				case SecurityType.Option:
                    criteria = "R=" + contractName;
					criteria += "&C=StockOption";
                    criteria += "&Exd=10";
					criteria += "&Stk=30"; //grab many strikes
					break;
				case SecurityType.Future:
                    criteria = "N=" + contractName;
					criteria += "&C=Future";
					break;
			}

			var symbolsList = new List<Symbol>();
			var results = _tradeStationClient.SearchSymbolsAsync(_accessToken, criteria).Result;
			foreach(var result in results)
			{
				Symbol symbol;
				if (!string.IsNullOrEmpty(result.OptionType))
				{
					symbol = Symbol.CreateOption(
                        baseEquitySymbol,
						"USA",
						OptionStyle.American,
						result.OptionType == "Put" ? OptionRight.Put : OptionRight.Call,
						(decimal)result.StrikePrice,
						(DateTime)result.ExpirationDate);

                    _optionNameResolver[symbol] = result.Name;
				}
				else
				{
                    symbol = baseEquitySymbol;
				}
				symbolsList.Add(symbol);
			}

            Log.Trace("[TradeStationBrokerage.LookupSymbols] Found " + symbolsList.Count.ToString() + " symbols for " + contractName);

			return symbolsList;
		}

        /// <summary>
        /// Subscribe to a specific list of symbols
        /// </summary>
        /// <param name="job">Live job to subscribe with</param>
        /// <param name="symbols">List of symbols to subscribe to</param>
        public void Subscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            //Add the symbols to the list if they aren't there already.
            foreach (var symbol in symbols.Where(x => !x.Value.Contains("-UNIVERSE-")))
            {
                if (symbol.ID.SecurityType == SecurityType.Equity ||
                    symbol.ID.SecurityType == SecurityType.Option)
                {
                    _subscriptions.TryAdd(symbol, symbol.Value);
                }
            }

            Refresh();
        }

        /// <summary>
        /// Remove the symbol from the subscription list.
        /// </summary>
        /// <param name="job">Live Job to subscribe with</param>
        /// <param name="symbols">List of symbols to unsubscribe from</param>
        public void Unsubscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            //Remove the symbols from the subscription list if there.
            foreach (var symbol in symbols)
            {
                string value;
                if (_subscriptions.TryRemove(symbol, out value))
                {
                    //!!! _optionNameResolver.Remove(symbol); //DON'T REMOVE FROM CACHE!! for now keep everything in the cache
                }
            }

            Log.Trace("TradeStationBrokerage.DataQueueHandler: removed symbols.");

            Refresh();
        }

        /// <summary>
        /// Refresh the subscriptions list.
        /// </summary>
        private void Refresh()
        {
            if (_refreshDelay.Enabled) _refreshDelay.Stop();
            _refreshDelay = new Timer(5000);
            _refreshDelay.Elapsed += (sender, args) =>
            {
                _refresh = true;
                Log.Trace("TradeStationBrokerage.DataQueueHandler.Refresh(): Updating tickers..." + string.Join(",", _subscriptions.Select(x => x.Value)));
                CloseStream();
                _refreshDelay.Stop();
            };
            _refreshDelay.Start();
        }

        /// <summary>
        /// Create a tick from the tradier stream data:
        /// </summary>
        /// <param name="tsd">Tradier stream data obejct</param>
        /// <returns>LEAN Tick object</returns>
        private Tick CreateTick(QuoteStreamDefinition tsd)
        {
            return CreateDerivativeTick(tsd, SecurityType.Equity);
        }

        private Tick CreateDerivativeTick(QuoteStreamDefinition tsd, SecurityType derivativeType = SecurityType.Equity)
        {
            Symbol symbol = null;

			//TODO: fix this hackiness
            if (derivativeType == SecurityType.Option)
            {
                symbol = _optionNameResolver.FirstOrDefault(x => x.Value == tsd.Symbol).Key;
				if (symbol == null)
				{
					symbol = _subscriptions.FirstOrDefault(x => x.Key.Value.StartsWith("?") && x.Key.ID.Symbol == tsd.Symbol && x.Key.SecurityType == SecurityType.Option).Key;
				}
            }
            else
            {
                symbol = _subscriptions.FirstOrDefault(x => x.Key.ID.Symbol == tsd.Symbol && x.Key.SecurityType == derivativeType).Key;
            }

			// Not subscribed to this symbol.
			if (symbol == null)
			{
				//Log.Trace("TradeStation.DataQueueHandler.Stream(): Not subscribed to symbol " + tsd.Symbol);
				return null;
			}
            //this is bad/useless data
            //if (tsd.TradeTime == DateTime.MinValue) return null;
            //TODO: hack fix
            tsd.TradeTime = GetRealTimeTickTime(symbol);

            var tick = new Tick
            {
                Exchange = tsd.Exchange,
                TickType = symbol.ID.SecurityType == SecurityType.Option ? TickType.Quote : TickType.Trade,
                Quantity = (int)tsd.Volume,
                Time = tsd.TradeTime,
                EndTime = tsd.TradeTime,
                Symbol = symbol,
                //DataType = MarketDataType.Tick,
                Suspicious = false,
                Value = (decimal)tsd.Last,
				AskPrice = (decimal)tsd.Ask,
				AskSize = (decimal)tsd.AskSize,
				BidPrice = (decimal)tsd.Bid,
				BidSize = (decimal)tsd.BidSize
            };

            /*
            if (tick.TickType == TickType.Quote)
            {
                Console.WriteLine("got option quote: {0}\t= {1}, {2}", tick.Symbol.ToString(), tick.Value, tick.Time);
            }
            */

            return tick;
        }

        private DateTime GetRealTimeTickTime(Symbol symbol)
        {
            DateTimeZone exchangeTimeZone = MarketHoursDatabase.FromDataFolder().GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType).TimeZone;
            return DateTime.UtcNow.ConvertFromUtc(exchangeTimeZone);
            /*
            var time = DateTime.UtcNow.Add(_brokerTimeDiff);

            DateTimeZone exchangeTimeZone;
            if (!_symbolExchangeTimeZones.TryGetValue(symbol, out exchangeTimeZone))
            {
                // read the exchange time zone from market-hours-database
                exchangeTimeZone = MarketHoursDatabase.FromDataFolder().GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType).TimeZone;
                _symbolExchangeTimeZones.Add(symbol, exchangeTimeZone);
            }

            return time.ConvertFromUtc(exchangeTimeZone);
            */
        }

        /*
        /// <summary>
        /// Get the current market status
        /// </summary>
        private TradierStreamSession CreateStreamSession()
        {
            var request = new RestRequest("markets/events/session", Method.POST);
            return Execute<TradierStreamSession>(request, TradierApiRequestType.Data, "stream");
        }
		*/

        /// <summary>
        /// Close the current stream async
        /// </summary>
        private void CloseStream()
        {
            if (_tradestationStream !=  null)
            {
                _tradestationStream.Close();

                Log.Trace("TradestationBrokerage.DataQueueHandler.CloseStream(): Closing stream socket...");
                _refresh = true;
            }
        }

        /// <summary>
        /// Connect to tradier API strea:
        /// </summary>
        /// <param name="symbols">symbol list</param>
        /// <returns></returns>

        private IEnumerable<QuoteStreamDefinition> Stream()
        {
            if (_tradestationStream != null)
            {
                //only 1 is allowed at a time
                _tradestationStream.Close();
            }

			//Tradestation will send a full quote first, then only changes. We need to keep track of the full one,
			// and merge in the changed one.
			var activeQuotes = new Dictionary<string, QuoteStreamDefinition>();
			//Gather together all the symbols we want to subscribe to
            var symbols = new HashSet<string>();
			foreach(var sub in _subscriptions)
            {
                if (sub.Key.Value.StartsWith("?"))
                {
                    //resolve derivative
                    symbols.Add(sub.Key.ID.Symbol);
                }
                else if (sub.Key.SecurityType == SecurityType.Option)
                {
                    if (_optionNameResolver.ContainsKey(sub.Key))
                        symbols.Add(_optionNameResolver[sub.Key]);
                    else
                        Log.Error("No option symbol was resolved for " + sub.Key.Value);
                }
                else
                {
                    symbols.Add(sub.Value);
                }
			}
			var symbolJoined = String.Join(",", symbols);

            Log.Trace("TradeStation.Stream(): Creating new session, Reading Stream... (" + symbols.Count + " tickers)", true);
			HttpWebRequest request;
            request = (HttpWebRequest)WebRequest.Create(String.Format("{0}/stream/quote/changes/{1}?access_token={2}", _tradeStationClient.BaseUrl, symbolJoined, _accessToken));
            request.Timeout = 30 * 1000;
            request.Accept = "application/vnd.tradestation.streams+json";

            //Get response as a stream:
            try
            {
                var response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Log.Trace("TradeStation.DataQueueHandler.Stream(): Bad request status " + response.StatusCode + "! Disconnecting from stream...");
                    _isConnected = false;
                    yield break;
                }
                _tradestationStream = response.GetResponseStream();
                if (_tradestationStream == null)
                {
                    Log.Error("TradeStation.DataQueueHandler.Stream(): Null stream error!");
                    yield break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("TradeStation.DataQueueHandler.Stream(): Error establishing connection: " + ex.Message);
                yield break;
            }

            using(_tradestationStream)
            using (var sr = new StreamReader(_tradestationStream))
            using (var jsonReader = new JsonTextReader(sr))
            {
                jsonReader.SupportMultipleContent = true;

                // keep going until stream gets closed
                while (!_refresh)
                {
					JToken token = null;

					try
                    {
                        if (!_tradestationStream.CanRead)
							yield break; //stream closed down, exit normally

                        //Read the jsonSocket in a safe manner: might close and so need handlers, but can't put handlers around a yield.
                        jsonReader.Read();
                        if (jsonReader.TokenType != JsonToken.StartObject)
                        {
                            Log.Debug("TradeStation.DataQueueHandler.Stream(): token parse error");
                            continue; //bad json or we're parsing in the wrong place somehow, just move along...
                        }
						
						token = JToken.Load(jsonReader);
					}
                    catch (Exception err)
                    {
                        Log.Trace("TradeStation.DataQueueHandler.Stream(): Stream read error: " + err.Message);
					}

                    if (token == null)
                    {
						// if we couldn't get a successful read, abort this session
						yield break;
                    }

					//after the first read, we set a high timeout on the socket, the server can keep it open as long as it wants.
					//... but if nothing comes in for a few minutes, might as well restart the stream.
                    _tradestationStream.ReadTimeout = 3 * 60 * 1000; //3mins

					//now deserialize it for processing
					/*
                    Anonymous3 z = null;
                    try
                    {
						z = token.ToObject<Anonymous3>();
                        if (z!=null)
                        {
                            Log.Trace("got snapshot from stream");
                        }
                    }
                    catch (Exception err)
                    {
                        // Do nothing for now. Can come back later to fix. Errors are from Tradier not properly json encoding values E.g. "NaN" string.
                        Log.Trace("TradeStation.DataQueueHandler.Stream(): z json deserialization error: " + err.Message);
                    }
					*/

					QuoteStreamDefinition tsd = null;
					try
                    {
                        tsd = token.ToObject<QuoteStreamDefinition>();
                        if (tsd != null)
                        {
							Log.Debug("TradeStation.DataQueueHandler.Stream(): got quote: " + token.ToString());
                        }
                    }
                    catch (Exception err)
                    {
                        // Do nothing for now. Can come back later to fix. Errors are from Tradier not properly json encoding values E.g. "NaN" string.
                        Log.Error("TradeStation.DataQueueHandler.Stream(): json deserialization error: " + err.Message);
                    }

                    // don't yield garbage, just wait for the next one
                    if (tsd != null)
                    {
						if (!activeQuotes.ContainsKey(tsd.Symbol))
						{
							activeQuotes[tsd.Symbol] = tsd;
						}
						else
						{
							mergeQuote(activeQuotes[tsd.Symbol], tsd);
						}
                        yield return tsd;
                    }

					//no need to rail the cpu doing this
					Thread.Sleep(10);
				}
            }
        }

		private void mergeQuote(QuoteStreamDefinition source, QuoteStreamDefinition target)
		{
			//we only care about the things needed to create a valid 'Tick' object
			target.Exchange = target.Exchange != null	? target.Exchange : source.Exchange;
			target.Volume = target.Volume != null		? target.Volume : 0;
			target.TradeTime = target.TradeTime > DateTime.MinValue ? target.TradeTime : source.TradeTime;
			target.Last = target.Last != null			? target.Last : source.Last;
			target.Ask = target.Ask != null				? target.Ask : source.Ask;
			target.AskSize = target.AskSize != null		? target.AskSize : source.AskSize;
			target.Bid = target.Bid != null				? target.Bid : source.Bid;
			target.BidSize = target.BidSize != null		? target.BidSize : source.BidSize;
		}

		#endregion
	}
}
