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
using QuantConnect.Securities;

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
        private TimeSpan consolidatedResolution = new TimeSpan(0, 15, 0);

        public override void Initialize()
        {
            UniverseSettings.Resolution = Resolution.Minute;
            UniverseSettings.FillForward = false;
            //this.Settings.DataSubscriptionLimit = 100000;
            this.SetSecurityInitializer((sec) =>
            {
                sec.SetDataNormalizationMode(DataNormalizationMode.Raw);
            });

            DateTime startDate = DateTime.Parse(GetParameter("start-date"));
            DateTime endDate = DateTime.Parse(GetParameter("end-date"));

            decimal.TryParse(GetParameter("take-profit"), out _takeProfit);
            if (_takeProfit > 0)
                this.Log("take-profit set to: " + _takeProfit);

            SetStartDate(startDate);
            SetEndDate(endDate);
            SetCash(100000);

			var security = QuantConnect.Symbol.Create(GetParameter("symbol"), SecurityType.Equity, Market.USA);
            var option = QuantConnect.Symbol.Create(GetParameter("symbol"), SecurityType.Option, Market.USA);

            SetUniverseSelection(new DynamicOptionUniverseSelectionModel((DateTime utcTime) =>
            {
                return new List<Symbol>(){
                    option
                };
            }));

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

            var alpha = CreateAlphaInstance(FindType(alphaModel));// (AlphaModel)Activator.CreateInstance(FindType(alphaModel), new TimeSpan(0, 15, 0), period, threshold, step, inverted);

            SetAlpha(alpha);
            SetPortfolioConstruction(portfolio);
            SetExecution(execution);

            Log("====== Starting... ======");

            InsightsGenerated += (algorithm, data) => Log($"{Time}: INSIGHT>> {string.Join(" | ", data.Insights)}");

            //this.SetBenchmark(security); //TODO: this breaks everything
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            //TODO: option to only have single position
            Log($"{Time}: ORDER_EVENT: {orderEvent}");
        }

        public override void OnAssignmentOrderEvent(OrderEvent assignmentEvent)
        {
			if (assignmentEvent.IsAssignment && assignmentEvent.Status == OrderStatus.Filled)
			{
				Log("CLOSING ASSIGNED POSITION at MARKET!");
				//close assigned positions
				if (assignmentEvent.Direction == OrderDirection.Buy)
					this.MarketOrder(assignmentEvent.Symbol.Underlying, -assignmentEvent.FillQuantity * 100);
				else if (assignmentEvent.Direction == OrderDirection.Sell)
					this.MarketOrder(assignmentEvent.Symbol.Underlying, assignmentEvent.FillQuantity * 100);
			}

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

        class DynamicOptionUniverseSelectionModel : OptionUniverseSelectionModel
        {
            public DynamicOptionUniverseSelectionModel(Func<DateTime, IEnumerable<Symbol>> optionChainSymbolSelector)
                : base(TimeSpan.FromDays(1), optionChainSymbolSelector)
            {
            }

            /// <summary>
            /// Defines the option chain universe filter
            /// </summary>
            protected override OptionFilterUniverse Filter(OptionFilterUniverse filter)
            {
                return filter
                    .Strikes(-7, +7)
                    .Expiration(TimeSpan.Zero, TimeSpan.FromDays(45));
                    //.WeeklysOnly()
                    //.PutsOnly()
                    //.OnlyApplyFilterAtMarketOpen();
            }
        }

        private AlphaModel CreateAlphaInstance(Type alphaModelType)
        {
            Log("Initializing Alpha with parameters:");
            //TODO: find best fitting CTOR
            var ctor = alphaModelType.GetConstructors()[0]; //pick first one for now
            List<object> parameterValues = new List<object>();
            foreach(var p in ctor.GetParameters())
            {
                var value = GetParameterGeneric(p.Name, p.ParameterType, p.DefaultValue);

                Log(String.Format("{0}\t{1}= {2}", p.Name, p.ParameterType.Name, value));

                parameterValues.Add(value);
            }

            return (AlphaModel)ctor.Invoke(parameterValues.ToArray());
        }

        private T GetParameterAs<T>(string parameterName, T defaultValue)
        {
            return (T)GetParameterGeneric(parameterName, typeof(T), defaultValue);
        }

        private object GetParameterGeneric(string parameterName, Type parameterCastType, object defaultValue)
        {
            if (parameterName == "resolution")
            {
                if (parameterCastType == typeof(Resolution))
                    return UniverseSettings.Resolution;
                else
                    return consolidatedResolution;
            }
            var paramValue = GetParameter(parameterName);
            if (string.IsNullOrEmpty(paramValue))
                return defaultValue;

            if (parameterCastType == typeof(bool))
            {
                return (object)bool.Parse(paramValue);
            }
            if (parameterCastType == typeof(int))
            {
                return (object)int.Parse(paramValue);
            }
            if (parameterCastType == typeof(Double))
            {
                return (object)double.Parse(paramValue);
            }
            if (parameterCastType == typeof(Decimal))
            {
                return (object)decimal.Parse(paramValue);
            }
            if (parameterCastType == typeof(string))
            {
                return (object)paramValue;
            }
            if (parameterCastType.IsEnum)
            {
                return (object)Enum.Parse(parameterCastType, paramValue);
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
