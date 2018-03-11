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
		/// <summary>
		/// QuantConnect Google Downloader For LEAN Algorithmic Trading Engine.
		/// Original by @chrisdk2015, tidied by @jaredbroad
		/// </summary>
		public static void Main(string[] args)
		{
			if (args.Length != 1)
			{
				Console.WriteLine("Usage: IBConverter IMPORT_DIR");
				Environment.Exit(1);
			}

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
					if (!file.Contains("historical_data"))
						continue;

					Console.WriteLine("Processing {0}...", file);
					
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
						while(!String.IsNullOrEmpty(line))
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

						Console.WriteLine("Found {0} bars", bars.Count);

						var writer = new LeanDataWriter(Resolution.Minute, symbolObject, dataDirectory);
						writer.Write(bars);

					}

					// Create an instance of the downloader
					/*
					const string market = Market.USA;
					// Download the data
					var symbolObject = Symbol.Create(symbol, SecurityType.Equity, market);
					var data = downloader.Get(symbolObject, resolution, startDate, endDate);

					// Save the data
					var writer = new LeanDataWriter(resolution, symbolObject, dataDirectory);
					writer.Write(data);
					*/
				}
			}
			catch (Exception err)
			{
				Log.Error(err);
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
