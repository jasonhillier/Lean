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
using QuantConnect.Queues;
using QuantConnect.Packets;
using Newtonsoft.Json;

namespace QuantConnect.ToolBox.AlgoOptimizer
{
    class Program
    {
        /// <summary>
        /// </summary>
        public static void Main(string[] args)
        {
            if (args.Length < 0)
            {
                Console.WriteLine("Usage: AlgoOptimizer");
                Environment.Exit(1);
            }

            //setup logging
            Log.LogHandler = new CompositeLogHandler(new ILogHandler[] { new ConsoleLogHandler() });

			List<Dictionary<string, string>> generatedParameters = new List<Dictionary<string, string>>();

			var parameters = JsonConvert.DeserializeObject<Dictionary<string, string>>(Config.Get("parameter-ranges"));
			recursivelyGenerate(parameters, generatedParameters);

			Log.Trace("Generated " + generatedParameters.Count + " parameter variants");
			Console.WriteLine("Press any key to transmit");
			Console.ReadLine();

			var queue = new AmazonSQSJobQueue();
			queue.Connect();
			foreach(var p in generatedParameters)
			{
				queue.SendJobParameters(p);
			}

			Log.Trace("Sent messages.");
			Console.ReadLine();
        }

		public static void recursivelyGenerate(Dictionary<string, string> pPrototype, List<Dictionary<string, string>> pGeneratedParameters, int depth = 0)
		{
			Dictionary<string, string> parameterSet = new Dictionary<string, string>();

			foreach (var kvp in pPrototype)
			{
				if (kvp.Value.IndexOf('[') >= 0)
				{
					var paramterRange = JsonConvert.DeserializeObject<List<string>>(kvp.Value);
					foreach (var p in paramterRange)
					{
						var clonedPrototype = Clone(pPrototype);
						clonedPrototype[kvp.Key] = p;

						recursivelyGenerate(clonedPrototype, pGeneratedParameters, depth + 1);
					}
					return; //end of recurse branch
				}
				else
				{
					parameterSet[kvp.Key] = kvp.Value;
				}
			}

			pGeneratedParameters.Add(parameterSet);
		}

		public static Dictionary<string,string> Clone(Dictionary<string,string> pSource)
		{
			var clonedDictionary = new Dictionary<string, string>();
			foreach(var kvp in pSource)
			{
				clonedDictionary[kvp.Key] = kvp.Value;
			}
			return clonedDictionary;
		}
    }
}
