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

        private bool _disconnect;
        private volatile bool _refresh = true;
        private Timer _refreshDelay = new Timer();
        private readonly ConcurrentDictionary<Symbol, string> _subscriptions = new ConcurrentDictionary<Symbol, string>();
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
                    var stream = Stream(GetTickers());
                    pipe = stream.GetEnumerator();
                    pipe.MoveNext();
                    _refresh = false;
                }

                if (pipe != null && pipe.Current != null)
                {
                    var tsd = pipe.Current;
                    if ("trade" == "trade")
                    {
                        var tick = CreateTick(tsd);
                        if (tick != null)
                        {
                            yield return tick;
                        }
                    }
                    pipe.MoveNext();
                }

            } while (!_disconnect);
        }

        public IEnumerable<Symbol> LookupSymbols(string lookupName, SecurityType securityType, string securityCurrency = null, string securityExchange = null)
        {
            return new List<Symbol>();
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
                if (symbol.ID.SecurityType == SecurityType.Equity || symbol.ID.SecurityType == SecurityType.Option)
                {
                    if (_subscriptions.TryAdd(symbol, symbol.Value))
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
        /// Get a string list of tickers from the symbol dictionary
        /// </summary>
        /// <returns>List of string tickers</returns>
        private List<string> GetTickers()
        {
            var values = _subscriptions.Select(x => x.Value).ToList();
            Log.Trace("TradierBrokerage.DataQueueHandler.GetTickers(): " + string.Join(",", values));
            return values;
        }

        /// <summary>
        /// Create a tick from the tradier stream data:
        /// </summary>
        /// <param name="tsd">Tradier stream data obejct</param>
        /// <returns>LEAN Tick object</returns>

        private Tick CreateTick(QuoteStreamDefinition tsd)
        {
            var symbol = _subscriptions.FirstOrDefault(x => x.Value == tsd.Symbol).Key;

            // Not subscribed to this symbol.
            if (symbol == null) return null;

            // Occassionally Tradier sends trades with 0 volume?
            /*
            if (tsd.TradeSize == 0) return null;

            // Tradier trades are US NY time only. Convert local server time to NY Time:
            var unix = Convert.ToInt64(tsd.UnixDate) / 1000;
            var utc = Time.UnixTimeStampToDateTime(unix);
            */
            var utc = DateTime.Parse(tsd.TradeTime);

            // Occassionally Tradier sends old ticks every 20sec-ish if no trading?
            //if (DateTime.UtcNow - utc > TimeSpan.FromSeconds(10)) return null;

            //Convert the to security timezone and pass into algorithm
            var time = utc.ConvertTo(DateTimeZone.Utc, TimeZones.NewYork);

            return new Tick
            {
                Exchange = tsd.Exchange,
                TickType = TickType.Trade,
                Quantity = (int)tsd.Volume,
                Time = time,
                EndTime = time,
                Symbol = symbol,
                DataType = MarketDataType.Tick,
                Suspicious = false,
                Value = (decimal)tsd.Last
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

        private IEnumerable<QuoteStreamDefinition> Stream(List<string> symbols)
        {
            //var quoteStream = _tradeStationClient.StreamQuotesChangesAsync(_accessToken, String.Join(",", symbols), TransferEncoding.Chunked).Result;

            //quoteStream.
            //bool success;
            var symbolJoined = String.Join(",", symbols);
            /*
            var session = CreateStreamSession();

            if (session == null || session.SessionId == null || session.Url == null)
            {
                Log.Error("Tradier.Stream(): Failed to Created Stream Session", true);
                yield break;
            }
            Log.Trace("Tradier.Stream(): Created Stream Session Id: " + session.SessionId + " Url:" + session.Url, true);
            */

            HttpWebRequest request;
            request = (HttpWebRequest)WebRequest.Create(String.Format("{0}/v2/stream/quote/changes/{1}?access_token={2}", _tradeStationClient.BaseUrl, symbolJoined, _accessToken));
            request.Accept = "application/vnd.tradestation.streams+json";

            //Get response as a stream:
            Log.Trace("TradeStation.Stream(): Session Created, Reading Stream...", true);
            var response = (HttpWebResponse)request.GetResponse();
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

                // keep going until we fail to read more from the stream
                while (true)
                {
                    bool successfulRead;
                    try
                    {
                        //Read the jsonSocket in a safe manner: might close and so need handlers, but can't put handlers around a yield.
                        successfulRead = jsonReader.Read();
                    }
                    catch (Exception err)
                    {
                        Log.Trace("TradeStation.DataQueueHandler.Stream(): Handled breakout / socket close from jsonRead operation: " + err.Message);
                        break;
                    }

                    if (!successfulRead)
                    {
                        // if we couldn't get a successful read just end the enumerable
                        yield break;
                    }

                    //Have a Tradier JSON Object:
                    QuoteStreamDefinition tsd = null;
                    try
                    {
                        tsd = serializer.Deserialize<QuoteStreamDefinition>(jsonReader);
                    }
                    catch (Exception err)
                    {
                        // Do nothing for now. Can come back later to fix. Errors are from Tradier not properly json encoding values E.g. "NaN" string.
                        Log.Trace("TradeStation.DataQueueHandler.Stream(): Handled breakout / socket close from jsonRead operation: " + err.Message);
                    }

                    // don't yield garbage, just wait for the next one
                    if (tsd != null)
                    {
                        yield return tsd;
                    }
                }
            }
        }

        #endregion
    }
}
