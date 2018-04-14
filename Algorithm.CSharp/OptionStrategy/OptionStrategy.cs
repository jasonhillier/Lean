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
		protected int _MinDaysRemaining;
		protected Stats _Stats;

		public OptionStrategy(QCAlgorithm Algo, Option Option, int MaxTiers = 3, int PositionSizeStart=1, int itmDepth = 3, int minDaysRemaining = 15)
        {
            _Algo = Algo;
            _MaxTiers = MaxTiers;
            _Option = Option;
            _OptionSymbol = Option.Symbol;
            _PositionSizeStart = PositionSizeStart;
			_ItmDepth = itmDepth;
			_MinDaysRemaining = minDaysRemaining;

			Console.WriteLine("SECURITY ID = " + _OptionSymbol.Value);

            // set our strike/expiry filter for this option chain
            _Option.SetFilter(u => u.Strikes(-20, +30)
                                   .Expiration(TimeSpan.Zero, TimeSpan.FromDays(90)));
        }

        protected void _Log(string Text, params object[] args)
        {
            _Algo.Debug(String.Format(Text, args));
        }

        public bool MarketBuyOptions(List<OptionContract> Contracts, int OverrideQuantity = 0)
        {
			if (Contracts == null || Contracts.Count == 0)
                return false;

			Contracts.ForEach(contract=>
            {
				int quantity = OverrideQuantity;
				if (quantity == 0)
					quantity = _PositionSizeStart * (this.InvestedTiers() + 1);
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
                _Algo.MarketOrder(contract.Symbol, quantity);
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
			//TODO: make 'tier' definition customizable
			var itmContracts = Chain
				.Where(x => x.Right == OptionRight.Put)
				.Where(x => (x.Strike - Chain.Underlying.Price) > 0)
				.Where(x=>(x.Expiry - x.Time).TotalDays >= _MinDaysRemaining)
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

		public bool RolloverBeforeExpiry(Slice slice)
		{
			int contractsClosedCount = CloseAnyBeforeExpiry(slice);

			if (contractsClosedCount > 0)
			{
				_Stats.RolloverCounter++;

				var nextOptions = GetNextTierOptions(slice);
				if (nextOptions != null && nextOptions.Count > 0)
				{
					_Stats.RolloverContracts += contractsClosedCount;
					return MarketBuyOptions(nextOptions, contractsClosedCount);
				}
			}

			return false;
		}

		public int CloseAnyBeforeExpiry(Slice slice)
		{
			var options = GetPositions();
			int contractsClosedCount = 0;

			options.All((p) =>
			{
				if ((p.Expiry - slice.Time).TotalDays < 1)
				{
					Console.WriteLine("=== CLOSING BEFORE EXPIRY ===");
					contractsClosedCount += Math.Abs((int)ClosePosition(p).Quantity);
				}
				return true;
			});

			if (contractsClosedCount > 0) _Stats.ExpiryCloseCounter++;

			return contractsClosedCount;
		}

		public List<Option> GetOTMPositions()
		{
			var positions = GetPositions();

			return positions.Where((p) =>
			{
				return p.GetIntrinsicValue(p.Underlying.Price) <= 0;
			}).ToList();
		}

        public List<Option> GetPositions()
        {
			return _Algo.Portfolio.Securities.Where((s) =>
			{
				return (s.Value is Option &&
						s.Value.HoldStock);
			})
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
			.Values.Cast<Option>()
			.ToList();
        }

        public decimal AggregateProfitPercentage(Slice slice)
        {
            var holdings = GetPositions();
			if (holdings.Count == 0)
				return 0;

			decimal profitAggPercent = 0;

            holdings.All((h) =>
            {
				profitAggPercent += h.Holdings.UnrealizedProfitPercent;
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
                profit += h.Holdings.UnrealizedProfit;
                return true;
            });

            return profit;
        }

		public List<OrderTicket> CloseAll() { return ClosePositions(); }
        public List<OrderTicket> ClosePositions(List<Option> positions = null)
        {
			List<OrderTicket> orders = new List<OrderTicket>();
            Console.WriteLine("========= CLOSE POSITIONS ==============");
			if (positions == null)
				positions = GetPositions();

			foreach(var optionPos in positions)
			{
				orders.Add(
					ClosePosition(optionPos)
					);
			}

			return orders;
        }

		public OrderTicket ClosePosition(Option position)
		{
			return _Algo.MarketOrder(position.Holdings.Symbol, -position.Holdings.Quantity);
		}

        public bool IsInvested()
        {
            return (this.InvestedTiers() > 0);
        }

        public bool IsMaxInvested()
        {
            if (this.InvestedTiers() >= _MaxTiers)
			{
				Console.WriteLine("!!! MAX TIER REACHED !!!");
				_Stats.MaxTierCounter++;
				return true;
			}
			else
			{
				return false;
			}
        }

        public virtual int InvestedTiers()
        {
			int totalHoldings = 0;

			var positions = GetPositions();
			foreach(var pos in positions)
			{
				totalHoldings += (int)pos.Holdings.Quantity;
			}

			int tier = totalHoldings / _PositionSizeStart;

			if (tier > _Stats.HighestTier) _Stats.HighestTier = tier;
			return tier;
		}

		public void PrintStats()
		{
			foreach(var key in _Stats.GetType().GetProperties())
			{
				var value = key.GetValue(_Stats);
				_Algo.RuntimeStatistics[key.Name] = value.ToString();

				_Algo.Log(key.Name + ":\t" + value.ToString());
			}
		}

		public struct Stats
		{
			public int MaxTierCounter { get; set; }
			public int HighestTier { get; set; }
			public int RolloverCounter { get; set; }
			public int RolloverContracts { get; set; }
			public int ExpiryCloseCounter { get; set; }
		}
    }
}
