using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net;

namespace QuantConnect.Lean.Engine.Results
{
	/// <summary>
	/// ElasticSearchResultHandler result handler passes messages back from the Lean to the User.
	/// </summary>
	public class ElasticSearchResultHandler : BacktestingResultHandler, IResultHandler
	{
		private const string ES_INDEX = "backtests";

		public ElasticSearchResultHandler() :
			base()
		{
		}

		public override void SaveResults(string name, Result pResult)
		{
			if (pResult.Statistics.Count > 0)
			{
				var backTestResult = new BackTestResult(_job.GetAlgorithmName(), _job.Parameters, pResult.Statistics);
				_Commit(backTestResult);
			}
		}

		private bool _Commit(BackTestResult Result)
		{
			string _PackagedData = Package(Result);

			Console.WriteLine("Storing " + _PackagedData.Length + " bytes...");

			String username = "data";
			String password = "data*123";
			String encoded = System.Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(username + ":" + password));

			HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://es.hillier.us/_bulk");
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

		/// <summary>
		/// Serialize computed option data into a data package (string).
		/// </summary>
		/// <returns>Size of data package</returns>
		private string Package(BackTestResult pResult)
		{
			StringBuilder builder = new StringBuilder();

			//elasticsearch document header
			builder.AppendLine(
				JsonConvert.SerializeObject(
					new
					{
						index = new
						{
							_index = ES_INDEX,
							_type = "doc",
							_id = pResult.id
						}
					})
				);

			//elasticsearch document body
			builder.AppendLine(
				JsonConvert.SerializeObject(pResult)
					);

			return builder.ToString();
		}

		class BackTestResult
		{
			public BackTestResult(string Name, IDictionary<string, string> Params, IDictionary<string,string> Stats)
			{
				name = Name;
				date = DateTime.Now;
				id = name + date.ToString("o");
				parameters = Params;
				stats = new Dictionary<string, double>();
				foreach(var kvp in Stats)
				{
					stats[kvp.Key.Replace(" ", "_")] = double.Parse(kvp.Value.Replace("%", "").Replace("$", ""));
				}
			}

			public string name { get; set; }
			public DateTime date { get; set; }
			public string id { get; set; }

			public IDictionary<string, string> parameters { get; set; }
			public Dictionary<string, double> stats { get; set; }
		}
	}
}
