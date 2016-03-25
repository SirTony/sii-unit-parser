namespace Sii.Parsing
{
    internal class Token
    {
        public string Text { get; }
        public TokenKind Kind { get; }
        public TextSpan Span { get; }
        public string FileName { get; }
        public object Tag { get; }

        public Token( string text, TokenKind kind, TextSpan span, string fileName, object tag )
        {
            this.Text = text;
            this.Kind = kind;
            this.Span = span;
            this.FileName = fileName;
            this.Tag = tag;
        }

        public override string ToString()
        {
            switch( this.Kind )
            {
                case TokenKind.EndOfInput:
                    return "end-of-input";

                case TokenKind.Identifier:
                    return this.Text;

                case TokenKind.Number:
                case TokenKind.True:
                case TokenKind.False:
                    return this.Kind.ToString().ToLowerInvariant();

                default:
                    return this.Text;
            }
        }
    }
}
