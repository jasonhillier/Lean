using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Packets;
using QuantConnect.Logging;
using Newtonsoft.Json;

namespace QuantConnect.Queues
{
	public class AmazonSQSJobQueue : JobQueue
	{
		private AmazonSQSClient _sqsClient;
		private string _queueUrl;

		public override void Initialize(IApi api)
		{
			base.Initialize(api);

			_sqsClient = new AmazonSQSClient(Config.Get("aws-id"), Config.Get("aws-key"), Amazon.RegionEndpoint.USWest2);

			var queues = _sqsClient.ListQueuesAsync(Config.Get("aws-queue", "backtest.fifo")).Result;
			if (queues.QueueUrls.Count <= 0)
				return;

			_queueUrl = queues.QueueUrls[0];
		}

		public override AlgorithmNodePacket NextJob(out string location)
		{
			var job = base.NextJob(out location);

			//just wait around for params

			Message message = null;
			while (message == null)
			{
				Log.Trace("Polling queue: {0}", _queueUrl);

				var recvMessageRequest = new ReceiveMessageRequest(_queueUrl);
				recvMessageRequest.WaitTimeSeconds = 20;
				var q = _sqsClient.ReceiveMessageAsync(recvMessageRequest).Result;
				message = q.Messages.FirstOrDefault();
			}

			Log.Trace("Received a message");

			var parameters = JsonConvert.DeserializeObject<Dictionary<string,string>>(message.Body);

			_sqsClient.DeleteMessageAsync(new DeleteMessageRequest(_queueUrl, message.ReceiptHandle)).Wait();

			job.Parameters = parameters;
			return job;
		}

		/// <summary>
		/// Desktop/Local acknowledge the task processed. Nothing to do.
		/// </summary>
		/// <param name="job"></param>
		public override void AcknowledgeJob(AlgorithmNodePacket job)
		{
			// Make the console window pause so we can read log output before exiting and killing the application completely
			Console.WriteLine("Engine.Main(): Analysis Complete");
		}
	}
}
