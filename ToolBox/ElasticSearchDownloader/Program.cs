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

namespace QuantConnect.ToolBox.ElasticSearchDownloader
{
	class Program
	{
		private static ElasticClient _client;
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
			var quotes = FetchQuotes<StockOptionQuote>(args[0], DateTime.Now, DateTime.Now).Result;

			importEquity(quotes);
			importOptions(quotes);

			Console.ReadLine();
		}

        private static void importEquity(IReadOnlyCollection<StockOptionQuote> quotes)
        {
			var symbol = quotes.First().baseSymbol;
            TimeSpan timeSpan = new TimeSpan(0, 15, 0);
            var symbolObject = Symbol.Create(symbol, SecurityType.Equity, Market.USA);

			Log.Trace("Processing equity {0}...", symbol);

			// Load settings from config.json
			var dataDirectory = Config.Get("data-directory", "../../../Data");

			var bars = new List<BaseData>();

            foreach(var quote in quotes)
            {
                var bar = new TradeBar(
                    quote.date,
                    symbolObject,
					quote.basePrice,
					quote.basePrice,
					quote.basePrice,
					quote.basePrice,
					0,
                    timeSpan);

                bars.Add(bar);
            }

			Log.Trace("Found {0} bars", bars.Count);

            var writer = new LeanDataWriter(Resolution.Minute, symbolObject, dataDirectory);
            writer.Write(bars);
        }

        private static void importOptions(IReadOnlyCollection<StockOptionQuote> quotes)
        {
			var underlying = quotes.First().baseSymbol;
			TimeSpan timeSpan = new TimeSpan(0, 15, 0);
			var underlyingSymbol = Symbol.Create(underlying, SecurityType.Equity, Market.USA);

			Log.Trace("Processing options {0}...", underlying);

			// Load settings from config.json
			var dataDirectory = Config.Get("data-directory", "../../../Data");

			Dictionary<string, OptionDataWriter> writers = new Dictionary<string, OptionDataWriter>();
			foreach (var quote in quotes)
			{
                var optionSymbol = Symbol.CreateOption(
                    underlying,
                    Market.USA,
                    OptionStyle.American,
                    (quote.right == "P" ? OptionRight.Put : OptionRight.Call),
                    (decimal)quote.strike,
                    quote.date
                );

				OptionDataWriter writer = null;
				if (!writers.TryGetValue(optionSymbol.Value + "_" + quote.date.ToShortDateString(), out writer))
				{
					writer = new OptionDataWriter(optionSymbol, quote.date, TickType.Quote, Resolution.Minute, dataDirectory);
					writers[optionSymbol.Value + "_" + quote.date.ToShortDateString()] = writer;
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
				/*
				var tick = new Tick(
					quote.date,
					optionSymbol,
					(decimal)quote.mid,
					(decimal)quote.bid,
					(decimal)quote.ask
					);

				writer.Process(tick);
				*/
            }

			long ticksProcessed = 0;
			foreach(var writer in writers)
			{
				ticksProcessed += writer.Value.SaveToDisk();
			}

			Log.Trace("Processed {0} ticks", ticksProcessed);
			//zip all the files
			OptionDataWriter.Package(dataDirectory);
        }


		public static async Task<IReadOnlyCollection<T>> FetchQuotes<T>(string Symbol, DateTime Start, DateTime End) where T : StockOptionQuote, new()
		{
			List<T> documents = new List<T>();

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
			documents.AddRange(search.Documents);

			ISearchResponse<T> results;
			do
			{
				//page until we get all the results
				results = await _client.ScrollAsync<T>(100000, scrollId);
				scrollId = results.ScrollId;

				documents.AddRange(results.Documents);

				Log.Trace("Downloaded {0} so far", documents.Count);
			} while (results.Documents.Count == 10000);

			Log.Trace("Retrieved {0} document objects.", documents.Count);

			return documents;
		}
	}
}
