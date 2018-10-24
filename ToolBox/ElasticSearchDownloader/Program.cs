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
using System.Globalization;
using QuantConnect.Configuration;
using QuantConnect.Logging;
using System.IO;
using QuantConnect.Data.Market;
using QuantConnect.Data;
using System.Collections.Generic;
using Nest;
using System.Threading.Tasks;
using System.Linq;
using QuantConnect.ToolBox.IBConverter;
using System.Collections.Concurrent;

namespace QuantConnect.ToolBox.ElasticSearchDownloader
{
	class Program
	{
		private static ElasticClient _client;
        private static ConcurrentQueue<StockOptionQuote> _StockBuffer = new ConcurrentQueue<StockOptionQuote>();
        private static ConcurrentQueue<StockOptionQuote> _OptionBuffer = new ConcurrentQueue<StockOptionQuote>();
        private static bool _downloadComplete;
		/// <summary>
		/// QuantConnect Google Downloader For LEAN Algorithmic Trading Engine.
		/// Original by @chrisdk2015, tidied by @jaredbroad
		/// </summary>
		public static void Main(string[] args)
		{
			if (args.Length < 4)
			{
				Console.WriteLine("Usage: ElasticSearchDownloader [symbol] [url] [index] [user] [pass]");
				Environment.Exit(1);
			}

            //setup logging
            Log.LogHandler = new CompositeLogHandler(new ILogHandler[] { new ConsoleLogHandler() });

			Log.Trace("Downloading {0} quotes from {1}:{2}...", args);

			_client = new Nest.ElasticClient(new ConnectionSettings(
				new Uri(args[1]))
				.DefaultIndex(args[2])
				.BasicAuthentication(args[3], args[4])
				.EnableHttpCompression()
				);

            //import the whole index
            Task.Run(() => beginFetchQuotes<StockOptionQuote>(args[0], DateTime.Now, DateTime.Now));

            Task.Run(() => beginImportEquity(args[0]));
            //only wait for the last one
            Task.Run(() => importOptions(args[0]));

            Console.ReadLine();
		}

        private async static Task beginImportEquity(string symbol)
        {
            //TimeSpan timeSpan = new TimeSpan(0, 15, 0);
            var symbolObject = Symbol.Create(symbol, SecurityType.Equity, Market.USA);

			Log.Trace("Processing equity {0}...", symbol);

			// Load settings from config.json
			var dataDirectory = Config.Get("data-directory", "../../../Data");

            var writer = new LeanDataWriter(Resolution.Minute, symbolObject, dataDirectory);

			DateTime frontierDate = DateTime.MinValue;
            int priorDay = 0;
            List<TradeBar> pendingBars = new List<TradeBar>();

            StockOptionQuote quote;

            while(!_downloadComplete || _StockBuffer.Count > 0)
            {
                await Task.Delay(0);
                if (!_StockBuffer.TryDequeue(out quote))
                    continue;

                if (quote.date > frontierDate) //ensure data is in sane order
				{
                    //write chunk per day
                    if (priorDay > 0 && priorDay != quote.date.Day)
                    {
                        writer.Write(pendingBars);
                        pendingBars.Clear();
                    }
                    priorDay = quote.date.Day;

					frontierDate = quote.date;

                    pendingBars.Add(new TradeBar(
						quote.date,
						symbolObject,
						quote.basePrice,
						quote.basePrice,
						quote.basePrice,
						quote.basePrice,
						quote.baseVolume
                    ));
				}
            }

            //final Flush
            if (pendingBars.Count > 0)
            {
                writer.Write(pendingBars);
                pendingBars.Clear();
            }

            Log.Trace("Finished equity import");

        }

        private static async Task importOptions(string symbol)
        {
            var underlyingSymbol = Symbol.Create(symbol, SecurityType.Equity, Market.USA);

            Log.Trace("Processing options {0}...", symbol);

			// Load settings from config.json
			var dataDirectory = Config.Get("data-directory", "../../../Data");

            Dictionary<string, OptionDataWriter> dayWriters = new Dictionary<string, OptionDataWriter>();
            StockOptionQuote quote;
            long ticksProcessed = 0;
            int priorDay = 0;


            while (!_downloadComplete || _OptionBuffer.Count > 0)
			{
                await Task.Delay(0);

                if (!_OptionBuffer.TryDequeue(out quote))
                    continue;

                var optionSymbol = Symbol.CreateOption(
                    symbol,
                    Market.USA,
                    OptionStyle.American,
                    (quote.right == "P" ? OptionRight.Put : OptionRight.Call),
                    (decimal)quote.strike,
                    quote.expiry
                );

                string optionFileName = optionSymbol.Value + "_" + quote.date.ToShortDateString();
                OptionDataWriter writer;
                if (!dayWriters.TryGetValue(optionFileName, out writer))
				{
					writer = new OptionDataWriter(optionSymbol, quote.date, TickType.Quote, Resolution.Minute, dataDirectory);
                    dayWriters.Add(optionFileName, writer);
				}

				var bar = new QuoteBar(
					quote.date,
					optionSymbol,
					new Bar(quote.bid, quote.bid, quote.bid, quote.bid),
					quote.bidSize,
					new Bar(quote.ask, quote.ask, quote.ask, quote.ask),
					quote.askSize
				);

				writer.Enqueue(bar);

                if (priorDay > 0 && priorDay != quote.date.Day)
                {
                    //save all the files for today
                    foreach(KeyValuePair<string, OptionDataWriter> w in dayWriters)
                    {
                        ticksProcessed += w.Value.SaveToDisk();
                    }

                    dayWriters.Clear();

                    Log.Trace("Stored {0} option ticks...", ticksProcessed);
                }
                priorDay = quote.date.Day;
            }

            //save any pending files
            foreach (KeyValuePair<string, OptionDataWriter> w in dayWriters)
            {
                ticksProcessed += w.Value.SaveToDisk();
            }


			Log.Trace("Stored total of option {0} ticks, zipping...", ticksProcessed);
			//zip all the files
			OptionDataWriter.Package(dataDirectory);
            Log.Trace("Option data stored successfully.");
        }


        public static async Task beginFetchQuotes<T>(string Symbol, DateTime Start, DateTime End) where T : StockOptionQuote, new()
		{
            _downloadComplete = false;

			var search = await _client.SearchAsync<T>(s => s
				.AllTypes()
				.From(0)
				.Size(10000)
				.Query(q => q
					.Match(m => m
						.Field(f=>f.symbol)
						.Query(Symbol)
					)/* && q
					.DateRange(r => r
						.Field(f => f.date)
						.GreaterThanOrEquals(Start)
						.LessThanOrEquals(End)
					)*/
				)
				.Scroll(100000)
				.Sort(ss=>ss.Ascending(f=>f.date))
			);

			Log.Trace("Found {0} quotes", search.HitsMetadata.Total);

			//first page
			string scrollId = search.ScrollId;
            foreach(T doc in search.Documents)
            {
                _StockBuffer.Enqueue(doc);
                _OptionBuffer.Enqueue(doc);
            }

            int totalReceived = search.Documents.Count;

			ISearchResponse<T> results;
			do
			{
				//page until we get all the results
				results = await _client.ScrollAsync<T>(100000, scrollId);
				scrollId = results.ScrollId;

                foreach (T doc in results.Documents)
                {
                    _StockBuffer.Enqueue(doc);
                    _OptionBuffer.Enqueue(doc);
                }

                totalReceived += search.Documents.Count;

                Log.Trace("Downloaded {0} so far", totalReceived);
			} while (results.Documents.Count == 10000);

            Log.Trace("Completed download - retrieved {0} document objects.", totalReceived);
            _downloadComplete = true;
		}
	}
}
