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
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Securities.Option;
using QuantConnect.Util;
using System.Linq;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    /// <summary>
    /// Uses Wilder's RSI to create insights. Using default settings, a cross over below 30 or above 70 will
    /// trigger a new insight.
    /// </summary>
    public class OptionTimerAlphaModel : OptionAlphaModel
    {
		private int _daysOpenBegin;
		private int _daysOpenEnd;
		private int _daysCloseBegin;
		private int _daysCloseEnd;

		/// <summary>
		/// Initializes a new instance of the <see cref="RsiAlphaModel"/> class
		/// </summary>
		/// <param name="period">The RSI indicator period</param>
		/// <param name="resolution">The resolution of data sent into the RSI indicator</param>
		public OptionTimerAlphaModel(
            int daysOpenBegin = 35,
			int daysOpenEnd = 30,
			int daysCloseBegin = 15,
			int daysCloseEnd = 10
			)
        {
			_daysOpenBegin = daysOpenBegin;
			_daysOpenEnd = daysOpenEnd;
			_daysCloseBegin = daysCloseBegin;
			_daysCloseEnd = daysCloseEnd;

			Name = $"{nameof(OptionTimerAlphaModel)}({_daysOpenBegin},{_daysCloseBegin})";
        }

        /// <summary>
        /// Updates this alpha model with the latest data from the algorithm.
        /// This is called each time the algorithm receives data for subscribed securities
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="data">The new data available</param>
        /// <returns>The new insights generated</returns>
        public override IEnumerable<Insight> Update(QCAlgorithmFramework algorithm, Slice data)
        {
			if (data == null || data.OptionChains == null || data.OptionChains.Count() < 1) return new List<Insight>();

			var chainItem = data.OptionChains.First();
			var symbol = chainItem.Key.Underlying;

			var options = OptionTools.GetOptionsForExpiry(algorithm, symbol, 0);

			var count = options.Where((o) =>
				(o.Expiry - data.Time).TotalDays <= _daysOpenBegin &&
				(o.Expiry - data.Time).TotalDays >= _daysOpenEnd)
				.Count();
			if (count > 0)
			{
				return new List<Insight>() { Insight.Price(chainItem.Key.Underlying, TimeSpan.FromDays(1), InsightDirection.Up) };
			}

			count = options.Where((o) =>
				Math.Abs((o.Expiry - data.Time).TotalDays) <= _daysCloseBegin &&
				Math.Abs((o.Expiry - data.Time).TotalDays) >= _daysCloseEnd)
				.Count();
			if (count > 0)
			{
				return new List<Insight>() { Insight.Price(chainItem.Key.Underlying, TimeSpan.FromDays(1), InsightDirection.Down) };
			}

            return new List<Insight>();
		}
    }
}