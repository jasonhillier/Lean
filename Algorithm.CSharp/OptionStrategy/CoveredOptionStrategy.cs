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
    public class CoveredOptionStrategy : OptionStrategy
    {
        protected OptionRight _Side;
        public CoveredOptionStrategy(QCAlgorithm Algo, Option Option, OptionRight Side = OptionRight.Put, int MaxTiers = 3, int PositionSizeStart = 1, int minDaysRemaining = 15)
            : base(Algo, Option, MaxTiers, PositionSizeStart, 0, minDaysRemaining)
        {
            _Side = Side;
        }

        public override bool MarketBuyOptions(List<OptionContract> Contracts, int OverrideQuantity = 0)
        {
			if (Contracts == null || Contracts.Count == 0)
                return false;

            int quantity = OverrideQuantity;
            if (quantity == 0)
                quantity = _PositionSizeStart * (this.InvestedTiers() + 1);

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
                _Algo.MarketOrder(contract.Symbol, quantity);
            });

            //if doing covered calls, market buy underlying
            if (_Side == OptionRight.Call)
            {
                _Algo.MarketOrder(_OptionSymbol.Underlying, quantity);
            }
            else
            {
                //otherwise, setup a synthetic short
                throw new NotImplementedException();
            }

			return true;
        }

        protected override int _GetNextTierOptions(Slice slice, List<OptionContract> Contracts, OptionChain Chain)
        {
            // we find nearby OTM contract with closest expiration (at least 10 days away)
            //TODO: make 'tier' definition customizable
            var targetContracts = Chain
                .Where(x => x.Right == _Side)
                .Where(x => x.IsOTM(Chain))
                .Where(x => (x.Expiry - x.Time).TotalDays >= _MinDaysRemaining)
                .OrderBy(x => x.Expiry)
                .ThenBy(x => x.Strike - Chain.Underlying.Price).ToList();

            var contract = targetContracts.LastOrDefault();

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
    }
}
