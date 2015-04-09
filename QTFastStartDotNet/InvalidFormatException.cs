using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace qtfaststart
{
    internal class InvalidFormatException : Exception
    {
        public InvalidFormatException(string msg) : base(msg)
        {
        }
    }
}
