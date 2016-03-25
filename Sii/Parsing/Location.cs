namespace Sii.Parsing
{
    public sealed class Location
    {
        public int Line { get; }
        public int Column { get; }
        public int Offset { get; }

        internal Location( int line, int column, int offset )
        {
            this.Line = line;
            this.Column = column;
            this.Offset = offset;
        }
    }
}
