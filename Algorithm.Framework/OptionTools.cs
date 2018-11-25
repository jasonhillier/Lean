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

        public static decimal GetNetHoldingQuantity(QCAlgorithmFramework algorithm, Symbol optionOrUnderlyingSymbol)
        {
            decimal pendingHoldingQuantity = 0;

            string targetSymbol = optionOrUnderlyingSymbol.Value;
            if (optionOrUnderlyingSymbol.HasUnderlying)
                targetSymbol = optionOrUnderlyingSymbol.Underlying.Value;

            //1. Add up total quantities held and pending for all symbols of underlying
            // (i.e. all the option contracts)
            foreach (var s in algorithm.Securities)
            {
                //only care about derivatives
                if (!s.Key.HasUnderlying)
                    continue;

                var symbol = s.Key.Underlying.Value;

                if (symbol == targetSymbol)
                {
                    pendingHoldingQuantity += s.Value.Holdings.Quantity;
                }
            }

            foreach (var o in algorithm.Transactions.GetOpenOrders())
            {
                //only care about derivatives
                if (!o.Symbol.HasUnderlying)
                    continue;

                var symbol = o.Symbol.Underlying.Value;

                if (symbol == targetSymbol)
                {
                    pendingHoldingQuantity += o.Quantity;
                }
            }

            return pendingHoldingQuantity;
        }
    }
}
