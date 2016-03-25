using Sii.Parsing;

namespace Sii
{
    public sealed class SiiSyntaxException : SiiException
    {
        public TextSpan Span { get; }

        internal SiiSyntaxException( TextSpan span, string message ) : base( message )
        {
            this.Span = span;
        }

        internal SiiSyntaxException( Token token, string message ) : this( token.Span, message ) { }
    }
}
