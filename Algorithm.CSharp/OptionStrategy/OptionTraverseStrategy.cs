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

        public OptionTraverseStrategy(QCAlgorithm Algo, Option Option, int AtmSpread = 2, int MinDaysRemaining = 2, int MaxDaysRemaining = 10)
        {
            this._Algo = Algo;
            this._Option = Option;
            this._OptionSymbol = Option.Symbol;
            this._AtmSpread = AtmSpread;
            this._MinDaysRemaining = MinDaysRemaining;
            this._MaxDaysRemaining = MaxDaysRemaining;

            this.OutsideOptionMinPrice = .20m;
            this.OutsideOptionMinSpread = 2;
            this.IsInvested = false;
        }

        public decimal OutsideOptionMinPrice { get; set; }
        public int OutsideOptionMinSpread { get; set; }

        public void Open(Slice slice, bool biasDown)
        {
            var chain = GetOptionChain(slice);
            if (chain == null) return;
            var inOptions = _SelectInsideOptions(slice, chain, biasDown);
            if (inOptions == null) return;
            var outOptions = _SelectOutsideOptions(slice, chain);
            if (outOptions == null) return;

            this.IsInvested = true;
        }

        public bool IsInvested { get; set; }

        protected virtual Tuple<OptionContract,OptionContract> _SelectInsideOptions(Slice slice, OptionChain Chain, bool biasDown)
        {
            // we find nearby OTM contracts within target expiration
            var otmContracts = Chain
                .Where(x => biasDown ? x.Right == OptionRight.Put : x.Right == OptionRight.Call)
                .Where(x => x.IsOTM(Chain))
                .Where(x => (x.Expiry - x.Time).TotalDays >= _MinDaysRemaining)
                .Where(x => (x.Expiry - x.Time).TotalDays <= _MaxDaysRemaining)
                .OrderBy(x => x.Expiry)
                .ThenBy(x => x.Strike - Chain.Underlying.Price).ToList();
            if (otmContracts.Count > _AtmSpread)
            {
                //select ATM (to buy) and x away from ATM (to short)
                var contractA = otmContracts.First();
                var contractB = otmContracts.Take(_AtmSpread).First();

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
            // find farthest OTM PUT option
            var otmContracts = Chain
                .Where(x=>x.Right == OptionRight.Put)
                .Where(x => x.IsOTM(Chain))
                .Where(x => (x.Expiry - x.Time).TotalDays >= _MinDaysRemaining)
                .Where(x => (x.Expiry - x.Time).TotalDays <= _MaxDaysRemaining)
                .Where(x=> (x.AskPrice + x.BidPrice)/2 >= this.OutsideOptionMinPrice)
                .Where(x=> x.Strike < Chain.Underlying.Price - this.OutsideOptionMinSpread)
                .OrderBy(x => x.Expiry)
                .ThenBy(x => x.Strike - Chain.Underlying.Price).ToList();
            if (otmContracts.Count > 0)
            {
                var contractA = otmContracts.Last();

                // find farthest OTM CALL option
                otmContracts = Chain
                    .Where(x => x.Right == OptionRight.Call)
                    .Where(x => x.IsOTM(Chain))
                    .Where(x => (x.Expiry - x.Time).TotalDays >= _MinDaysRemaining)
                    .Where(x => (x.Expiry - x.Time).TotalDays <= _MaxDaysRemaining)
                    .Where(x => (x.AskPrice + x.BidPrice) / 2 >= this.OutsideOptionMinPrice)
                    .Where(x => x.Strike > Chain.Underlying.Price - this.OutsideOptionMinSpread)
                    .OrderBy(x => x.Expiry)
                    .ThenBy(x => x.Strike - Chain.Underlying.Price).ToList();
                if (otmContracts.Count > 0)
                {
                    var contractB = otmContracts.Last();

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
    }
}
