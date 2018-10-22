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
using System.Linq;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Securities;
using QuantConnect.Securities.Option;

namespace QuantConnect.Algorithm.Framework.Portfolio
{
    /// <summary>
    /// Opens an OTM put if going down, or an OTM call if going up.
    /// </summary>
    public class RiskReversalPortfolioModel : BaseOptionPortfolioModel
    {
        protected QCAlgorithmFramework _Algo;
        protected int _AtmSpread;
        protected int _MinDaysRemaining;
        protected int _MaxDaysRemaining;

        public RiskReversalPortfolioModel()
            : this(2,2,20)
        {
        }

        public RiskReversalPortfolioModel(int AtmSpread, int MinDaysRemaining, int MaxDaysRemaining)
        {
            this._AtmSpread = AtmSpread;
            this._MinDaysRemaining = MinDaysRemaining;
            this._MaxDaysRemaining = MaxDaysRemaining;

            this.OutsideOptionMinPrice = .20m;
            this.OutsideOptionMinSpread = 2;

            //TODO: strike selector?
            this._OptionFilter = (o) => {
                return o.Strikes(-10, 20)
                        .Expiration(TimeSpan.FromDays(MinDaysRemaining), TimeSpan.FromDays(MaxDaysRemaining));
            };
        }

        public decimal OutsideOptionMinPrice { get; set; }
        public int OutsideOptionMinSpread { get; set; }
        public bool IsInvested(Symbol underlying)
        {
            //CheckIfAssigned();
            return _Algo.Portfolio.Securities.Any(x => x.Value.Symbol.SecurityType == SecurityType.Option &&
                                                  x.Value.Symbol.Underlying.Value == underlying.Value &&
                                                        x.Value.Invested);
        }

        /// <summary>
        /// When a new insight comes in, close down anything that might be open.
        /// </summary>
        public override List<IPortfolioTarget> CloseTargetsFromInsight(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
        {
            //hold til expiration
            return null;
        }

        public override List<IPortfolioTarget> OpenTargetsFromInsight(QCAlgorithmFramework algorithm, Symbol symbol, Insight insight)
        {
            this._Algo = algorithm;

            if (insight.Direction == InsightDirection.Flat)
                return null;
            if (this.IsInvested(symbol)) //TODO: probably have execution manager take care of this
                return null;

            var inOptions = _SelectInsideOptions(symbol, insight.Direction == InsightDirection.Down);
            if (inOptions == null) return null;
            var outOptions = _SelectOutsideOptions(symbol);
            if (outOptions == null) return null;

            if (insight.Direction == InsightDirection.Down)
            {
                //has atm PUT spread
                if (inOptions.Item2.Strike - outOptions.Item1.Strike < this.OutsideOptionMinSpread ||
                    outOptions.Item2.Strike - inOptions.Item1.Strike < this.OutsideOptionMinSpread)
                {
                    _Algo.Log("Minimum spread contracts not found");
                    return null;
                }
            }
            else
            {
                //has atm CALL spread
                if (inOptions.Item1.Strike - outOptions.Item1.Strike < this.OutsideOptionMinSpread ||
                    outOptions.Item2.Strike - inOptions.Item2.Strike < this.OutsideOptionMinSpread)
                {
                    _Algo.Log("Minimum spread contracts not found");
                    return null;
                }
            }

            var targets = new List<IPortfolioTarget>();

            //long first inside
            targets.Add(new PortfolioTarget(inOptions.Item1.Symbol, this.PositionSize));
            //then short all the others
            targets.Add(new PortfolioTarget(inOptions.Item2.Symbol, -this.PositionSize));
            targets.Add(new PortfolioTarget(outOptions.Item1.Symbol, -this.PositionSize));
            targets.Add(new PortfolioTarget(outOptions.Item2.Symbol, -this.PositionSize));

            return targets;
        }

        protected virtual Tuple<OptionContract, OptionContract> _SelectInsideOptions(Symbol underlying, bool biasDown)
        {
            // we find nearby OTM contracts within target expiration
            var otmContracts = this.GetOTM(this._Algo, underlying, biasDown ? OptionRight.Put : OptionRight.Call);
            if (otmContracts !=null && otmContracts.Count() > _AtmSpread)
            {
                //select ATM (to buy) and x away from ATM (to short)
                var contractA = otmContracts.First();
                var contractB = otmContracts.Skip(_AtmSpread).First();

                if (contractA != null &&
                    contractB != null)
                {
                    //Buy A, Sell B
                    return Tuple.Create(contractA, contractB);
                }
            }

            Console.WriteLine("No valid INSIDE contracts!!");
            return null;
        }

        protected virtual Tuple<OptionContract, OptionContract> _SelectOutsideOptions(Symbol underlying)
        {
            // find farthest OTM PUT option (sorted by furthest strike)
            var otmContracts = this.GetOTM(this._Algo, underlying, OptionRight.Put)
                                   .Where(x => (x.AskPrice + x.BidPrice) / 2 >= this.OutsideOptionMinPrice);
            if (otmContracts != null && otmContracts.Count() > 0)
            {
                var contractA = otmContracts.Last(); //furthest

                // find farthest OTM CALL option
                otmContracts = this.GetOTM(this._Algo, underlying, OptionRight.Call)
                                   .Where(x => (x.AskPrice + x.BidPrice) / 2 >= this.OutsideOptionMinPrice);
                if (otmContracts != null && otmContracts.Count() > 1)
                {
                    var contractB = otmContracts.Last(); //furthest

                    if (contractA != null &&
                        contractB != null)
                    {
                        //SELL call AND put
                        return Tuple.Create(contractA, contractB);
                    }
                }
            }
            Console.WriteLine("No valid OUTSIDE contracts!!");
            return null;
        }
    }
}