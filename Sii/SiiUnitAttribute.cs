using System;

namespace Sii
{
    [AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct )]
    public sealed class SiiUnitAttribute : Attribute
    {
        public string ClassName { get; }

        public SiiUnitAttribute( string className )
        {
            if( String.IsNullOrWhiteSpace( className ) )
                throw new ArgumentNullException( nameof( className ) );

            this.ClassName = className;
        }
    }
}
