using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Securities.Option;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.Text;
using System.Net;

namespace QuantConnect.Algorithm.CSharp
{
	public class OptionStatistics
	{
		protected QCAlgorithm _Algo;
		protected Option _Option;
		protected Symbol _OptionSymbol;
		protected string _Storage;
		protected List<OptionCalculation> _CurrentCalculations;
		protected string _PackagedData = null;
		private Int64 _TotalStoredBytes = 0;

		public OptionStatistics(QCAlgorithm Algo, Option Option, string Storage = "esoptions")
		{
			_Algo = Algo;
			_Option = Option;
			_OptionSymbol = Option.Symbol;
			_Storage = Storage;

			Console.WriteLine("SECURITY ID = " + _OptionSymbol.Value);

			// set our strike/expiry filter for this option chain
			_Option.SetFilter(u => u.Strikes(-20, +20)
								   .Expiration(TimeSpan.Zero, TimeSpan.FromDays(31)));
		}

		public OptionChain GetOptionChain(Slice slice)
		{
			OptionChain chain = null;
			slice.OptionChains.TryGetValue(_OptionSymbol, out chain);
			if (chain == null)
			{
				Console.WriteLine("<{0}> No option chain here!", slice.Time.ToString());
			}

			return chain;
		}

		public bool ComputeChain(Slice slice, DateTime QuoteTime)
		{
			var chain = GetOptionChain(slice);
			if (chain == null)
				return false;

			_CurrentCalculations = new List<OptionCalculation>();

			//TODO: divide into expirations and types, then assign rank/depth ITM/OTM
			return chain.All((contract) =>
			{
				_CurrentCalculations.Add(new OptionCalculation(contract, QuoteTime));
				return true;
			});
		}

		/// <summary>
		/// Serialize computed option data into a data package (string).
		/// </summary>
		/// <returns>Size of data package</returns>
		public virtual Int64 Package()
		{
			StringBuilder builder = new StringBuilder();

			_CurrentCalculations.ForEach((c) =>
			{
				//elasticsearch document header
				builder.AppendLine(
					JsonConvert.SerializeObject(
						new
						{
							index = new
							{
								_index = _Storage,
								_type = "doc",
								_id = c.id
							}
						})
					);

				//elasticsearch document body
				builder.AppendLine(
					JsonConvert.SerializeObject(c)
						);
			});

			if (_PackagedData == null)
				_PackagedData = "";

			_PackagedData += builder.ToString();
			//Console.WriteLine(_PackagedData);

			return _PackagedData.Length;
		}

		/// <summary>
		/// Clear current serialized data package.
		/// </summary>
		public void Clear()
		{
			_PackagedData = null;
		}

		/// <summary>
		/// Store current serialized data package to persistence medium.
		/// </summary>
		/// <returns>True if succeeded</returns>
		public bool Store()
		{
			if (_PackagedData == null)
			{
				if (_CurrentCalculations != null)
					Package();
				else
					return false;
			}

			//store data
			if (!_Commit())
			{
				_Algo.Error("Error committing data package!");

				//for now, clean the data buffers
				Clear();
				return false;
			}

			_TotalStoredBytes += _PackagedData.Length;

			_Algo.Log("Cumulative committed bytes: " + _TotalStoredBytes);

			//clear out old data for next storage operation
			Clear();

			return true;
		}

		protected virtual bool _Commit()
		{
			if (_PackagedData == null)
				return false;

			_Algo.Log("Storing " + _PackagedData.Length + " bytes...");

			String username = "data";
			String password = "data*123";
			String encoded = System.Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(username + ":" + password));

			HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://54.169.251.220/_bulk");
			request.Headers.Add("Authorization", "Basic " + encoded);
			request.Method = "PUT";
			request.AutomaticDecompression = DecompressionMethods.GZip;
			request.ContentType = "application/json";
			var writer = request.GetRequestStream();
			byte[] data = System.Text.Encoding.UTF8.GetBytes(_PackagedData);
			writer.Write(data, 0, data.Length);

			HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			response.Close();

			return (response.StatusCode == HttpStatusCode.OK);
		}

		public class OptionCalculation
		{
			private OptionContract _Contract;
			private DateTime _QuoteTime;

			public OptionCalculation(OptionContract Contract, DateTime QuoteTime)
			{
				_Contract = Contract;
				_QuoteTime = QuoteTime;
			}

			public string id
			{
				get
				{
					return this.symbol + " " + _QuoteTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
 				}
			}

			public string symbol
			{
				get { return _Contract.Symbol.Value; }
			}

			public DateTime date
			{
				get { return _QuoteTime; }
			}

			public DateTime expiry
			{
				get { return _Contract.Expiry; }
			}

			public decimal strike
			{
				get { return _Contract.Strike; }
			}

			public string right
			{
				get { return _Contract.Right == OptionRight.Call ? "C" : "P"; }
			}

			public decimal bid
			{
				get { return _Contract.BidPrice; }
			}

			public decimal ask
			{
				get { return _Contract.AskPrice; }
			}

			public long bidSize
			{
				get { return _Contract.BidSize; }
			}

			public long askSize
			{
				get { return _Contract.AskSize; }
			}

			public long size
			{
				get { return this.bidSize + this.askSize; }
			}

			public string baseSymbol
			{
				get { return _Contract.UnderlyingSymbol.Value; }
			}

			public decimal basePrice
			{
				get { return Math.Round(_Contract.UnderlyingLastPrice, 2); }
			}

			public decimal spread
			{
				get { return Math.Round(_Contract.AskPrice - _Contract.BidPrice, 2); }
			}

			public decimal mid
			{
				get { return Math.Round(_Contract.BidPrice + (this.spread / 2), 2); }
			}

			public decimal dist
			{
				get	{ return Math.Abs(this.strike - this.basePrice); }
			}

			//In-the-money
			public bool itm
			{
				get
				{
					if (_Contract.Right == OptionRight.Call &&
						this.strike < this.basePrice)
					{
						return true;
					}
					else if (_Contract.Right == OptionRight.Put &&
						this.strike > this.basePrice)
					{
						return true;
					}

					return false;
				}
			}

			//extrinsic value
			public decimal extr
			{
				get
				{
					//otm
					if (!this.itm)
						return this.mid > 0 ? this.mid : 0;

					//itm
					decimal result = this.mid - Math.Abs(this.strike - this.basePrice);

					return result > 0 ? result : 0;
				}
			}
		}
	}
}
