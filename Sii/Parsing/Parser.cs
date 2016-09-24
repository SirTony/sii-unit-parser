using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Sii.Parsing
{
    internal sealed class Parser
    {
        /// <summary>
        /// Flags for loading MemberInfo's
        /// </summary>
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly ReadOnlyCollection<Type> IntegralTypes;
        private static readonly ReadOnlyCollection<Type> FloatTypes;
        private static readonly ReadOnlyCollection<Type> NumericTypes;
        private static readonly ReadOnlyCollection<Type> VectorTypes;

        static Parser()
        {
            IntegralTypes = new ReadOnlyCollection<Type>(new[]
            {
                typeof( sbyte ), typeof( byte ),
                typeof( short ), typeof( ushort ),
                typeof( int ), typeof( uint ),
                typeof( long ), typeof( ulong ),
            });

            FloatTypes = new ReadOnlyCollection<Type>(new[]
            {
                typeof( float ), typeof( double ),
                typeof( decimal ),
            });

            NumericTypes = IntegralTypes.Concat(FloatTypes)
                                        .ToList()
                                        .AsReadOnly();

            VectorTypes = new ReadOnlyCollection<Type>(new[]
            {
                typeof( Vector2 ),
                typeof( Vector3 ),
                typeof( Vector4 ),
                typeof( Quaternion )
            });
        }

        private readonly ReadOnlyCollection<Token> Tokens;
        private readonly Token EndOfInput;
        private readonly int Length;
        private int Index;
        private Lexer Lexer;
        private Dictionary<string, object> ClassMap = new Dictionary<string, object>();

        public Parser(Lexer lexer)
        {
            this.Lexer = lexer;
            this.Tokens = lexer.Tokenize();
            this.EndOfInput = this.Tokens.Last();
            this.Length = this.Tokens.Count;
        }

        /// <summary>
        /// Parses the SiiDocument using the specified C# types
        /// </summary>
        /// <param name="types">An array of custom object types, which can be parsed into from this SiiDocument</param>
        /// <returns></returns>
        public ReadOnlyDictionary<string, object> Parse(IEnumerable<Type> types)
        {
            var map = new Dictionary<string, object>();
            var first = default(Token);

            // Take the initial SiiNunit line
            if (!this.MatchAndTake(TokenKind.Identifier, out first) || first.Text != "SiiNunit")
                throw new SiiSyntaxException(this.Peek(), "SII unit files must begin with 'SiiNunit'");

            // Take the Brace on line 2
            this.Take(TokenKind.LeftBrace);

            // Compile our Class List, mapping name => type
            var classes = types.Where(t => t.GetCustomAttribute<SiiUnitAttribute>() != null)
                               .ToDictionary(t => t.GetCustomAttribute<SiiUnitAttribute>().ClassName, t => t);
            var classDict = new ReadOnlyDictionary<string, Type>(classes);

            // Grab all the structs in the document, and create their
            // initial instance value, so they can be used as attribute values,
            // no matter where in the file they are defined
            foreach (var item in classDict)
            {
                string pattern = @"^[\s\t]*" + item.Key + @"[\s\t]*:[\s\t]*(?<name>[\.a-z0-9_]+)[\s\t]*$";
                Regex reg = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                MatchCollection matches = reg.Matches(Lexer.Source);

                // Add each struct to the ClassMap Dictionary
                foreach (Match m in matches)
                {
                    var instance = Activator.CreateInstance(item.Value);
                    ClassMap.Add(m.Groups["name"].Value, instance);
                }
            }

            // Parse all the objects in the Sii file
            foreach (string cl in classDict.Keys)
            {
                // If we hit a right brace here, we are at the EOF
                while (!this.Match(TokenKind.RightBrace))
                {
                    var pair = this.ParseDefinition(classDict);
                    if (pair == null)
                        continue;

                    map.Add(pair.Value.Key, pair.Value.Value);
                }
            }

            // Take the last brace, and the EOF token
            this.Take( TokenKind.RightBrace );
            this.Take( TokenKind.EndOfInput );

            return new ReadOnlyDictionary<string, object>( map );
        }

        /// <summary>
        /// Parses an entire object and its properties
        /// </summary>
        /// <param name="classes">className => C# InstanceType</param>
        /// <returns></returns>
        private KeyValuePair<string, object>? ParseDefinition( ReadOnlyDictionary<string, Type> classes )
        {
            // Grab the classname
            var className = this.Take( TokenKind.Identifier ).Text;
            this.Take( TokenKind.Colon );

            // is this an anonymous classs?
            var anonymous = this.Match( TokenKind.Dot );

            // Begin fetching the class name
            var builder = new StringBuilder();
            if( anonymous )
                builder.Append( this.Take().Text );

            // Parse the object name, taking all sections seperated by dots
            builder.Append( this.Take( TokenKind.Identifier ).Text );
            var dot = default( Token );
            while( this.MatchAndTake( TokenKind.Dot, out dot ) )
            {
                builder.Append( dot.Text );
                builder.Append( this.Take( new[] { TokenKind.Identifier, TokenKind.Number } ).Text );
            }

            // Fetch the C# class type, so we can create an object instance
            var name = builder.ToString();
            var type = default( Type );
            if( !classes.TryGetValue( className, out type ) )
                throw new SiiException( $"No type for {name} (class {className})" );

            // Start fetching the class properties
            var instance = ClassMap[name];
            var members = type.GetFields( Flags )
                              .Where( f => f.GetCustomAttribute<SiiAttributeAttribute>() != null )
                              .Where( f => !f.IsSpecialName )
                              .Cast<MemberInfo>()
                              .Concat( type.GetProperties( Flags )
                                           .Where( p => p.GetCustomAttribute<SiiAttributeAttribute>() != null )
                                           .Where( p => !p.IsSpecialName )
                              ).ToArray();

            // Array of [C# ArrayMember => ListOfArrayValues]
            var arrays = new Dictionary<MemberInfo, List<object>>();
            var memberType = default(Type);
            this.Take( TokenKind.LeftBrace );

            // Parse through until we find the Right Brace
            while( !this.MatchAndTake( TokenKind.RightBrace ) )
            {
                // Grab the attribute name
                var attribute = this.Take( TokenKind.Identifier ).Text;
                // Check for array
                var isArray = this.MatchAndTake( TokenKind.LeftSquare );

                var member = default( MemberInfo );
                var value = default( object );

                if( isArray )
                {
                    // Attempt to grab the array index. We cant actually use it (as of yet) since
                    // we are using a list (since most array's don't define size)
                    var arrayIndex = default(Token);
                    this.MatchAndTake(TokenKind.Number, out arrayIndex);

                    // Grab the right square and colon
                    this.Take( TokenKind.RightSquare );
                    this.Take( TokenKind.Colon );

                    // First we find the proper C# property for this array value
                    // We search the cache'd array members first
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

                    // If we didnt find the property, this is our first access
                    if( member == null )
                    {
                        // Search all members
                        foreach( var m in members )
                        {
                            if( m.GetCustomAttribute<SiiAttributeAttribute>()?.Name == attribute )
                            {
                                member = m;
                                break;
                            }
                        }
                    }

                    // If there is no member to this attribute, throw an exception
                    if (member == null)
                        throw new SiiException($"No property for {attribute} found in (type {type.Name}) for (class {className})");

                    // Ensure that our C# member is an array type
                    memberType = this.GetDeclaredType( member );
                    if( !memberType.IsArray )
                        throw new SiiException( $"{member.Name} is not an array" );

                    // Grab the value of this attribute from the Sii object
                    value = this.ParseValue( memberType.GetElementType() );
                    list.Add( value );

                    // Add this member to the arrayMembers cache
                    if( !arrays.ContainsKey( member ) )
                        arrays.Add( member, list );

                    continue;
                }

                // Non-array.. Grab the colon and move on
                this.Take( TokenKind.Colon );
                foreach( var m in members )
                {
                    if( m.GetCustomAttribute<SiiAttributeAttribute>()?.Name == attribute )
                    {
                        member = m;
                        break;
                    }
                }

                // If we forgot to assign a member to this attribute, throw it up
                if (member == null)
                    throw new SiiException($"No property for {attribute} found in (type {type.Name}) for (class {className})");

                // Grab the C# attribute (Field or Property) type, and parse the value
                memberType = this.GetDeclaredType( member );
                value = this.ParseValue( memberType );

                // Apply the parsed value from the Sii Object into the C# member
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

            // Now that we have parsed this Object completly, Its time to
            // fill the array values on the C# members
            foreach( var pair in arrays )
            {
                memberType = this.GetDeclaredType( pair.Key );
                var length = pair.Value.Count;
                var elementType = memberType.GetElementType();

                // Create the Array
                var array = Array.CreateInstance( elementType, length );

                // Set each array index value
                for( var i = 0; i < length; ++i )
                    array.SetValue( pair.Value[i], i );

                // Set the C# member value to the newly filled array
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

            // Do not return nameless objects
            return anonymous ? (KeyValuePair<string, object>?) null : new KeyValuePair<string, object>( name, instance );
        }

        /// <summary>
        /// Parses the next token based on the C# member type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private object ParseValue( Type type )
        {
            var token = default( Token );

            // If we can take a left parentesis, then we have a Vector
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

                // Make sure the sizes of the Vectors match
                var size = Marshal.SizeOf( type ) / Marshal.SizeOf( fieldType );
                if( values.Count != size )
                    throw new SiiException( $"Too {( values.Count > size ? "many" : "few" )} values for {type.Name}, expected {size}, got {values.Count}" );

                return Activator.CreateInstance( type, values.ToArray() );
            }

            return this.ParseScalarValue( type );
        }

        /// <summary>
        /// Returns the C# Member type (not the value, but definition type)
        /// </summary>
        /// <param name="member"></param>
        /// <returns></returns>
        private Type GetDeclaredType( MemberInfo member )
        {
            if( member is PropertyInfo )
                return ( member as PropertyInfo ).PropertyType;
            else
                return ( member as FieldInfo ).FieldType;
        }

        /// <summary>
        /// Parses and converts a token value from the Sii attribute into a C# data type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
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

                case TokenKind.Dot:
                    // Grab the dot
                    var builder = new StringBuilder(token.Text);

                    // Parse the object name, taking all sections seperated by dots
                    builder.Append(this.Take(TokenKind.Identifier).Text);
                    var dot = default(Token);
                    while (this.MatchAndTake(TokenKind.Dot, out dot))
                    {
                        builder.Append(dot.Text);
                        builder.Append(this.Take(TokenKind.Identifier).Text);
                    }

                    // Fetch the C# class type, so we can create an object instance
                    var name = builder.ToString();
                    return ClassMap[name];
                default:
                    throw new SiiException( $"Unsupported value type {token.Kind.ToString().ToLowerInvariant()}" );
            }
        }

        /// <summary>
        /// Returns the <see cref="Token"/> at the secified index, offset by
        /// the current index
        /// </summary>
        /// <param name="distance"></param>
        /// <returns></returns>
        private Token Peek( int distance = 0 )
        {
            var newIndex = this.Index + distance;
            if( newIndex < 0 || newIndex >= this.Length )
                return this.EndOfInput;

            return this.Tokens[newIndex];
        }

        /// <summary>
        /// Takes the next token
        /// </summary>
        /// <param name="kind">If the new token does not match the specified token, an parser error occurs</param>
        /// <returns></returns>
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

        /// <summary>
        /// Takes the next token
        /// </summary>
        /// <param name="kinds">Specifies the expected following Token. If these token do 
        /// not match the specified token, an parser error occurs</param>
        /// <returns></returns>
        private Token Take(TokenKind[] kinds)
        {
            var current = this.Peek();
            foreach (TokenKind token in kinds)
            {
                if (token == current.Kind)
                {
                    ++this.Index;
                    return current;
                }
            }

            throw new SiiSyntaxException(current, $"Unexpected {current}");
        }

        /// <summary>
        /// Returns if the Next token matches the specified token type
        /// </summary>
        /// <param name="kind"></param>
        /// <returns></returns>
        private bool Match( TokenKind kind ) => this.Peek().Kind == kind;

        /// <summary>
        /// Takes the next token if the specified tokenkind matches.
        /// </summary>
        /// <param name="kind"></param>
        /// <returns></returns>
        private bool MatchAndTake( TokenKind kind )
        {
            var dummy = default( Token );
            return this.MatchAndTake( kind, out dummy );
        }

        /// <summary>
        /// Takes the next token if the specified tokenkind matches.
        /// </summary>
        /// <param name="kind"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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
