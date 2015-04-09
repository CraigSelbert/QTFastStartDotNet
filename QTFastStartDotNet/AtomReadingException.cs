using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace qtfaststart
{
    public class AtomReadingException : Exception
    {
        public AtomReadingException(string msg) : base(msg)
        {
        }
    }
}
