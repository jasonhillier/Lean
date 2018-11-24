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
using Elasticsearch.Net;

namespace QuantConnect.ToolBox.ElasticSearchDownloader
{
    //Copies quotes from options datafarm to equity datafarm
    class ProgramCopyToEquity
    {
        private static ElasticClient _client;
        private static ConcurrentQueue<StockOptionQuote> _StockBuffer = new ConcurrentQueue<StockOptionQuote>();
        private static bool _downloadComplete;
        const int MAX_UPLOAD_SIZE = 1000;

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

            Task.Run(() => beginUploadEquity(args[0]));

            while (Console.ReadKey().KeyChar != 'q')
                System.Threading.Thread.Sleep(1);
        }

        private async static Task beginUploadEquity(string symbol)
        {
            var uploadBlock = new List<EquityOnlyQuote>();

            while (!_downloadComplete || _StockBuffer.Count > 0)
            {
                await Task.Delay(100);
                _StockBuffer.All((r) =>
                {
                    StockOptionQuote quote;
                    _StockBuffer.TryDequeue(out quote);
                    uploadBlock.Add(new EquityOnlyQuote(quote));
                    return uploadBlock.Count < MAX_UPLOAD_SIZE;
                });

                if (uploadBlock.Count>0)
                {
                    var result = _client.IndexMany(uploadBlock, "equity-" + symbol.ToLower());
                    if (!result.Errors)
                        Console.WriteLine("Uploaded {0} quotes to equity-" + symbol.ToLower(), uploadBlock.Count);
                    else
                        throw new Exception("Error uploading!!");
                }
                uploadBlock.Clear();
            }
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
                        .Field(f => f.symbol)
                        .Query(Symbol)
                    )/* && q
                    .DateRange(r => r
                        .Field(f => f.date)
                        .GreaterThanOrEquals(Start)
                        .LessThanOrEquals(End)
                    )*/
                )
                .Scroll(100000)
                .Sort(ss => ss.Descending(f => f.date))
            );

            Log.Trace("Found {0} quotes", search.HitsMetadata.Total);

            //first page
            string scrollId = search.ScrollId;
            foreach (T doc in search.Documents)
            {
                _StockBuffer.Enqueue(doc);
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
                }

                totalReceived += search.Documents.Count;

                Log.Trace("Downloaded {0} so far", totalReceived);
            } while (results.Documents.Count == 10000);

            Log.Trace("Completed download - retrieved {0} document objects.", totalReceived);
            _downloadComplete = true;
        }
    }

    class EquityOnlyQuote
    {
        private StockOptionQuote _quote;

        public EquityOnlyQuote(StockOptionQuote pQuote)
        {
            this._quote = pQuote;
        }

        public string id
        {
            get { return this.symbol + ' ' + this.date.ToString("o"); }
        }

        public string symbol
        {
            get { return _quote.baseSymbol; }
        }
        public decimal price
        {
            get { return _quote.basePrice; }
        }
        public int volume
        {
            get { return _quote.baseVolume; }
        }
        public DateTime date
        {
            get { return _quote.date; }
        }
    }
}
