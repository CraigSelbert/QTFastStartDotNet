using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QTFastStartDotNet;

namespace QTFastStartApp
{
    class Program
    {
        static void Main(string[] args)
        {
            QTFastStartProcessor p = new QTFastStartProcessor();
            DateTime start = DateTime.Now;
            p.Process(args[0], args[1]);
            Console.WriteLine("Processed video in: {0} seconds", DateTime.Now.Subtract(start).TotalSeconds);
        }
    }
}
