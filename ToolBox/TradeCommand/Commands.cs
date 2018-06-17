﻿using System;
using System.Collections;
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation;
using QuantConnect.Orders;

namespace QuantConnect.ToolBox.TradeCommand
{
    public class Commands
    {
        public Commands()
        {
        }

        public static void PrintQuotes(IEnumerable<Anonymous3> quotes)
        {
            Console.WriteLine("= Quotes =");
            foreach(var q in quotes)
            {
                Console.WriteLine("{0} = {1}", q.Symbol, q.Close);
            }
        }

        public static void PrintBalance(IEnumerable<Securities.Cash> balances)
        {
            Console.WriteLine("= Cash Balances =");
            foreach(var b in balances)
            {
                Console.WriteLine("{0} = {1}", b.CurrencySymbol, b.Amount);
            }
        }

        public static void PrintHoldings(IEnumerable<Holding> holdings)
        {
            Console.WriteLine("= Holdings =");
            foreach (var h in holdings)
            {
                Console.WriteLine("{0} = {1}", h.Symbol.ToString(), h.Quantity);
            }
        }

        public static void PrintOrders(IEnumerable<Order> orders)
        {
            Console.WriteLine("= Orders =");
            foreach (var o in orders)
            {
                Console.WriteLine("{0}\t({1}) [{2}]\t = {3}", o.Symbol.ToString(), o.SecurityType, o.Status, o.Quantity);
            }
        }
    }
}
