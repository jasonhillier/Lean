/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.IO;
using System.Net;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Configuration;

namespace QuantConnect.Algorithm.Framework.Execution
{
    /// <summary>
    /// Provides an implementation of <see cref="IExecutionModel"/> that does nothing
    /// </summary>
    public class NotifyExecutionModel : ExecutionModel
    {
        public override void Execute(QCAlgorithmFramework algorithm, IPortfolioTarget[] targets)
        {
            HttpWebRequest request;
            request = (HttpWebRequest)WebRequest.Create(Config.Get("slack-hook"));
            request.Method = "POST";
            request.ContentType = "application/json";
            string payload = "{\"text\": \"has " + targets.Length + " targets.\"}";
            Stream stream = request.GetRequestStream();
            stream.Write(payload.GetBytes(), 0, payload.Length);
            stream.Close();

            algorithm.Log("NOTIFY_SLACK >> " + payload);

            //Get response as a stream:
            try
            {
                var response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    algorithm.Log("DEBUG ++++>>" + targets.Length);
                }
            }
            catch(Exception ex)
            {
                algorithm.Error(ex);
            }
        }
    }
}
