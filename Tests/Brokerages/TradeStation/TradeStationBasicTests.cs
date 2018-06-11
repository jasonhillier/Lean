

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using NUnit.Framework;
using QuantConnect.Brokerages.TradeStation;
using QuantConnect.Interfaces;
using QuantConnect.Orders;
using QuantConnect.Securities;

namespace QuantConnect.Tests.Brokerages.TradeStation
{
	[TestFixture]
	public class TradeStationBasicTests
	{
		private TradeStationBrokerage _Broker;
		/// <summary>
		/// Creates the brokerage under test
		/// </summary>
		/// <returns>A connected brokerage instance</returns>
		[Test]
		public void TradeStation_CreateBrokerage()
		{
			var accountID = TradeStationBrokerageFactory.Configuration.AccountID;
			var accessToken = TradeStationBrokerageFactory.Configuration.AccessToken;

			_Broker = new TradeStationBrokerage(new OrderProvider(), new SecurityProvider(), accountID, accessToken);
		}

		[Test]
		public void TradeStation_GetQuote()
		{
			var quotes = _Broker.GetQuotes(new List<string>() { "VXX" });
		}
	}
}