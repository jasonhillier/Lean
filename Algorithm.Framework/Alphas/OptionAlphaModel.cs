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
            return OptionTools.TryGetOptionChain(algorithm, underlyingSymbol, out chain);
        }
	}
}
