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
    public class OptionStrategy
    {
        private int _MaxTiers;
        protected QCAlgorithm _Algo;
        protected Option _Option;
        protected Symbol _OptionSymbol;
        protected int _PositionSizeStart;

        public OptionStrategy(QCAlgorithm Algo, Option Option, int MaxTiers = 3, int PositionSizeStart=1)
        {
            _Algo = Algo;
            _MaxTiers = MaxTiers;
            _Option = Option;
            _OptionSymbol = Option.Symbol;
            _PositionSizeStart = PositionSizeStart;

            Console.WriteLine("SECURITY ID = " + _OptionSymbol.Value);

            // set our strike/expiry filter for this option chain
            _Option.SetFilter(u => u.Strikes(-2, +2)
                                   .Expiration(TimeSpan.Zero, TimeSpan.FromDays(31)));
        }

        protected void _Log(string Text, params object[] args)
        {
            _Algo.Debug(String.Format(Text, args));
        }

        public bool MarketBuyOptions(List<OptionContract> Contracts)
        {
            if (Contracts == null)
                return false;
            
            Contracts.ForEach(contract=>
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
                _Algo.MarketOrder(contract.Symbol, _PositionSizeStart); //*this.InvestedTiers());
            });

            return true;
        }

        public bool MarketBuyNextTierOptions(Slice slice)
        {
            List<OptionContract> contracts = GetNextTierOptions(slice);

            return MarketBuyOptions(contracts);
        }

        public virtual List<OptionContract> GetNextTierOptions(Slice slice)
        {
            List<OptionContract> contracts = new List<OptionContract>();
            if (!_Algo.IsMarketOpen(_OptionSymbol.Underlying) ||
                IsMaxInvested())
            {
                //return empty list
                return contracts;
            }

            OptionChain chain = GetOptionChain(slice);
            if (chain != null)
            {
                _GetNextTierOptions(slice, contracts, chain);
            }

            return contracts;
        }

        protected virtual int _GetNextTierOptions(Slice slice, List<OptionContract> Contracts, OptionChain Chain)
        {
            // we find at the money (ATM) put contract with farthest expiration
            var atmContract = Chain
                .OrderByDescending(x => x.Expiry)
                .ThenBy(x => Math.Abs(Chain.Underlying.Price - x.Strike))
                .ThenByDescending(x => x.Right)
                .FirstOrDefault();

            if (atmContract != null)
            {
                Contracts.Add(atmContract);
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public virtual OptionChain GetOptionChain(Slice slice)
        {
            OptionChain chain = null;
            slice.OptionChains.TryGetValue(_OptionSymbol, out chain);
			if (chain == null)
			{
				Console.WriteLine("<{0}> No option chain here!", slice.Time.ToString());
			}

            return chain;
        }

        public List<SecurityHolding> GetPositions()
        {
            var portfolio = _Algo.Portfolio.Where((s) =>
            {
                return (s.Key.SecurityType == SecurityType.Option &&
                        s.Key.Underlying.Value == _Option.Symbol.Value);
            });

            List<SecurityHolding> holdings = new List<SecurityHolding>();


            portfolio.All((k) =>
            {
                holdings.Add(k.Value);
                return true;
            });

            return holdings;
        }

        public decimal AggregateProfitPercentage(Slice slice)
        {
            var holdings = GetPositions();

            decimal profit = 0;
            decimal capital = 1;

            holdings.All((h) =>
            {
                profit += h.NetProfit;
                capital += h.HoldingsCost;
                return true;
            });

            return profit / capital;
        }

        public decimal AggregateProfit(Slice slice)
        {
            var holdings = GetPositions();

            decimal profit = 0;

            holdings.All((h) =>
            {
                profit += h.NetProfit;
                return true;
            });

            return profit;
        }

        public bool CloseAll()
        {
            Console.WriteLine("CLOSE ALL!!");
            return true;
        }

        public bool IsInvested()
        {
            return (this.InvestedTiers() > 0);
        }

        public bool IsMaxInvested()
        {
            return (this.InvestedTiers() >= _MaxTiers);
        }

        public virtual int InvestedTiers()
        {
            //_Algo.Portfolio.
            //TODO: we can compute this by finding how many strikes we have purchased long
            return GetPositions().Count;
        }
    }
}
