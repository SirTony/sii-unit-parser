using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Sii.Parsing
{
    internal sealed class Parser
    {
        private static readonly ReadOnlyCollection<Type> IntegralTypes;
        private static readonly ReadOnlyCollection<Type> FloatTypes;
        private static readonly ReadOnlyCollection<Type> NumericTypes;
        private static readonly ReadOnlyCollection<Type> VectorTypes;

        static Parser()
        {
            IntegralTypes = new ReadOnlyCollection<Type>( new[]
            {
                typeof( sbyte ), typeof( byte ),
                typeof( short ), typeof( ushort ),
                typeof( int ), typeof( uint ),
                typeof( long ), typeof( ulong ),
            } );

            FloatTypes = new ReadOnlyCollection<Type>( new[]
            {
                typeof( float ), typeof( double ),
                typeof( decimal ),
            } );

            NumericTypes = IntegralTypes.Concat( FloatTypes )
                                        .ToList()
                                        .AsReadOnly();

            VectorTypes = new ReadOnlyCollection<Type>( new[]
            {
                typeof( Vector2 ),
                typeof( Vector3 ),
                typeof( Vector4 ),
                typeof( Quaternion )
            } );
        }

        private readonly ReadOnlyCollection<Token> Tokens;
        private readonly Token EndOfInput;
        private readonly int Length;
        private int Index;

        public Parser( Lexer lexer )
        {
            this.Tokens = lexer.Tokenize();
            this.EndOfInput = this.Tokens.Last();
            this.Length = this.Tokens.Count;
        }

        public ReadOnlyDictionary<string, object> Parse( IEnumerable<Type> types )
        {
            var map = new Dictionary<string, object>();

            var first = default( Token );
            if( !this.MatchAndTake( TokenKind.Identifier, out first ) || first.Text != "SiiNunit" )
                throw new SiiSyntaxException( this.Peek(), "SII unit files must begin with 'SiiNunit'" );

            this.Take( TokenKind.LeftBrace );

            var classes = types.Where( t => t.GetCustomAttribute<SiiUnitAttribute>() != null )
                               .ToDictionary( t => t.GetCustomAttribute<SiiUnitAttribute>().ClassName, t => t );
            var classDict = new ReadOnlyDictionary<string, Type>( classes );

            while( !this.Match( TokenKind.RightBrace ) )
            {
                var pair = this.ParseDefinition( classDict );
                if( pair == null )
                    continue;

                map.Add( pair.Value.Key, pair.Value.Value );
            }

            this.Take( TokenKind.RightBrace );
            this.Take( TokenKind.EndOfInput );

            return new ReadOnlyDictionary<string, object>( map );
        }

        private KeyValuePair<string, object>? ParseDefinition( ReadOnlyDictionary<string, Type> classes )
        {
            var className = this.Take( TokenKind.Identifier ).Text;
            this.Take( TokenKind.Colon );
            var anonymous = this.MatchAndTake( TokenKind.Dot );
            var builder = new StringBuilder();

            if( anonymous )
                builder.Append( this.Take().Text );

            builder.Append( this.Take( TokenKind.Identifier ).Text );
            var dot = default( Token );
            while( this.MatchAndTake( TokenKind.Dot, out dot ) )
            {
                builder.Append( dot.Text );
                builder.Append( this.Take( TokenKind.Identifier ).Text );
            }

            var name = builder.ToString();
            var type = default( Type );
            if( !anonymous && !classes.TryGetValue( className, out type ) )
                throw new SiiException( $"No type for {name} (class {className})" );

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var instance = Activator.CreateInstance( type );
            var members = type.GetFields( Flags )
                              .Where( f => f.GetCustomAttribute<SiiAttributeAttribute>() != null )
                              .Where( f => !f.IsSpecialName )
                              .Cast<MemberInfo>()
                              .Concat( type.GetProperties( Flags )
                                           .Where( p => p.GetCustomAttribute<SiiAttributeAttribute>() != null )
                                           .Where( p => !p.IsSpecialName )
                              ).ToArray();

            var memberType = default( Type );
            var arrays = new Dictionary<MemberInfo, List<object>>();
            this.Take( TokenKind.LeftBrace );
            while( !this.MatchAndTake( TokenKind.RightBrace ) )
            {
                var attribute = this.Take( TokenKind.Identifier ).Text;
                var isArray = this.MatchAndTake( TokenKind.LeftSquare );
                var arraySize = default( Token );
                if( isArray && this.MatchAndTake( TokenKind.Number, out arraySize ) )
                    throw new SiiSyntaxException( arraySize, "Fixed-length arrays are not supported" );

                var member = default( MemberInfo );
                var value = default( object );

                if( isArray )
                {
                    this.Take( TokenKind.RightSquare );
                    this.Take( TokenKind.Colon );

                    var list = new List<object>();
                    foreach( var pair in arrays )
                    {
                        if( pair.Key.GetCustomAttribute<SiiAttributeAttribute>()?.Name == attribute )
                        {
                            member = pair.Key;
                            list = pair.Value;
                            break;
                        }
                    }

                    if( member == null )
                    {
                        foreach( var m in members )
                        {
                            if( m.GetCustomAttribute<SiiAttributeAttribute>()?.Name == attribute )
                            {
                                member = m;
                                break;
                            }
                        }
                    }

                    memberType = this.GetDeclaredType( member );
                    if( !memberType.IsArray )
                        throw new SiiException( $"{member.Name} is not an array" );

                    value = this.ParseValue( memberType.GetElementType() );
                    list.Add( value );

                    if( !arrays.ContainsKey( member ) )
                        arrays.Add( member, list );

                    continue;
                }

                this.Take( TokenKind.Colon );
                foreach( var m in members )
                {
                    if( m.GetCustomAttribute<SiiAttributeAttribute>()?.Name == attribute )
                    {
                        member = m;
                        break;
                    }
                }

                memberType = this.GetDeclaredType( member );
                value = this.ParseValue( memberType );
                if( member is PropertyInfo )
                {
                    var property = member as PropertyInfo;
                    var setter = property.GetSetMethod( true );
                    setter?.Invoke( instance, new[] { value } );
                }
                else
                {
                    var field = member as FieldInfo;
                    field.SetValue( instance, value );
                }
            }

            foreach( var pair in arrays )
            {
                memberType = this.GetDeclaredType( pair.Key );
                var length = pair.Value.Count;
                var elementType = memberType.GetElementType();
                var array = Array.CreateInstance( elementType, length );
                for( var i = 0; i < length; ++i )
                    array.SetValue( pair.Value[i], i );

                if( pair.Key is PropertyInfo )
                {
                    var property = pair.Key as PropertyInfo;
                    var setter = property.GetSetMethod( true );
                    setter?.Invoke( instance, new[] { array } );
                }
                else
                {
                    var field = pair.Key as FieldInfo;
                    field.SetValue( instance, array );
                }
            }

            return anonymous ? (KeyValuePair<string, object>?)null : new KeyValuePair<string, object>( name, instance );
        }

        private object ParseValue( Type type )
        {
            var token = default( Token );
            if( this.MatchAndTake( TokenKind.LeftParen, out token ) )
            {
                if( !VectorTypes.Contains( type ) )
                    throw new SiiException( $"Type mismatch. Expected {type.Name} but found vector" );

                var fieldType = type.GetField( "X", BindingFlags.Public | BindingFlags.Instance ).FieldType;

                var values = new List<object>();
                values.Add( this.ParseScalarValue( fieldType ) );
                this.Take( TokenKind.Comma );
                do values.Add( this.ParseScalarValue( fieldType ) );
                while( this.MatchAndTake( TokenKind.Comma ) );
                this.Take( TokenKind.RightParen );

                var size = Marshal.SizeOf( type ) / Marshal.SizeOf( fieldType );

                if( values.Count != size )
                    throw new SiiException( $"Too {( values.Count > size ? "many" : "few" )} values for {type.Name}, expected {size}, got {values.Count}" );

                return Activator.CreateInstance( type, values.ToArray() );
            }

            return this.ParseScalarValue( type );
        }

        private Type GetDeclaredType( MemberInfo member )
        {
            if( member is PropertyInfo )
                return ( member as PropertyInfo ).PropertyType;
            else
                return ( member as FieldInfo ).FieldType;
        }

        private object ParseScalarValue( Type type )
        {
            var token = this.Take();
            switch( token.Kind )
            {
                case TokenKind.String:
                    if( type != typeof( string ) )
                        throw new SiiException( $"Type mistmatch. Expected string, got {type.Name}" );

                    return token.Text;

                case TokenKind.Number:
                    {
                        if( !NumericTypes.Contains( type ) )
                            throw new SiiException( $"Type mismatch. Expected numeric type, got {type.Name}" );

                        var format = (NumberFormat)token.Tag;

                        if( format == NumberFormat.HexFloat )
                            throw new SiiSyntaxException( token, "Hex floats are not supported" );

                        if( ( format == NumberFormat.Float || format == NumberFormat.HexFloat ) && !FloatTypes.Contains( type ) )
                            throw new SiiException( $"Type mismatch. Expected {type.Name}, got float" );

                        var style = format == NumberFormat.Float || format == NumberFormat.HexFloat ? NumberStyles.Float : NumberStyles.Integer;
                        var parser = type.GetMethod( "Parse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof( string ), typeof( NumberStyles ) }, null );
                        return parser.Invoke( null, new object[] { token.Text, style } );
                    }

                case TokenKind.True:
                case TokenKind.False:
                    if( type != typeof( bool ) )
                        throw new SiiException( $"Type mismatch. Expected bool, got {type.Name}" );

                    return token.Kind == TokenKind.True ? true : false;

                default:
                    throw new SiiException( $"Unsupported value type {token.Kind.ToString().ToLowerInvariant()}" );
            }
        }

        private IEnumerable<Type> GetTypes( IEnumerable<Assembly> assemblies )
            => assemblies.SelectMany( a => a.GetTypes() )
                         .Where( t => t.GetCustomAttribute<SiiUnitAttribute>() != null )
                         .Where( t => !t.IsSpecialName );

        private Token Peek( int distance = 0 )
        {
            var newIndex = this.Index + distance;
            if( newIndex < 0 || newIndex >= this.Length )
                return this.EndOfInput;

            return this.Tokens[newIndex];
        }

        private Token Take( TokenKind? kind = null )
        {
            if( kind == null )
                return this.Tokens[this.Index++];

            var current = this.Peek();
            if( current.Kind != kind.Value )
                throw new SiiSyntaxException( current, $"Unexpected {current}" );

            ++this.Index;
            return current;
        }

        private bool Match( TokenKind kind )
            => this.Peek().Kind == kind;

        private bool MatchAndTake( TokenKind kind )
        {
            var dummy = default( Token );
            return this.MatchAndTake( kind, out dummy );
        }

        private bool MatchAndTake( TokenKind kind, out Token token )
        {
            if( this.Match( kind ) )
            {
                token = this.Take();
                return true;
            }

            token = null;
            return false;
        }
    }
}
