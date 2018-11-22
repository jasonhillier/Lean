using QuantConnect.Data.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.Framework.Alphas
{
	public abstract class OptionAlphaModel : AlphaModel
	{
		public bool TryGetOptionChain(QCAlgorithmFramework algorithm, Symbol underlyingSymbol, out OptionChain chain)
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
	}
}
