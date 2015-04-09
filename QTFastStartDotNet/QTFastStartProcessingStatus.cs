using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QTFastStartDotNet
{
    public enum QTFastStartProcessingStatus 
    {
        Success = 0,
        AlreadyConverted = 1,
        Error = -1,
        FileIsCompressed = -2, 
        InvalidFormat = -3,
    }
}
