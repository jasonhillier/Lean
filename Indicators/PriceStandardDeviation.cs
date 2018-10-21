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
*/

using System;
using QuantConnect.Data.Market;

namespace QuantConnect.Indicators
{
    /// <summary>
    /// This indicator computes the n-period population standard deviation.
    /// </summary>
    public class PriceStandardDeviation : TradeBarIndicator
    {
        private StandardDeviation _std;
        //private CompositeIndicator<IndicatorDataPoint> _vwapStd;

        /// <summary>
        /// Initializes a new instance of the StandardDeviation class with the specified period.
        /// 
        /// Evaluates the standard deviation of samples in the lookback period. 
        /// On a dataset of size N will use an N normalizer and would thus be biased if applied to a subset.
        /// </summary>
        /// <param name="period">The sample size of the standard deviation</param>
        public PriceStandardDeviation(int period)
            : this("PRICESTD" + period, period)
        {
        }

        /// <summary>
        /// Initializes a new instance of the StandardDeviation class with the specified name and period.
        /// 
        /// Evaluates the standard deviation of samples in the lookback period. 
        /// On a dataset of size N will use an N normalizer and would thus be biased if applied to a subset.
        /// </summary>
        /// <param name="name">The name of this indicator</param>
        /// <param name="period">The sample size of the standard deviation</param>
        public PriceStandardDeviation(string name, int period)
            : base(name)
        {
            _std = new StandardDeviation(period);
        }

        public override bool IsReady
        {
            get { return _std.IsReady; }
        }

        public override void Reset()
        {
            _std.Reset();
        }

        protected override decimal ComputeNextValue(TradeBar input)
        {
            _std.Update(input.EndTime, input.Price);
            //Console.WriteLine("{0} = {1} {2}", input.Price, _std.Current.Value, this.IsReady);
            return _std.Current.Value;
        }
    }
}
