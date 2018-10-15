using System;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities.Option;
using System.Collections.Generic;
using QuantConnect.Securities;

namespace QuantConnect.Algorithm.CSharp
{
    public class OptionTraverseStrategy
    {
        protected QCAlgorithm _Algo;
        protected Option _Option;
        protected Symbol _OptionSymbol;
        protected int _AtmSpread;
        protected int _MinDaysRemaining;
        protected int _MaxDaysRemaining;

        public OptionTraverseStrategy(QCAlgorithm Algo, Option Option, int AtmSpread = 2, int MinDaysRemaining = 2, int MaxDaysRemaining = 20)
        {
            this._Algo = Algo;
            this._Option = Option;
            this._OptionSymbol = Option.Symbol;
            this._AtmSpread = AtmSpread;
            this._MinDaysRemaining = MinDaysRemaining;
            this._MaxDaysRemaining = MaxDaysRemaining;

            this.OutsideOptionMinPrice = .20m;
            this.OutsideOptionMinSpread = 2;
        }

        public decimal OutsideOptionMinPrice { get; set; }
        public int OutsideOptionMinSpread { get; set; }
        public bool IsInvested
        {
            get
            {
                CheckIfAssigned();
                return _Algo.Portfolio.Securities.Any(x =>  x.Value.Symbol.SecurityType == SecurityType.Option &&
                                                            x.Value.Symbol.Underlying.Value == _Option.Symbol.Underlying.Value &&
                                                            x.Value.Invested);
            }
        }

        protected void _Log(string Text, params object[] args)
        {
            _Algo.Debug(String.Format(Text, args));
        }

        public void Open(Slice slice, bool biasDown, int quantity = 1)
        {
            var chain = GetOptionChain(slice);
            if (chain == null) return;
            var inOptions = _SelectInsideOptions(slice, chain, biasDown);
            if (inOptions == null) return;
            var outOptions = _SelectOutsideOptions(slice, chain);
            if (outOptions == null) return;

            if (biasDown)
            {
                //has atm PUT spread
                if (inOptions.Item2.Strike - outOptions.Item1.Strike < this.OutsideOptionMinSpread ||
                    outOptions.Item2.Strike - inOptions.Item1.Strike < this.OutsideOptionMinSpread)
                {
                    Console.WriteLine("Minimum spread contracts not found");
                    return;
                }
            }
            else
            {
                //has atm CALL spread
                if (inOptions.Item1.Strike - outOptions.Item1.Strike < this.OutsideOptionMinSpread ||
                    outOptions.Item2.Strike - inOptions.Item2.Strike < this.OutsideOptionMinSpread)
                {
                    Console.WriteLine("Minimum spread contracts not found");
                    return;
                }
            }

            _PlaceComboOrder(inOptions, outOptions, quantity);
        }

        protected virtual void _PlaceComboOrder(Tuple<OptionContract, OptionContract> inside, Tuple<OptionContract, OptionContract> outside, int Quantity = 1)
        {
            _Log("Placing combo option order...");
            //TODO: need to use advance order type so broker can dynamically adjust to order book but keep relative limit price

            //buy ATM option
            MarketBuyOptions(new List<OptionContract> { inside.Item1 }, Quantity);
            //short adjacent, and short outside options
            MarketShortOptions(new List<OptionContract>
            {
                inside.Item2,
                outside.Item1,
                outside.Item2
            }, Quantity);
        }

        public virtual bool MarketBuyOptions(List<OptionContract> Contracts, int Quantity = 1)
        {
            if (Contracts == null || Contracts.Count == 0)
                return false;

            Contracts.ForEach(contract =>
            {
                //seems wrong
                DateTime lastBarEndTime = _Option.Underlying.GetLastData().EndTime; //verify
                _Log("{0} Purchase {1} {2} @ {3} ({4} {5})",
                     lastBarEndTime.ToString(),
                     contract.Right.ToString().ToUpper(),
                     contract.Strike,
                     contract.AskPrice,
                     _Option.Underlying.Symbol,
                     _Option.Underlying.Price
                    );
                _Algo.MarketOrder(contract.Symbol, Quantity);
            });

            return true;
        }

        public virtual bool MarketShortOptions(List<OptionContract> Contracts, int Quantity = 1)
        {
            if (Contracts == null || Contracts.Count == 0)
                return false;

            Contracts.ForEach(contract =>
            {
                //seems wrong
                DateTime lastBarEndTime = _Option.Underlying.GetLastData().EndTime; //verify
                _Log("{0} Short {1} {2} @ {3} ({4} {5})",
                     lastBarEndTime.ToString(),
                     contract.Right.ToString().ToUpper(),
                     contract.Strike,
                     contract.AskPrice,
                     _Option.Underlying.Symbol,
                     _Option.Underlying.Price
                    );
                _Algo.MarketOrder(contract.Symbol, -Quantity);
            });

            return true;
        }

        protected virtual Tuple<OptionContract,OptionContract> _SelectInsideOptions(Slice slice, OptionChain Chain, bool biasDown)
        {
            // we find nearby OTM contracts within target expiration
            var otmContracts = Chain
                .Where(x => biasDown ? x.Right == OptionRight.Put : x.Right == OptionRight.Call)
                .Where(x => x.IsOTM(Chain))
                .Where(x => (x.Expiry - x.Time).TotalDays >= _MinDaysRemaining)
                .Where(x => (x.Expiry - x.Time).TotalDays <= _MaxDaysRemaining)
                .OrderBy(x => x.Expiry)
                .ThenBy(x => {
                    if (biasDown)
                        return Chain.Underlying.Price - x.Strike;
                    else
                        return x.Strike - Chain.Underlying.Price;
                }).ToList();
            if (otmContracts.Count > _AtmSpread)
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

        protected virtual Tuple<OptionContract, OptionContract> _SelectOutsideOptions(Slice slice, OptionChain Chain)
        {
            // find farthest OTM PUT option (sorted by furthest strike)
            var otmContracts = Chain
                .Where(x=>x.Right == OptionRight.Put)
                .Where(x => x.IsOTM(Chain))
                .Where(x => (x.Expiry - x.Time).TotalDays >= _MinDaysRemaining)
                .Where(x => (x.Expiry - x.Time).TotalDays <= _MaxDaysRemaining)
                .Where(x=> (x.AskPrice + x.BidPrice)/2 >= this.OutsideOptionMinPrice)
                .OrderBy(x => x.Expiry)
                .ThenBy(x => x.Strike - Chain.Underlying.Price).ToList();
            if (otmContracts.Count > 0)
            {
                var contractA = otmContracts.First();

                // find farthest OTM CALL option
                otmContracts = Chain
                    .Where(x => x.Right == OptionRight.Call)
                    .Where(x => x.IsOTM(Chain))
                    .Where(x => (x.Expiry - x.Time).TotalDays >= _MinDaysRemaining)
                    .Where(x => (x.Expiry - x.Time).TotalDays <= _MaxDaysRemaining)
                    .Where(x => (x.AskPrice + x.BidPrice) / 2 >= this.OutsideOptionMinPrice)
                    .Where(x => x.Expiry == contractA.Expiry)
                    .OrderBy(x => Chain.Underlying.Price - x.Strike).ToList();
                if (otmContracts.Count > 1)
                {
                    var contractB = otmContracts.First();

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

        public virtual OptionChain GetOptionChain(Slice slice)
        {
            OptionChain chain = null;
            slice.OptionChains.TryGetValue(_OptionSymbol, out chain);
            if (chain == null)
            {
                Console.WriteLine("<{0}> No option chain here!", slice.Time.ToString());
                return null;
            }

            return chain;
        }

        public bool CheckIfAssigned()
        {
            if (_Algo.Portfolio.Securities[this._OptionSymbol.Underlying.Value].Invested)
            {
                //dispose of assigned stock
                _Algo.MarketOrder(this._OptionSymbol.Underlying, -_Algo.Portfolio.Securities[this._OptionSymbol.Underlying.Value].Holdings.Quantity);
                return true;
            }

            return false;
        }
    }
}
