using System;

namespace Sii
{
    [AttributeUsage( AttributeTargets.Field | AttributeTargets.Property )]
    public sealed class SiiAttributeAttribute : Attribute
    {
        public string Name { get; }

        public SiiAttributeAttribute( string name )
        {
            if( String.IsNullOrWhiteSpace( name ) )
                throw new ArgumentNullException( nameof( name ) );

            this.Name = name;
        }
    }
}
