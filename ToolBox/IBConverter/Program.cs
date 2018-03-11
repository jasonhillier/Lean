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

namespace QuantConnect.ToolBox.IBConverter
{
	class Program
	{
        private static bool _importOptions = false;
        private static bool _importEquity = false;
		/// <summary>
		/// QuantConnect Google Downloader For LEAN Algorithmic Trading Engine.
		/// Original by @chrisdk2015, tidied by @jaredbroad
		/// </summary>
		public static void Main(string[] args)
		{
			if (args.Length < 1)
			{
				Console.WriteLine("Usage: IBConverter IMPORT_DIR -o -e");
				Environment.Exit(1);
			}

            foreach(var arg in args)
            {
                if (arg == "-o")
                    _importOptions = true;
                if (arg == "-e")
                    _importEquity = true;
            }

            //setup logging
            Log.LogHandler = new CompositeLogHandler(new ILogHandler[] { new ConsoleLogHandler() });

			try
			{
				string directory = args[0];
				// Load settings from command line
				//var symbols = args[0].Split(',');
				//var resolution = (Resolution)Enum.Parse(typeof(Resolution), args[1]);
				//var startDate = DateTime.ParseExact(args[2], "yyyyMMdd", CultureInfo.InvariantCulture);
				//var endDate = DateTime.ParseExact(args[3], "yyyyMMdd", CultureInfo.InvariantCulture);

				//var date = GetDate(directory);
				foreach (var file in System.IO.Directory.EnumerateFiles(directory))
				{
                    if (file.Contains("historical_data"))
                        importEquity(file);
                    else if (file.Contains("historical_OPTIONS"))
                        importOptions(file);
				}
			}
			catch (Exception err)
			{
				Log.Error(err);
			}
		}

        private static void importEquity(string file)
        {
            if (!_importEquity)
                return;
            Log.Trace("Processing equity {0}...", file);

            var symbol = GetSymbol(file);
            TimeSpan timeSpan = new TimeSpan(0, 15, 0);
            var symbolObject = Symbol.Create(symbol, SecurityType.Equity, Market.USA);
            var fileContents = File.ReadAllText(file);

            // Load settings from config.json
            var dataDirectory = Config.Get("data-directory", "../../../Data");

            using (StringReader reader = new StringReader(fileContents))
            {
                if (!reader.ReadLine().StartsWith("open,high,low,close,volume,bar_size,date_time"))
                {
                    throw new Exception("Invalid file format: " + file);
                }

                var bars = new List<BaseData>();

                string line = reader.ReadLine();
                while (!String.IsNullOrEmpty(line))
                {
                    var splits = line.Split(',');

                    var date = DateTime.Parse(splits[6]);

                    var bar = new TradeBar(
                        date,
                        symbolObject,
                        decimal.Parse(splits[0]),
                        decimal.Parse(splits[1]),
                        decimal.Parse(splits[2]),
                        decimal.Parse(splits[3]),
                        decimal.Parse(splits[4]),
                        timeSpan);

                    bars.Add(bar);

                    line = reader.ReadLine();
                }

                Log.Trace("Found {0} bars", bars.Count);

                var writer = new LeanDataWriter(Resolution.Minute, symbolObject, dataDirectory);
                writer.Write(bars);
            }
        }

        private static void importOptions(string file)
        {
            if (!_importOptions)
                return;
            
            Log.Trace("Processing options {0}...", file);

            var underlying = GetSymbol(file);
            TimeSpan timeSpan = new TimeSpan(0, 15, 0);
            var underlyingSymbol = Symbol.Create(underlying, SecurityType.Equity, Market.USA);
            var fileContents = File.ReadAllText(file);

            // Load settings from config.json
            var dataDirectory = Config.Get("data-directory", "../../../Data");

            using (StringReader reader = new StringReader(fileContents))
            {
                if (!reader.ReadLine().StartsWith("UNDERLYING_SYMBOL,OPT_CONTRACT_ID,EXCHANGE,MULTIPLIER,EXPIRATION_DATE,RIGHT,STRIKE,BAR_TIME,BAR_SIZE,OPEN_PRICE,HIGH_PRICE,LOW_PRICE,CLOSE_PRICE"))
                {
                    throw new Exception("Invalid file format: " + file);
                }

                var bars = new List<BaseData>();

                string line = reader.ReadLine();
                while (!String.IsNullOrEmpty(line))
                {
                    var splits = line.Split(',');

                    var optionSymbol = Symbol.CreateOption(
                        underlying,
                        Market.USA,
                        OptionStyle.American,
                        (splits[5] == "PUT" ? OptionRight.Put : OptionRight.Call),
                        decimal.Parse(splits[6]),
                        DateTime.Parse(splits[4])
                    );
                    //var optionSymbol = new OptionContract(splits[0], underlyingSymbol);

                    var date = DateTime.Parse(splits[7]);

                    var bar = new TradeBar(
                        date,
                        optionSymbol,
                        decimal.Parse(splits[9]),
                        decimal.Parse(splits[10]),
                        decimal.Parse(splits[11]),
                        decimal.Parse(splits[12]),
                        0,
                        timeSpan);

                    bars.Add(bar);

                    line = reader.ReadLine();
                }

                Log.Trace("Found {0} bars", bars.Count);

                //var writer = new OptionDataWriter(o

                //var writer = new LeanDataWriter(Resolution.Minute, symbolObject, dataDirectory);
                //writer.Write(bars);
            }
        }

		/// <summary>
		/// Extract the symbol from the path
		/// </summary>
		private static string GetSymbol(string filePath)
		{
			//historical_data_VXX_USD__TRADES_15mins_20180310-161940

			var splits = filePath.Split('/', '\\');
			var file = splits[splits.Length - 1];
			file = file.Trim('.', '/', '\\');
			file = file.Replace("historical_data_", "");
			var endIndex = file.IndexOf("_USD");
			return file.Substring(0, endIndex);
		}

		/// <summary>
		/// Extract the symbol from the path
		/// </summary>
		private static string GetDate(string filePath)
		{
			var splits = filePath.Split('/', '\\');
			var file = splits[splits.Length - 1];

			file = file.Split('-')[0];
			splits = file.Split('_');
			file = splits[splits.Length - 1];

			return file;
		}
	}
}
