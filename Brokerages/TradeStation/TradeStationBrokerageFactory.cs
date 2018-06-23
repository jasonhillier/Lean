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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Logging;
using QuantConnect.Packets;
using QuantConnect.Util;

namespace QuantConnect.Brokerages.TradeStation
{
    /// <summary>
    /// Provides an implementations of IBrokerageFactory that produces a TradeStationBrokerage
    /// </summary>
    public class TradeStationBrokerageFactory : BrokerageFactory
    {
        /// <summary>
        /// Gets tradier values from configuration
        /// </summary>
        public static class Configuration
        {
            /// <summary>
            /// Gets the account ID to be used when instantiating a brokerage
            /// </summary>
            /*
            public static int QuantConnectUserID
            {
                get { return Config.GetInt("qc-user-id"); }
            }
            */

            /// <summary>
            /// Gets flag indicating if connecting to simulation api
            /// </summary>
            public static bool Simulation
            {
                get { return Config.GetBool("tradestation-simulation"); }
            }

            /// <summary>
            /// Gets the account ID to be used when instantiating a brokerage
            /// </summary>
            public static string AccountID
            {
                get { return Config.Get("tradestation-account-id"); }
            }

            /// <summary>
            /// Gets the access token from configuration
            /// </summary>
            public static string AccessToken
            {
                get { return Config.Get("tradestation-access-token"); }
            }
        }

        /// <summary>
        /// Initializes a new instance of he TradierBrokerageFactory class
        /// </summary>
        public TradeStationBrokerageFactory()
            : base(typeof(TradeStationBrokerage))
        {
        }

        /// <summary>
        /// Gets the brokerage data required to run the brokerage from configuration/disk
        /// </summary>
        /// <remarks>
        /// The implementation of this property will create the brokerage data dictionary required for
        /// running live jobs. See <see cref="IJobQueueHandler.NextJob"/>
        /// </remarks>
        public override Dictionary<string, string> BrokerageData
        {
            get
            {
                var data = new Dictionary<string, string>();
                data.Add("tradestation-account-id", Configuration.AccountID);
                data.Add("tradestation-access-token", Configuration.AccessToken);
                data.Add("tradestation-simulation", Configuration.Simulation.ToString());
                return data;
            }
        }

        /// <summary>
        /// Gets a new instance of the <see cref="TradierBrokerageModel"/>
        /// </summary>
        public override IBrokerageModel BrokerageModel
        {
            get { return new TradierBrokerageModel(); }
        }

        /// <summary>
        /// Creates a new IBrokerage instance
        /// </summary>
        /// <param name="job">The job packet to create the brokerage for</param>
        /// <param name="algorithm">The algorithm instance</param>
        /// <returns>A new brokerage instance</returns>
        public override IBrokerage CreateBrokerage(LiveNodePacket job, IAlgorithm algorithm)
        {
            var errors = new List<string>();
            var accountID = Read<string>(job.BrokerageData, "tradestation-account-id", errors);
            var accessToken = Read<string>(job.BrokerageData, "tradestation-access-token", errors);
            var simulation = Read<bool>(job.BrokerageData, "tradestation-simulation", errors);

            var brokerage = new TradeStationBrokerage(algorithm.Transactions, algorithm.Portfolio, accountID, accessToken, simulation);

            //Add the brokerage to the composer to ensure its accessible to the live data feed.
            Composer.Instance.AddPart<IDataQueueHandler>(brokerage);
            Composer.Instance.AddPart<IHistoryProvider>(brokerage);
            return brokerage;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public override void Dispose()
        {
        }
    }
}
