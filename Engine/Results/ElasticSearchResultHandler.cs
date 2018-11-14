using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net;
using QuantConnect.Configuration;
using QuantConnect.Orders;
using QuantConnect.Securities;
using System.Dynamic;
using System.ComponentModel;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Transfer;
using System.IO;

namespace QuantConnect.Lean.Engine.Results
{
	/// <summary>
	/// ElasticSearchResultHandler result handler passes messages back from the Lean to the User.
	/// </summary>
	public class ElasticSearchResultHandler : BacktestingResultHandler, IResultHandler
	{
		private string ES_INDEX;
		private AmazonS3Client S3_CLIENT;

		/// <summary>
		/// Default initializer for
		/// </summary>
		public ElasticSearchResultHandler() :
			base()
		{
			ES_INDEX = Config.Get("es-index", "backtests");
			S3_CLIENT = new AmazonS3Client(Config.Get("aws-id"), Config.Get("aws-key"), Amazon.RegionEndpoint.USWest2);
		}

		public override void SaveResults(string name, Result pResult)
		{
			if (pResult.Statistics.Count > 0)
			{
				//backtests
				var backTestResult = new BackTestResult(_job.GetAlgorithmName(), _job.Parameters, pResult.Statistics, this.Algorithm.RuntimeStatistics);
				try
				{
					this.Commit(new List<BackTestResult>() { backTestResult }, ES_INDEX);

					Console.WriteLine("[ElasticSearchResultHandler] Storing results for " + backTestResult.id + "...");

					Console.WriteLine("[ElasticSearchResultHandler] + Storing result orders...");
					//backtests-orders
					var backTestOrders = new List<dynamic>();
					foreach (var order in pResult.Orders)
					{
						var metaOrder = order.Value.ToDynamic();
						metaOrder.backtestId = backTestResult.id;
						metaOrder.id = metaOrder.backtestId + "_" + metaOrder.Id;
						metaOrder.date = metaOrder.Time;

						backTestOrders.Add(metaOrder);
					}
					this.Commit(backTestOrders, ES_INDEX + "-orders");

					/* -- don't save the charts, instead, we upload the backtest report json
					Console.WriteLine("[ElasticSearchResultHandler] + Storing result charts...");
					//backtests-charts
					var chartValues = new List<BackTestChartPoint>();
					foreach (var chart in pResult.Charts)
					{
						foreach (var series in chart.Value.Series)
						{
							foreach (var point in series.Value.Values)
							{
								var chartValue = new BackTestChartPoint(
									backTestResult.id,
									chart.Key,
									series.Value,
									point);

								chartValues.Add(chartValue);
							}
						}
					}
					_Commit(chartValues, ES_INDEX + "-charts");
					*/
				}
				catch (Exception ex)
				{
					this.ErrorMessage("Error uploading to ElasticSearch: " + ex.Message);
				}

				base.SaveResults(name, pResult);

				Console.WriteLine("Uploading results to S3: reports/" + backTestResult.id);

				try
				{
					//upload JSON file
					var filePath = Path.Combine(Directory.GetCurrentDirectory(), name);
					var tx = new TransferUtility(S3_CLIENT);
					tx.UploadAsync(filePath, Config.Get("aws-bucket", "lean-option-data"), "reports/" + backTestResult.id).Wait();
					Console.WriteLine("Upload complete.");
				} catch (Exception ex)
				{
					this.ErrorMessage("Error uploading to S3: " + ex.Message);
				}
			}
		}

		private bool Commit(IEnumerable<dynamic> Results, string ESIndex)
		{
			if (Results == null || Results.Count() == 0)
				return false;
			string _PackagedData = Package(Results, ESIndex);

			Console.WriteLine("Storing " + _PackagedData.Length + " bytes...");

			String username = Config.Get("es-user");
			String password = Config.Get("es-pwd");
			String encoded = System.Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(username + ":" + password));

			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Config.Get("es-server") + "/_bulk");
			request.Headers.Add("Authorization", "Basic " + encoded);
			request.Method = "PUT";
			request.AutomaticDecompression = DecompressionMethods.GZip;
			request.ContentType = "application/json";
			var writer = request.GetRequestStream();
			byte[] data = System.Text.Encoding.UTF8.GetBytes(_PackagedData);
			writer.Write(data, 0, data.Length);

			HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			response.Close();

			if (response.StatusCode != HttpStatusCode.OK)
			{
				Console.WriteLine("[ElasticSearch] Bad response: " + response.StatusCode.ToString());            
			}

			return (response.StatusCode == HttpStatusCode.OK);
		}

		/// <summary>
		/// Serialize computed option data into a data package (string).
		/// </summary>
		/// <returns>Size of data package</returns>
		private string Package(IEnumerable<dynamic> pResults, string pESIndex)
		{ 
			StringBuilder builder = new StringBuilder();

			//elasticsearch document header
			foreach (var entity in pResults)
			{
				builder.AppendLine(
					JsonConvert.SerializeObject(
						new
						{
							index = new
							{
								_index = pESIndex,
								_type = "doc",
								_id = entity.id
							}
						})
					);

				//elasticsearch document body
				builder.AppendLine(
					JsonConvert.SerializeObject(entity)
						);
			}

			return builder.ToString();
		}

		class BackTestChartPoint
		{
			public BackTestChartPoint(string pBacktestId, string pChartName, Series pSeriesInfo, ChartPoint pPoint)
			{
				backtestId = pBacktestId;
				chart = pChartName;
				series = pSeriesInfo.Name;
				unit = pSeriesInfo.Unit;
				seriesType = pSeriesInfo.SeriesType.ToString();
				date = Time.UnixTimeStampToDateTime(pPoint.x);
				y = pPoint.y;
				id = pBacktestId + "_" + pChartName + "_" + pSeriesInfo.Name + "_" + pPoint.ToString();
			}

			public DateTime date { get; set; }
			public decimal y { get; set; }
			public string backtestId { get; set; }
			public string chart { get; set; }
			public string series { get; set; }
			public string seriesType { get; set; }
			public string unit { get; set; }
			public string id { get; set; }
		}

		class BackTestResult
		{
			public BackTestResult(string Name, IDictionary<string, string> Params, IDictionary<string,string> Stats, IDictionary<string, string> RuntimeStats)
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
				runtimeStats = new Dictionary<string, double>();
				foreach (var kvp in RuntimeStats)
                {
					runtimeStats[kvp.Key.Replace(" ", "_")] = double.Parse(kvp.Value.Replace("%", "").Replace("$", ""));
                }
			}

			public string name { get; set; }
			public DateTime date { get; set; }
			public string id { get; set; }

			public IDictionary<string, string> parameters { get; set; }
			public Dictionary<string, double> stats { get; set; }
			public Dictionary<string, double> runtimeStats { get; set; }
		}
	}

	public static class DynamicExtensions
	{
		public static dynamic ToDynamic(this object value)
		{
			IDictionary<string, object> expando = new ExpandoObject();

			foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(value.GetType()))
				expando.Add(property.Name, property.GetValue(value));

			return expando as ExpandoObject;
		}
	}
}
