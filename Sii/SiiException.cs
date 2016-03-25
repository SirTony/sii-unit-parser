using System;

namespace Sii
{
    public class SiiException : Exception
    {
        internal SiiException( string message ) : base( message ) { }
    }
}
