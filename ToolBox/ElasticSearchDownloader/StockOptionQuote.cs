using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.ToolBox.ElasticSearchDownloader
{
	//BID_ASK quote
	public class OptionQuote
	{
		public OptionQuote() { }

		public string id { get; set; }
		public string symbol { get; set; }
		public string type { get; set; } //TODO: need to have normalized type names
		public int contractId { get; set; }

		public DateTime date { get; set; }
		public DateTime expiry { get; set; }
		public int daysExp { get; set; }

		public double strike { get; set; }
		public string right { get; set; }

		public decimal bid { get; set; }

		public double maxAsk { get; set; }

		public decimal bidSize { get; set; }
		public decimal askSize { get; set; }

		public double lowBid { get; set; }
		public decimal ask { get; set; }
		public long volume { get; set; }

		public decimal spread
		{
			get { return Math.Round(ask - bid, 2); }
		}
		public decimal mid
		{
			get { return Math.Round(bid + (spread / 2), 2); }
		}

		public string baseSymbol { get; set; }
		public string baseType { get; set; }
	}

	public class StockOptionQuote : OptionQuote
	{
		public StockOptionQuote() : base() { }

		public decimal basePrice { get; set; }
		public double dist { get; set; }

		//In-the-money
		public bool itm { get; set; }

		public double intr { get; set; }

		//extrinsic value
		public double extr { get; set; }
	}
}
