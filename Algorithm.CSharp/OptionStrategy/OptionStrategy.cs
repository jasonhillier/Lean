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
		protected int _ItmDepth;

        public OptionStrategy(QCAlgorithm Algo, Option Option, int MaxTiers = 3, int PositionSizeStart=1, int itmDepth = 3)
        {
            _Algo = Algo;
            _MaxTiers = MaxTiers;
            _Option = Option;
            _OptionSymbol = Option.Symbol;
            _PositionSizeStart = PositionSizeStart;
			_ItmDepth = itmDepth;


			Console.WriteLine("SECURITY ID = " + _OptionSymbol.Value);

            // set our strike/expiry filter for this option chain
            _Option.SetFilter(u => u.Strikes(-100, +100)
                                   .Expiration(TimeSpan.Zero, TimeSpan.FromDays(180)));
        }

        protected void _Log(string Text, params object[] args)
        {
            _Algo.Debug(String.Format(Text, args));
        }

        public bool MarketBuyOptions(List<OptionContract> Contracts)
        {
			if (Contracts == null || Contracts.Count == 0)
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
                _Algo.MarketOrder(contract.Symbol, _PositionSizeStart * this.InvestedTiers()+1);
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
			// we find nearby ITM put contract with closest expiration (at least 10 days away)
			var itmContracts = Chain
				.Where(x => x.Right == OptionRight.Put)
				.Where(x => (x.Strike - Chain.Underlying.Price) > 0)
				.Where(x=>(x.Expiry - x.Time).TotalDays >= 10)
				.OrderBy(x => x.Expiry)
				.ThenBy(x => x.Strike - Chain.Underlying.Price).ToList();

			var contract = itmContracts.Take(_ItmDepth).LastOrDefault();

			if (contract != null)
            {
                Contracts.Add(contract);
                return 1;
            }
            else
            {
				Console.WriteLine("NO VALID CONTRACTS TO BUY!!");
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
				return null;
			}

            return chain;
        }

        public List<SecurityHolding> GetPositions()
        {
            var portfolio = _Algo.Portfolio.Securities.Where((s) =>
            {
				return (s.Value.HoldStock);
            });

            List<SecurityHolding> holdings = new List<SecurityHolding>();


            portfolio.All((k) =>
            {
				holdings.Add(k.Value.Holdings);
                return true;
            });

            return holdings;
        }

        public decimal AggregateProfitPercentage(Slice slice)
        {
            var holdings = GetPositions();
			if (holdings.Count == 0)
				return 0;

			decimal profitAggPercent = 0;

            holdings.All((h) =>
            {
				profitAggPercent += h.UnrealizedProfitPercent;
                return true;
            });

			return profitAggPercent / holdings.Count;
		}

        public decimal AggregateProfit(Slice slice)
        {
            var holdings = GetPositions();

            decimal profit = 0;

            holdings.All((h) =>
            {
                profit += h.UnrealizedProfit;
                return true;
            });

            return profit;
        }

        public bool CloseAll()
        {
            Console.WriteLine("========= CLOSE ALL!! ==============");
			var holdings = GetPositions();
			foreach(var sec in holdings)
			{
				_Algo.MarketOrder(sec.Symbol, -sec.Quantity);
			}
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
			int totalHoldings = 0;

			var positions = GetPositions();
			foreach(var pos in positions)
			{
				totalHoldings += (int)pos.Quantity;
			}

			return totalHoldings / _PositionSizeStart;
		}
    }
}
