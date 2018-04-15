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

			var queue = new AmazonSQSJobQueue();
			queue.Connect();
			//queue.SendJob(new AlgorithmNodePacket());

            Log.Trace("Downloading {0} quotes from {1}:{2}...", args);
        }
    }
}
