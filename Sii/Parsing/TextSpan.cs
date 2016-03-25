namespace Sii.Parsing
{
    public sealed class TextSpan
    {
        public Location Start { get; }
        public Location End { get; }
        public int Length => ( this.End?.Offset - this.Start?.Offset ) ?? -1;

        internal TextSpan( Location start, Location end )
        {
            this.Start = start;
            this.End = end;
        }

        internal TextSpan WithEnd( Location end )
            => new TextSpan( this.Start, end );
    }
}
