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
 *
*/

using System;
using System.Collections.Generic;
using System.Reflection;
using QuantConnect.Algorithm.Framework;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Data;
using QuantConnect.Orders;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// Regression algorithm for the VolumeWeightedAveragePriceExecutionModel.
    /// This algorithm shows how the execution model works to split up orders and submit them only when
    /// the price is on the favorable side of the intraday VWAP.
    /// </summary>
    public class DynamicAlgorithim : QCAlgorithmFramework
    {
        private decimal _takeProfit = 0;

        public override void Initialize()
        {
            UniverseSettings.Resolution = Resolution.Minute;

            DateTime startDate = DateTime.Parse(GetParameter("start-date"));
            DateTime endDate = DateTime.Parse(GetParameter("end-date"));

            decimal.TryParse(GetParameter("take-profit"), out _takeProfit);
            if (_takeProfit > 0)
                this.Log("take-profit set to: " + _takeProfit);

            SetStartDate(startDate);
            SetEndDate(endDate);
            SetCash(1000000);

            SetUniverseSelection(new ManualUniverseSelectionModel(
                //QuantConnect.Symbol.Create("AIG", SecurityType.Equity, Market.USA),
                //QuantConnect.Symbol.Create("BAC", SecurityType.Equity, Market.USA),
                //QuantConnect.Symbol.Create("IBM", SecurityType.Equity, Market.USA),
                QuantConnect.Symbol.Create(GetParameter("symbol"), SecurityType.Equity, Market.USA)
            ));

            var alphaModel = GetParameter("alpha-model");
            var portfolioModel = GetParameter("portfolio-model");
            var executionModel = GetParameter("execution-model");

            if (String.IsNullOrEmpty(alphaModel))
                throw new Exception("No dynamic alpha-model type specified!");
            if (String.IsNullOrEmpty(portfolioModel))
                throw new Exception("No dynamic portfolio-model type specified!");
            if (String.IsNullOrEmpty(executionModel))
                throw new Exception("No dynamic execution-model type specified!");

            var portfolio = (IPortfolioConstructionModel)Activator.CreateInstance(FindType(portfolioModel));
            var execution = (IExecutionModel)Activator.CreateInstance(FindType(executionModel));

            this.Log("Using portfoliom model: " + portfolio.GetType().Name);
            this.Log("Using execution model: " + execution.GetType().Name);

            SetParametersOnObject("portfolio", portfolio);
            SetParametersOnObject("execution", execution);

            int period = GetParameterAs<int>("period", 14);
            double threshold = GetParameterAs<double>("threshold", 0);
            double step = GetParameterAs<double>("step", 0);
            bool inverted = GetParameterAs<bool>("inverted", false);

            //TODO: need to dynamically call ctor with parameters
            var alpha = (AlphaModel)Activator.CreateInstance(FindType(alphaModel), new TimeSpan(0, 15, 0), period, threshold, step, inverted);

            SetAlpha(alpha);
            SetPortfolioConstruction(portfolio);
            SetExecution(execution);

            InsightsGenerated += (algorithm, data) => Log($"{Time}: INSIGHT>> {string.Join(" | ", data.Insights)}");
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            //TODO: option to only have single position
            Log($"{Time}: ORDER_EVENT: {orderEvent}");
        }

        public override void OnAssignmentOrderEvent(OrderEvent assignmentEvent)
        {
            //TODO: close assigned positions
            base.OnAssignmentOrderEvent(assignmentEvent);
        }

        public override void OnData(Slice slice)
        {
            if (_takeProfit > 0)
            {
                if (this.Portfolio.TotalUnrealizedProfit > this.Portfolio.TotalAbsoluteHoldingsCost * _takeProfit)
                {
                    this.Liquidate();
                }
            }
        }

        private T GetParameterAs<T>(string parameterName, T defaultValue)
        {
            var paramValue = GetParameter(parameterName);
            if (string.IsNullOrEmpty(paramValue))
                return defaultValue;

            if (typeof(T) == typeof(bool))
            {
                return (T)(object)bool.Parse(paramValue);
            }
            if (typeof(T) == typeof(int))
            {
                return (T)(object)int.Parse(paramValue);
            }
            if (typeof(T) == typeof(Double))
            {
                return (T)(object)double.Parse(paramValue);
            }
            if (typeof(T) == typeof(Decimal))
            {
                return (T)(object)decimal.Parse(paramValue);
            }
            if (typeof(T) == typeof(string))
            {
                return (T)(object)paramValue;
            }

            throw new Exception("cast type not supported!");
        }

		private void SetParametersOnObject(string domain, object o)
        {
            foreach(PropertyInfo prop in o.GetType().GetProperties())
            {
                if (prop.CanWrite)
                {
                    var paramVal = GetParameter(domain + "-" + prop.Name.ToLower());
                    if (string.IsNullOrEmpty(paramVal))
                        paramVal = GetParameter(domain + "-" + prop.Name);

                    if (!string.IsNullOrEmpty(paramVal))
                    {
                        prop.SetValue(o, paramVal);
                    }
                }
            }
        }

        private Type FindType(string Name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type t in a.GetTypes())
                {
                    if (t.Name == Name)
                        return t;
                }
            }

            return null;
        }
    }
}
