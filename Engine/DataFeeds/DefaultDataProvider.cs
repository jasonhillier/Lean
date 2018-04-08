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
using System.Collections.Generic;
using System.IO;
using QuantConnect.Interfaces;
using QuantConnect.Logging;

namespace QuantConnect.Lean.Engine.DataFeeds
{
    /// <summary>
    /// Default file provider functionality that does not attempt to retrieve any data
    /// </summary>
    public class DefaultDataProvider : IDataProvider, IDisposable
    {
		private Dictionary<string, bool> _FileExistsStatus = new Dictionary<string, bool>();
        /// <summary>
        /// Retrieves data from disc to be used in an algorithm
        /// </summary>
        /// <param name="key">A string representing where the data is stored</param>
        /// <returns>A <see cref="Stream"/> of the data requested</returns>
        public Stream Fetch(string key)
        {
			if (!_FileExistsStatus.ContainsKey(key))
			{
				_FileExistsStatus[key] = File.Exists(key);
			}

            if (!_FileExistsStatus[key])
            {
				if (key.Contains("option") && (key.Contains("openinterest") || key.Contains("trade")))
				{
					//these files are not required, don't want to spam errors
					Log.Debug("DefaultDataProvider.Fetch(): The specified file was not found: " + key);
				}
				else
				{
					Log.Error("DefaultDataProvider.Fetch(): The specified file was not found: {0}", key);
				}
                return null;
            }

            return new FileStream(key, FileMode.Open, FileAccess.Read);
        }

        /// <summary>
        /// The stream created by this type is passed up the stack to the IStreamReader
        /// The stream is closed when the StreamReader that wraps this stream is disposed</summary>
        public void Dispose()
        {
            //
        }
    }
}
