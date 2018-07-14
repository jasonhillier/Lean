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
using NodaTime;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using QuantConnect.Packets;
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
		private Dictionary<Symbol, List<Symbol>> _optionList = new Dictionary<Symbol, List<Symbol>>();
		private Dictionary<string, Symbol> _optionNameResolver = new Dictionary<string, Symbol>();
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
                    Thread.Sleep(10);
                    continue;
                }

                //If there's been an update to the subscriptions list; recreate the stream.
                if (_refresh)
                {
                    var stream = Stream();
                    pipe = stream.GetEnumerator();
                    pipe.MoveNext();
                    _refresh = false;
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

                    if (!pipe.MoveNext())
					{
						//if we ran out, we need to restart the stream
						_refresh = true;
						//to avoid spamming the server if it is rejecting us, then
						Thread.Sleep(10000);
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
			string safeName = lookupSymbol.Value.Replace("?", "");

			string criteria = "";
			switch(lookupSymbol.SecurityType)
			{
				case SecurityType.Equity:
					criteria = "N=" + safeName;
					criteria += "&C=Stock";
					break;
				case SecurityType.Option:
					criteria = "R=" + safeName;
					criteria += "&C=StockOption";
					criteria += "&Stk=20"; //grab many strikes
					break;
				case SecurityType.Future:
					criteria = "N=" + safeName;
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
                        lookupSymbol.Underlying,
						"USA",
						OptionStyle.American,
						result.OptionType == "Put" ? OptionRight.Put : OptionRight.Call,
						(decimal)result.StrikePrice,
						(DateTime)result.ExpirationDate);

					_optionNameResolver[result.Name] = symbol;
				}
				else
				{
					symbol = Symbol.Create(result.Name, SecurityType.Equity, "USA");
				}
				symbolsList.Add(symbol);
			}

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
				if (symbol.ID.SecurityType == SecurityType.Equity)
				{
					if (_subscriptions.TryAdd(symbol, symbol.Value))
					{
						Refresh();
					}
				}
                else if (symbol.ID.SecurityType == SecurityType.Option && symbol.IsCanonical())
                {
                    if (_subscriptions.TryAdd(symbol.Underlying, symbol.Underlying.Value))
                    {
                        Refresh();
                    }
                }
            }
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
                    Refresh();
                }
            }
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
                //CloseStream();
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
			Symbol symbol;
			if (!_optionNameResolver.TryGetValue(tsd.Symbol, out symbol))
			{
				symbol = _subscriptions.FirstOrDefault(x => x.Value == tsd.Symbol).Key;
			}

			// Not subscribed to this symbol.
			if (symbol == null)
			{
				Log.Trace("TradeStation.DataQueueHandler.Stream(): Not subscribed to symbol " + tsd.Symbol);
				return null;
			}
			//this is bad/useless data
			if (tsd.TradeTime == DateTime.MinValue) return null;

            return new Tick
            {
                Exchange = tsd.Exchange,
                TickType = tsd.Volume == 0 ? TickType.Quote : TickType.Trade,
                Quantity = (int)tsd.Volume,
                Time = tsd.TradeTime,
                EndTime = tsd.TradeTime,
                Symbol = symbol,
                DataType = MarketDataType.Tick,
                Suspicious = false,
                Value = (decimal)tsd.Last,
				AskPrice = (decimal)tsd.Ask,
				AskSize = (decimal)tsd.AskSize,
				BidPrice = (decimal)tsd.Bid,
				BidSize = (decimal)tsd.BidSize
            };
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

        /// <summary>
        /// Close the current stream async
        /// </summary>
        private void CloseStream()
        {
            if (_tradierStream != null)
            {
                Log.Trace("TradestationBrokerage.DataQueueHandler.CloseStream(): Closing stream socket...");
                _tradierStream.Close();
            }
        }
        */

        /// <summary>
        /// Connect to tradier API strea:
        /// </summary>
        /// <param name="symbols">symbol list</param>
        /// <returns></returns>

        private IEnumerable<QuoteStreamDefinition> Stream()
        {
			var symbols = new List<string>();
			foreach(var sub in _subscriptions)
			{
				if (sub.Key.SecurityType == SecurityType.Equity)
				{
					symbols.Add(sub.Key.Value);
				}
				if (sub.Key.SecurityType == SecurityType.Option)
				{
					foreach(var option in _optionList[sub.Key])
					{
						var name = _optionNameResolver.SingleOrDefault(x => x.Value == option).Key;
						symbols.Add(name);
					}
				}
			}
			var symbolJoined = String.Join(",", symbols);

			HttpWebRequest request;
            request = (HttpWebRequest)WebRequest.Create(String.Format("{0}/stream/quote/changes/{1}?access_token={2}", _tradeStationClient.BaseUrl, symbolJoined, _accessToken));
            request.Accept = "application/vnd.tradestation.streams+json";

            //Get response as a stream:
            Log.Trace("TradeStation.Stream(): Session Created, Reading Stream...", true);
            var response = (HttpWebResponse)request.GetResponse();
			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				Log.Trace("TradeStation.DataQueueHandler.Stream(): Unauthroized request! Disconnecting from stream...");
				_isConnected = false;
				yield break;
			}
            _tradestationStream = response.GetResponseStream();
            if (_tradestationStream == null)
            {
                yield break;
            }

            using (var sr = new StreamReader(_tradestationStream))
            using (var jsonReader = new JsonTextReader(sr))
            {
                var serializer = new JsonSerializer();
                jsonReader.SupportMultipleContent = true;

                // keep going until stream gets closed
                while (!sr.EndOfStream)
                {
					bool successfulRead = false;
                    try
                    {
                        //Read the jsonSocket in a safe manner: might close and so need handlers, but can't put handlers around a yield.
                        successfulRead = jsonReader.Read();
                    }
                    catch (Exception err)
                    {
                        Log.Trace("TradeStation.DataQueueHandler.Stream(): Handled breakout / socket close from jsonRead operation: " + err.Message);
					}

                    if (!successfulRead)
                    {
                        // if we couldn't get a successful read just keep trying
                        continue;
                    }

                    //Have a Tradier JSON Object:
                    QuoteStreamDefinition tsd = null;
                    Anonymous3 z = null;
                    try
                    {
                        z = serializer.Deserialize<Anonymous3>(jsonReader);
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

                    try
                    {
                        tsd = serializer.Deserialize<QuoteStreamDefinition>(jsonReader);
                        if (tsd != null)
                        {
                            Log.Trace("got quote change from stream");
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
                        yield return tsd;
                    }

					//no need to rail the cpu doing this
					Thread.Sleep(10);
				}
            }
        }

        #endregion
    }
}
