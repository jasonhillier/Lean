using System;
using QuantConnect.Data.Market;

namespace QuantConnect.Algorithm.Framework
{
    public static class OptionTools
    {
        public static bool TryGetOptionChain(QCAlgorithmFramework algorithm, Symbol underlyingSymbol, out OptionChain chain)
        {
            chain = null;

            if (algorithm.CurrentSlice == null ||
                algorithm.CurrentSlice.OptionChains == null)
                return false;

            //find the option chain that has the underlying
            foreach (var kvp in algorithm.CurrentSlice.OptionChains)
            {
                if (kvp.Key.Underlying == underlyingSymbol)
                {
                    chain = kvp.Value;
                    return true;
                }
            }

            return false;
        }

		public static decimal GetHoldingQuantity(QCAlgorithmFramework algorithm, Symbol optionOrUnderlyingSymbol, bool pLong, bool pShort)
		{
			decimal pendingHoldingQuantity = 0;

			string targetSymbol = optionOrUnderlyingSymbol.Value;
			if (optionOrUnderlyingSymbol.HasUnderlying)
				targetSymbol = optionOrUnderlyingSymbol.Underlying.Value;

			foreach (var s in algorithm.Securities)
			{
				//only care about derivatives
				if (!s.Key.HasUnderlying || !s.Value.Invested)
					continue;

				var symbol = s.Key.Underlying.Value;

				if (symbol == targetSymbol)
				{
					if (pLong && s.Value.Holdings.IsLong)
					{
						pendingHoldingQuantity += s.Value.Holdings.AbsoluteQuantity;
					}
					if (pShort && s.Value.Holdings.IsShort)
					{
						pendingHoldingQuantity += s.Value.Holdings.AbsoluteQuantity;
					}
				}
			}

			return pendingHoldingQuantity;
		}

		public static decimal GetOpenOrderQuantity(QCAlgorithmFramework algorithm, Symbol optionOrUnderlyingSymbol, bool pLong, bool pShort)
		{
			decimal pendingHoldingQuantity = 0;

			string targetSymbol = optionOrUnderlyingSymbol.Value;
			if (optionOrUnderlyingSymbol.HasUnderlying)
				targetSymbol = optionOrUnderlyingSymbol.Underlying.Value;

			foreach (var o in algorithm.Transactions.GetOpenOrders())
			{
				//only care about derivatives
				if (!o.Symbol.HasUnderlying)
					continue;

				var symbol = o.Symbol.Underlying.Value;

				if (symbol == targetSymbol)
				{
					if (o.Quantity > 0 && pLong)
					{
						pendingHoldingQuantity += o.AbsoluteQuantity;
					}
					if (o.Quantity < 0 && pShort)
					{
						pendingHoldingQuantity += o.AbsoluteQuantity;
					}
				}
			}

			return pendingHoldingQuantity;
		}

		/// <summary>
		/// Get total of quantity of holdings + pending orders
		/// </summary>
		public static decimal GetNetHoldingQuantity(QCAlgorithmFramework algorithm, Symbol optionOrUnderlyingSymbol)
        {
            decimal pendingHoldingQuantity = 0;

            string targetSymbol = optionOrUnderlyingSymbol.Value;
            if (optionOrUnderlyingSymbol.HasUnderlying)
                targetSymbol = optionOrUnderlyingSymbol.Underlying.Value;

			//1. Add up total quantities held and pending for all symbols of underlying
			// (i.e. all the option contracts)
			pendingHoldingQuantity = GetHoldingQuantity(algorithm, optionOrUnderlyingSymbol, true, false);
			pendingHoldingQuantity -= GetHoldingQuantity(algorithm, optionOrUnderlyingSymbol, false, true);
			pendingHoldingQuantity += GetOpenOrderQuantity(algorithm, optionOrUnderlyingSymbol, true, false);
			pendingHoldingQuantity -= GetOpenOrderQuantity(algorithm, optionOrUnderlyingSymbol, false, true);

			return pendingHoldingQuantity;
        }
    }
}
