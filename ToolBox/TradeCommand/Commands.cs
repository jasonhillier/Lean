using System;
using System.Collections;
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation;

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
    }
}
