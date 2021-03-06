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
using Newtonsoft.Json;
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
    public class DynamicAlgorithm : QCAlgorithmFramework
    {
        private decimal _takeProfit = 0;
		private decimal _stopLoss = 0;
        private TimeSpan consolidatedResolution = new TimeSpan(0, 15, 0);
		private Symbol _primarySymbol;
		private Series _longHoldingsCount;
		private Series _shortHoldingsCount;
		private Series _longOpenOrdersCount;
		private Series _shortOpenOrdersCount;

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

			_takeProfit = GetParameterAs("take-profit", 0m);
			_stopLoss = Math.Abs(GetParameterAs("stop-loss", 0m));

            this.Log("take-profit set to: " + _takeProfit);
			this.Log("stop-loss set to: " + _stopLoss);

			SetStartDate(startDate);
            SetEndDate(endDate);
            SetCash(100000);

			_primarySymbol = QuantConnect.Symbol.Create(GetParameter("symbol"), SecurityType.Equity, Market.USA);
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

            this.Log("Using portfolio model: " + portfolio.GetType().Name);
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

			//setup chart to plot net position
			var chart = new Chart("Net Position");
			_longHoldingsCount = new Series("Holdings - Long " + this._primarySymbol.Value, SeriesType.StackedArea, '#');
			_shortHoldingsCount = new Series("Holdings - Short " + this._primarySymbol.Value, SeriesType.StackedArea, '#');
			_longOpenOrdersCount = new Series("Open Orders - Long " + this._primarySymbol.Value, SeriesType.StackedArea, '#');
			_shortOpenOrdersCount = new Series("Open Orders - Short " + this._primarySymbol.Value, SeriesType.StackedArea, '#');
			chart.AddSeries(_longHoldingsCount);
			chart.AddSeries(_shortHoldingsCount);
			chart.AddSeries(_longOpenOrdersCount);
			chart.AddSeries(_shortOpenOrdersCount);
			this.AddChart(chart);
		}

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            //TODO: option to only have single position
            Log($"{Time}: ORDER_EVENT: {orderEvent}");
            //we stop the algorithim if it starts trying to post invalid orders
            if (orderEvent.Status == OrderStatus.Invalid && orderEvent.Message.ToLower().Contains("insufficient"))
			{
				throw new Exception("ABORT ORDER FAILURE! " + orderEvent.Message);
            }
        }

        public override void OnAssignmentOrderEvent(OrderEvent assignmentEvent)
        {
			if (assignmentEvent.IsAssignment && assignmentEvent.Status == OrderStatus.Filled)
			{
				Log("CLOSING ASSIGNED POSITION at MARKET!");
				//close assigned positions
				if (assignmentEvent.Direction == OrderDirection.Buy)
					this.MarketOrder(assignmentEvent.Symbol.Underlying, -assignmentEvent.FillQuantity * 100, true, "assigned-close");
				else if (assignmentEvent.Direction == OrderDirection.Sell)
					this.MarketOrder(assignmentEvent.Symbol.Underlying, assignmentEvent.FillQuantity * 100, true, "assigned-close");
			}

			base.OnAssignmentOrderEvent(assignmentEvent);
        }

        public override void OnData(Slice slice)
        {
			foreach(var p in this.Portfolio.Values)
			{
				if (p.Quantity != 0)
				{
					if (_takeProfit > 0 && p.UnrealizedProfitPercent > _takeProfit)
					{
                        Log("TAKE PROFIT");
						this.MarketOrder(p.Symbol, -p.Quantity, true, "take-profit");
					}
					else if (_stopLoss > 0 && p.UnrealizedProfitPercent < -_stopLoss)
					{
                        Log("STOP LOSS");
						this.MarketOrder(p.Symbol, -p.Quantity, true, "stop-loss");
					}
				}
			}

			this.PlotNetPosition(slice);
        }

		public void PlotNetPosition(Slice slice)
		{
			_longHoldingsCount.AddPoint(slice.Time, OptionTools.GetHoldingQuantity(this, _primarySymbol, true, false));
			_shortHoldingsCount.AddPoint(slice.Time, -OptionTools.GetHoldingQuantity(this, _primarySymbol, false, true));
			_longOpenOrdersCount.AddPoint(slice.Time, OptionTools.GetOpenOrderQuantity(this, _primarySymbol, true, false));
			_shortOpenOrdersCount.AddPoint(slice.Time, -OptionTools.GetOpenOrderQuantity(this, _primarySymbol, false, true));
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
                    .Strikes(-20, +20)
                    .Expiration(TimeSpan.Zero, TimeSpan.FromDays(60));
                    //.WeeklysOnly()
                    //.PutsOnly()
                    //.OnlyApplyFilterAtMarketOpen();
            }
        }

        private AlphaModel CreateAlphaInstance(Type alphaModelType)
        {
            Log("Initializing " + alphaModelType.Name + " with parameters:");
            //TODO: find best fitting CTOR
            var ctor = alphaModelType.GetConstructors()[0]; //pick first one for now
            List<object> parameterValues = new List<object>();
            foreach(var p in ctor.GetParameters())
            {
                var value = GetParameterGeneric(p.Name, p.ParameterType, p.DefaultValue);

                Log(String.Format("- {0}\t{1}= {2}", p.Name, p.ParameterType.Name, value));

                parameterValues.Add(value);
            }

            return (AlphaModel)ctor.Invoke(parameterValues.ToArray());
        }

        private T GetParameterAs<T>(string parameterName, T defaultValue)
        {
            return (T)GetParameterGeneric(parameterName, typeof(T), defaultValue);
        }

		private object ConvertString(string value, Type parameterCastType, object defaultValue)
		{
			if (string.IsNullOrEmpty(value))
				return defaultValue;

			if (parameterCastType == typeof(bool))
			{
				return (object)bool.Parse(value);
			}
			if (parameterCastType == typeof(int))
			{
				return (object)int.Parse(value);
			}
			if (parameterCastType == typeof(Double))
			{
				return (object)double.Parse(value);
			}
			if (parameterCastType == typeof(Decimal))
			{
				return (object)decimal.Parse(value);
			}
			if (parameterCastType == typeof(string))
			{
				return (object)value;
			}
			if (parameterCastType.IsEnum)
			{
				return (object)Enum.Parse(parameterCastType, value);
			}
			if (parameterCastType == typeof(double[]))
			{
				return JsonConvert.DeserializeObject<double[]>(value);
			}
			if (parameterCastType == typeof(int[]))
			{
				return JsonConvert.DeserializeObject<int[]>(value);
			}

			throw new Exception("cast type not supported!");
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

			return ConvertString(paramValue, parameterCastType, defaultValue);
        }

		private void SetParametersOnObject(string domain, object o)
        {
			foreach(FieldInfo field in o.GetType().GetFields())
			{
				if (field.IsPublic)
				{
					var paramVal = GetParameter(domain + "-" + field.Name.ToLower());
					if (string.IsNullOrEmpty(paramVal))
						paramVal = GetParameter(domain + "-" + field.Name);

					if (!string.IsNullOrEmpty(paramVal))
					{
						var castedVal = ConvertString(paramVal, field.FieldType, field.GetValue(o));
						field.SetValue(o, castedVal);
						Log(String.Format("- FIELD {0}.{1}= {2}", o.GetType().Name, field.Name, castedVal));
					}
				}
			}

            foreach(PropertyInfo prop in o.GetType().GetProperties())
            {
                if (prop.CanWrite)
                {
                    var paramVal = GetParameter(domain + "-" + prop.Name.ToLower());
                    if (string.IsNullOrEmpty(paramVal))
                        paramVal = GetParameter(domain + "-" + prop.Name);

                    if (!string.IsNullOrEmpty(paramVal))
                    {
						var castedVal = ConvertString(paramVal, prop.PropertyType, prop.GetValue(o));
						prop.SetValue(o, castedVal);
						Log(String.Format("- PROPERTY {0}.{1}= {2}", o.GetType().Name, prop.Name, castedVal));
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
