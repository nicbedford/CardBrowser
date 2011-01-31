using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PCSC
{
    /// <summary>
    ///  PC/SC exceptions
    /// </summary>
    public class PCSCException : Exception
    {
        public PCSCException()
            : base("PC/SC exception")
        {
        }

        public PCSCException(int result)
            : base(WinSCard.SCardErrorMessage(result))
        {
            Result = result;
        }

        public int Result { get; private set; }
    }
}
