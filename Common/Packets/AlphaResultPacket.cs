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

/*
* QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
* Lean Algorithmic Trading Engine v2.2 Copyright 2015 QuantConnect Corporation.
*/

using System.Collections.Generic;
using Newtonsoft.Json;
using QuantConnect.Algorithm.Framework.Alphas;

namespace QuantConnect.Packets
{
    /// <summary>
    /// Provides a packet type for transmitting alpha data
    /// </summary>
    public class AlphaResultPacket : Packet
    {
        /// <summary>
        /// The user's id that deployed the alpha stream
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// The deployer alpha id. If this is a user backtest or live algo then this will not be specified
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string AlphaId { get; set; }

        /// <summary>
        /// The algorithm's unique identifier
        /// </summary>
        public string AlgorithmId { get; set; }

        /// <summary>
        /// The generated alphas
        /// </summary>
        public List<Alpha> Alphas { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AlphaResultPacket"/> class
        /// </summary>
        public AlphaResultPacket()
            : base(PacketType.AlphaResult)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AlphaResultPacket"/> class
        /// </summary>
        /// <param name="algorithmId">The algorithm's unique identifier</param>
        /// <param name="userId">The user's id</param>
        /// <param name="alphas">Alphas generated by the algorithm</param>
        public AlphaResultPacket(string algorithmId, int userId, List<Alpha> alphas)
            : base(PacketType.AlphaResult)
        {
            UserId = userId;
            AlgorithmId = algorithmId;
            Alphas = alphas;
        }
    }
}
