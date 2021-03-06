﻿
using System;
using System.Globalization;
using QuantConnect.Configuration;
using QuantConnect.Logging;
using System.Linq;
using System.IO;
using QuantConnect.Data.Market;
using QuantConnect.Data;
using System.Collections.Generic;
using QuantConnect.Lean.Engine;
using System.ComponentModel.Composition;
using QuantConnect.Util;
using System.Threading;
using QuantConnect.Brokerages.TradeStation;

namespace QuantConnect.ToolBox.TradeCommand
{
    class Program
    {
        public static void Main(string[] args)
        {
            //Initialize:
            var mode = "RELEASE";
#if DEBUG
            mode = "DEBUG";
#endif

            if (OS.IsWindows)
            {
                Console.OutputEncoding = System.Text.Encoding.Unicode;
            }

            var environment = Config.Get("environment");
            var liveMode = Config.GetBool("live-mode");
            Log.DebuggingEnabled = Config.GetBool("debug-mode");
            Log.LogHandler = Composer.Instance.GetExportedValueByTypeName<ILogHandler>(Config.Get("log-handler", "CompositeLogHandler"));

            //Name thread for the profiler:
            Thread.CurrentThread.Name = "Algorithm Analysis Thread";
            Log.Trace("Engine.Main(): LEAN ALGORITHMIC TRADING ENGINE v" + Globals.Version + " Mode: " + mode + " (" + (Environment.Is64BitProcess ? "64" : "32") + "bit)");
            Log.Trace("Engine.Main(): Started " + DateTime.Now.ToShortTimeString());
            Log.Trace("Engine.Main(): Memory " + OS.ApplicationMemoryUsed + "Mb-App  " + +OS.TotalPhysicalMemoryUsed + "Mb-Used  " + OS.TotalPhysicalMemory + "Mb-Total");

            //TODO: instead, plug broker into Lean system, and build interface around it
            var orderProvider = new OrderProvider();
            var securityProvider = new SecurityProvider();
            var accountID = TradeStationBrokerageFactory.Configuration.AccountID;
            var accessToken = TradeStationBrokerageFactory.Configuration.AccessToken;
            TradeStationBrokerage broker = new TradeStationBrokerage(orderProvider, securityProvider, accountID, accessToken, TradeStationBrokerageFactory.Configuration.Simulation);

            try
            {
                var quotes = broker.GetQuotes(new List<string>() { "SPY", "VXX" });
                Commands.PrintQuotes(quotes);

                var balance = broker.GetCashBalance();
                Commands.PrintBalance(balance);

                var holdings = broker.GetAccountHoldings();
                Commands.PrintHoldings(holdings);

                var orders = broker.GetOpenOrders();
                Commands.PrintOrders(orders);

                //lookup options for VXX
                var symbols = broker.LookupSymbols(Symbol.CreateOption("VXX", "usa", OptionStyle.American, OptionRight.Call, 1, DateTime.Now));
                Commands.PrintSymbols(symbols);

                var option = symbols.First();
                var optionQuote = broker.GetQuote(option);
                Commands.PrintQuotes(optionQuote);

                broker.PlaceOrder(new Orders.LimitOrder(
                    option,
                    1,
                    (decimal)1.55,
                    DateTime.Now));

                /*
                broker.PlaceOrder(new Orders.MarketOrder(
                    option,
                    1,
                    DateTime.Now));
                    */


                //broker.PlaceOrder(new Orders.)

                /*
                broker.PlaceOrder(new Orders.LimitOrder(
                    Symbol.Create("SPY", SecurityType.Equity, Market.USA),
                    -10,
                    275,
                    DateTime.Now));

                broker.PlaceOrder(new Orders.MarketOrder(
                    Symbol.Create("SPY", SecurityType.Equity, Market.USA),
                    -10,
                    DateTime.Now));
                */
                orders = broker.GetOpenOrders();
                Commands.PrintOrders(orders);

                /*
                if (orders.Count > 0)
                {
                    bool result = broker.CancelOrder(orders[0]);
                    Console.WriteLine("cancel first order: {0}", result);
                }
                */
            }
            /*
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.Message == "Unauthorized")
                {
                    Console.WriteLine("Unauthorized: check token");
                }
                else
                {
                    throw ex;
                }
            }
            */
            finally {}

            //Import external libraries specific to physical server location (cloud/local)
            //LeanEngineSystemHandlers leanEngineSystemHandlers;
            /*
            try
            {
                leanEngineSystemHandlers = LeanEngineSystemHandlers.FromConfiguration(Composer.Instance);
            }
            catch (CompositionException compositionException)
            {
                Log.Error("Engine.Main(): Failed to load library: " + compositionException);
                throw;
            }

            //Setup packeting, queue and controls system: These don't do much locally.
            leanEngineSystemHandlers.Initialize();
            */
        }
    }
}
