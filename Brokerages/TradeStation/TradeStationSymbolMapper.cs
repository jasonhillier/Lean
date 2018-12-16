using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.TradeStation
{
	class TradeStationSymbolMapper : ISymbolMapper
	{
		public Dictionary<Symbol, string> OptionNameResolver = new Dictionary<Symbol, string>();
		private readonly TradeStationBrokerage _broker;

		public TradeStationSymbolMapper(TradeStationBrokerage broker)
		{
			_broker = broker;
		}

		public string GetBrokerageSymbol(Symbol pSymbol)
		{
			if (pSymbol.SecurityType == SecurityType.Option)
			{
				//look in cache
				if (OptionNameResolver.ContainsKey(pSymbol))
					return OptionNameResolver[pSymbol];

				//if it isn't in the cache, try lookup option symbols
				_broker.LookupSymbols(pSymbol.Underlying.Value);

				if (OptionNameResolver.ContainsKey(pSymbol))
					return OptionNameResolver[pSymbol];

				//TODO: build converter
				throw new Exception("TradeStation.ConvertToTradestationSymbol: Could not resolve option symbol!");
			}
			else
			{
				return pSymbol.Value;
			}
		}

		public string GetUnderlyingFromSymbolName(string pSymbolName)
		{
			return pSymbolName.Split(' ')[0];
		}

		public Symbol GetLeanSymbol(string pSymbolName, AssetType3 pAssetType, string pMarket = Market.USA, DateTime expirationDate = default(DateTime), decimal strike = 0, OptionRight optionRight = OptionRight.Call)
		{
			SecurityType secType;
			switch(pAssetType)
			{
				case AssetType3.Fu:
					secType = SecurityType.Future;
					break;
				case AssetType3.Op:
					secType = SecurityType.Option;
					break;
				case AssetType3.EQ:
				default:
					secType = SecurityType.Equity;
					break;
			}

			return GetLeanSymbol(pSymbolName, secType, pMarket, expirationDate, strike, optionRight);
		}

		public Symbol GetLeanSymbol(string brokerageSymbol, SecurityType securityType, string market, DateTime expirationDate = default(DateTime), decimal strike = 0, OptionRight optionRight = OptionRight.Call)
		{
			switch (securityType)
			{
				case SecurityType.Future:
					return Symbol.CreateFuture(brokerageSymbol, market, expirationDate);

				case SecurityType.Option:
					//try to resolve
					var symbol = this.OptionNameResolver.FirstOrDefault((x) => x.Value == brokerageSymbol).Key;
					if (symbol != null)
						return symbol;

					//lookup the options for this base symbol
					string assumeBaseSymbol = GetUnderlyingFromSymbolName(brokerageSymbol);
					_broker.LookupSymbols(assumeBaseSymbol, SecurityType.Option);
					//try again
					symbol = this.OptionNameResolver.FirstOrDefault((x) => x.Value == brokerageSymbol).Key;
					if (symbol != null)
						return symbol;

					throw new Exception("Failed to resolve symbol " + brokerageSymbol + " with broker!");
				case SecurityType.Equity:
				default:
					return Symbol.Create(brokerageSymbol, SecurityType.Equity, market);
			}
		}
	}
}
