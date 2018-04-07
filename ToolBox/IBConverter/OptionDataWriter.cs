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
using QuantConnect.Data;
using System.Collections.Generic;
using System.IO;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using QuantConnect.Util;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using QuantConnect.Logging;
using System.Diagnostics;
using System.IO.Compression;

namespace QuantConnect.ToolBox.IBConverter
{
    /// <summary>
    /// Processor for caching and consolidating ticks; 
    /// then flushing the ticks in memory to disk when triggered.
    /// </summary>
    public class OptionDataWriter
    {
        private string _zipPath;
        private string _entryPath;
        private Symbol _symbol;
        private TickType _tickType;
		private DateTime _lastTickTime;
		private long _linesWritten;
        private Resolution _resolution;
        private Queue<IBaseData> _queue;
        private string _dataDirectory;
        private IDataConsolidator _consolidator;
        private DateTime _referenceDate;
        private static string[] _windowsRestrictedNames =
        {
            "con", "prn", "aux", "nul"
        };

		public DateTime ReferenceDate
		{
			get { return _referenceDate; }
		}

        /// <summary>
        /// Zip entry name for the option contract
        /// </summary>
        public string EntryPath
        {
            get
            {
                if (_entryPath == null)
                {
                    _entryPath = SafeName(LeanData.GenerateZipEntryName(_symbol, _referenceDate, _resolution, _tickType));
                }
                return _entryPath;
            }
            set { _entryPath = value; }
        }

        /// <summary>
        /// Zip file path for the option contract collection
        /// </summary>
        public string ZipPath
        {
            get
            {
                if (_zipPath == null)
                {
                    _zipPath = Path.Combine(_dataDirectory, SafeName(LeanData.GenerateRelativeZipFilePath(Safe(_symbol), _referenceDate, _resolution, _tickType).Replace(".zip", string.Empty))) + ".zip";
                }
                return _zipPath;
            }
            set { _zipPath = value; }
        }

        /// <summary>
        /// Public access to the processor symbol
        /// </summary>
        public Symbol Symbol
        {
            get { return _symbol; }
        }

        /// <summary>
        /// Output base data queue for processing in memory
        /// </summary>
        public Queue<IBaseData> Queue
        {
            get { return _queue; }
        }

        /// <summary>
        /// Accessor for the final enumerator
        /// </summary>
        public Resolution Resolution
        {
            get { return _resolution; }
        }

        /// <summary>
        /// Type of this option processor. 
        /// ASOP's are grouped trade type for file writing.
        /// </summary>
        public TickType TickType
        {
            get { return _tickType; }
            set { _tickType = value; }
        }

        /// <summary>
        /// Create a new AlgoSeekOptionsProcessor for enquing consolidated bars and flushing them to disk
        /// </summary>
        /// <param name="symbol">Symbol for the processor</param>
        /// <param name="date">Reference date for the processor</param>
        /// <param name="tickType">TradeBar or QuoteBar to generate</param>
        /// <param name="resolution">Resolution to consolidate</param>
        /// <param name="dataDirectory">Data directory for LEAN</param>
        public OptionDataWriter(Symbol symbol, DateTime date, TickType tickType, Resolution resolution, string dataDirectory)
        {
            _symbol = Safe(symbol);
            _tickType = tickType;
            _referenceDate = date;
            _resolution = resolution;
            _queue = new Queue<IBaseData>();
            _dataDirectory = dataDirectory;

            // Setup the consolidator for the requested resolution
            if (resolution == Resolution.Tick) throw new NotSupportedException();

            switch (tickType)
            {
                case TickType.Trade:
                    _consolidator = new TickConsolidator(resolution.ToTimeSpan());
                    break;
                case TickType.Quote:
                    _consolidator = new TickQuoteBarConsolidator(resolution.ToTimeSpan());
                    break;
                case TickType.OpenInterest:
                    _consolidator = new OpenInterestConsolidator(resolution.ToTimeSpan());
                    break;
            }

            // On consolidating the bars put the bar into a queue in memory to be written to disk later.
            _consolidator.DataConsolidated += (sender, consolidated) =>
            {
                _queue.Enqueue(consolidated);
            };
        }

        /// <summary>
        /// Process the tick; add to the con
        /// </summary>
        /// <param name="data"></param>
        public void Process(Tick data)
        {
            if (data.TickType != _tickType)
            {
                return;
            }

			_lastTickTime = data.EndTime;

			_consolidator.Update(data);
        }

		public void Enqueue(QuoteBar quoteBar)
		{
			_lastTickTime = quoteBar.EndTime;
			_queue.Enqueue(quoteBar);
		}

        /// <summary>
        /// Write the in memory queues to the disk.
        /// </summary>
        /// <param name="frontierTime">Current foremost tick time</param>
        /// <param name="finalFlush">Indicates is this is the final push to disk at the end of the data</param>
        public void FlushBuffer(DateTime frontierTime, bool finalFlush)
        {
            //Force the consolidation if time has past the bar
            _consolidator.Scan(frontierTime);

            // If this is the final packet dump it to the queue
            if (finalFlush && _consolidator.WorkingData != null)
            {
                _queue.Enqueue(_consolidator.WorkingData);
            }
        }

        /// <summary>
        /// Add filtering to safe check the symbol for windows environments
        /// </summary>
        /// <param name="symbol">Symbol to rename if required</param>
        /// <returns>Renamed symbol for reserved names</returns>
        private static Symbol Safe(Symbol symbol)
        {
            if (OS.IsWindows)
            {
                if (_windowsRestrictedNames.Contains(symbol.Value.ToLower()) ||
                    _windowsRestrictedNames.Contains(symbol.Underlying.Value.ToLower()))
                {
                    symbol = Symbol.CreateOption(SafeName(symbol.Underlying.Value), Market.USA, OptionStyle.American, symbol.ID.OptionRight, symbol.ID.StrikePrice, symbol.ID.Date);
                }
            }
            return symbol;
        }

        private static string SafeName(string fileName)
        {
            if (OS.IsWindows)
            {
                if (_windowsRestrictedNames.Contains(fileName.ToLower()))
                    return "_" + fileName;
            }
            return fileName;
        }

		public long SaveToDisk()
		{
			return this.WriteToDisk(_lastTickTime, true);
		}

        /// <summary>
        /// Write the processor queues to disk
        /// </summary>
        /// <param name="peekTickTime">Time of the next tick in the stream</param>
        /// <param name="step">Period between flushes to disk</param>
        /// <param name="final">Final push to disk</param>
        /// <returns></returns>
        private long WriteToDisk(DateTime peekTickTime, bool final = false)
        {
			_linesWritten = 0;

			FlushBuffer(peekTickTime, final);

            try
            {
                //var tickType = type;
                string zip = string.Empty;

                try
                {
                    //var symbol = this.Symbol;
                    zip = this.ZipPath.Replace(".zip", string.Empty);

                    var tempFileName = Path.Combine(zip, this.EntryPath);

                    Directory.CreateDirectory(zip);
                    File.WriteAllText(tempFileName, FileBuilder());
                }
                catch (Exception err)
                {
                    Log.Error("AlgoSeekOptionsConverter.WriteToDisk() returned error: " + err.Message + " zip name: " + zip);
                }
            }
            catch (Exception err)
            {
                Log.Error("AlgoSeekOptionsConverter.WriteToDisk() returned error: " + err.Message);
            }

			return _linesWritten;
        }

        /// <summary>
        /// Output a list of basedata objects into a string csv line.
        /// </summary>
        /// <returns></returns>
        private string FileBuilder()
        {
            var sb = new StringBuilder();
            foreach (var data in this.Queue)
            {
				sb.AppendLine(LeanData.GenerateLine(data, SecurityType.Option, this.Resolution));
				_linesWritten++;
			}
            return sb.ToString();
        }

		/// <summary>
		/// Compress the queue buffers directly to a zip file. Lightening fast as streaming ram-> compressed zip.
		/// </summary>
		public static void Package(string dataDir)
		{
			//var zipper = OS.IsWindows ? "C:/Program Files/7-Zip/7z.exe" : "7z";

			Log.Trace("AlgoSeekOptionsConverter.Package(): Zipping all files ...");

			var destination = Path.Combine(dataDir, "option");
			//var dateMask = date.ToString(DateFormat.EightCharacter);

			var files =
				Directory.EnumerateFiles(destination, "*.csv", SearchOption.AllDirectories)
				.GroupBy(x => Directory.GetParent(x).FullName);

			//Zip each file massively in parallel.
			Parallel.ForEach(files, file =>
			{
				try
				{
					var outputFileName = file.Key + ".zip";
					// Create and open a new ZIP file
					var filesToCompress = Directory.GetFiles(file.Key, "*.csv", SearchOption.AllDirectories);
					using (var zip = ZipFile.Open(outputFileName, ZipArchiveMode.Create))
					{
						Log.Trace("AlgoSeekOptionsConverter.Package(): Zipping " + outputFileName);

						foreach (var fileToCompress in filesToCompress)
						{
							// Add the entry for each file
							zip.CreateEntryFromFile(fileToCompress, Path.GetFileName(fileToCompress), CompressionLevel.NoCompression);
						}
					}

					try
					{
						Directory.Delete(file.Key, true);
					}
					catch (Exception err)
					{
						Log.Error("AlgoSeekOptionsConverter.Package(): Directory.Delete returned error: " + err.Message);
					}
				}
				catch (Exception err)
				{
					Log.Error("File: {0} Err: {1} Source {2} Stack {3}", file, err.Message, err.Source, err.StackTrace);
				}
			});
		}
	}
}