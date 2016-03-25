using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Sii.Parsing
{
    internal sealed class Lexer
    {
        private delegate bool Lexeme( char c, out Token token );

        private static readonly ReadOnlyDictionary<string, TokenKind> Keywords;
        private static readonly ReadOnlyDictionary<char, TokenKind> Punctuation;
        private static readonly ReadOnlyCollection<char> HexDigits;

        static Lexer()
        {
            var punctuation = new Dictionary<char, TokenKind>
            {
                ['('] = TokenKind.LeftParen,
                [')'] = TokenKind.RightParen,
                ['['] = TokenKind.LeftSquare,
                [']'] = TokenKind.RightSquare,
                ['{'] = TokenKind.LeftBrace,
                ['}'] = TokenKind.RightBrace,
                [':'] = TokenKind.Colon,
                ['.'] = TokenKind.Dot,
                [','] = TokenKind.Comma,
            };

            var keywords = new Dictionary<string, TokenKind>
            {
                ["true"] = TokenKind.True,
                ["false"] = TokenKind.False,
            };

            Keywords = new ReadOnlyDictionary<string, TokenKind>( keywords );
            Punctuation = new ReadOnlyDictionary<char, TokenKind>( punctuation );
            HexDigits = new ReadOnlyCollection<char>( new[]
            {
                'a', 'b', 'c', 'd', 'e', 'f',
                'A', 'B', 'C', 'D', 'E', 'F',
            } );
        }

        private readonly string FileName;
        private readonly string Source;
        private readonly int Length;
        private int Index;
        private int Line;
        private int Column;

        private readonly Stack<TextSpan> Spans;
        private readonly ReadOnlyCollection<Lexeme> Lexemes;

        private bool EndOfInput => this.Index >= this.Length;

        public Lexer( string source, string fileName )
        {
            this.FileName = fileName;
            this.Source = source;
            this.Length = source.Length;
            this.Spans = new Stack<TextSpan>();
            this.Lexemes = new ReadOnlyCollection<Lexeme>( new Lexeme[]
            {
                this.TryLexIdentifier,
                this.TryLexNumber,
                this.TryLexString,
                this.TryLexPunctuation,
            } );
        }

        public ReadOnlyCollection<Token> Tokenize()
        {
            this.Index = 0;
            this.Line = 1;
            this.Column = 1;

            var tokens = new List<Token>();
            while( !this.EndOfInput )
            {
                this.SkipWhile( Char.IsWhiteSpace );

                var current = this.Peek();
                if( this.SkipComments( current ) )
                    continue;

                this.MarkStart();
                var token = default( Token );
                var success = this.Lexemes.Any( lexeme => lexeme( current, out token ) );
                if( !success )
                    throw new SiiSyntaxException( this.MarkEnd(), $"Unexpected character '{current}' (0x{Convert.ToUInt16( current ):X4})" );

                tokens.Add( token );
                this.Spans.Pop();
            }

            this.MarkStart();
            var eof = this.MakeToken( TokenKind.EndOfInput, null );
            tokens.Add( eof );
            
            return tokens.AsReadOnly();
        }

        private void MarkStart()
        {
            var location = new Location( this.Line, this.Column, this.Index );
            var span = new TextSpan( location, null );
            this.Spans.Push( span );
        }

        private TextSpan MarkEnd()
        {
            var location = new Location( this.Line, this.Column, this.Index );
            return this.Spans.Pop().WithEnd( location );
        }

        private Token MakeToken( TokenKind kind, string text, object tag = null )
        {
            var span = this.MarkEnd();
            return new Token( text, kind, span, this.FileName, tag );
        }

        private bool SkipComments( char c )
            => this.SkipLineComments( c ) || this.SkipBlockComments( c );

        private bool SkipLineComments( char c )
        {
            if( c != '#' )
                return false;

            this.SkipWhile( ch => ch != '\n' && ch != '\r' );
            return true;
        }

        private bool SkipBlockComments( char c )
        {
            if( !this.IsNext( "/*" ) )
                return false;

            this.MarkStart();
            this.Skip( 2 );

            var closed = false;
            while( !this.EndOfInput )
            {
                if( this.TakeIfNext( "*/" ) )
                {
                    closed = true;
                    break;
                }

                this.Take();
            }

            if( !closed )
                throw new SiiSyntaxException( this.MarkEnd(), "Unexpected end-of-input (unclosed multi-line comment)" );

            this.Spans.Pop();
            return true;
        }

        private bool TryLexIdentifier( char c, out Token token )
        {
            if( !Char.IsLetter( c ) && c != '_' )
            {
                token = null;
                return false;
            }

            this.MarkStart();
            var text = this.TakeWhile( ch => Char.IsLetterOrDigit( ch ) || ch == '_' );
            var kind = Keywords.ContainsKey( text ) ? Keywords[text] : TokenKind.Identifier;
            token = this.MakeToken( kind, text );
            return true;
        }

        private bool TryLexNumber( char c, out Token token )
        {
            if( c != '&' && !Char.IsDigit( c ) )
            {
                token = null;
                return false;
            }

            this.MarkStart();
            var text = default( string );
            if( c == '&' )
            {
                this.Take();
                text = this.TakeWhile( ch => Char.IsDigit( ch ) || HexDigits.Contains( ch ) );
                if( text.Length != 8 )
                    throw new SiiSyntaxException( this.MarkEnd(), "Hexadecimal floating point numbers must be 8 characters" );

                token = this.MakeToken( TokenKind.Number, text, NumberFormat.HexFloat );
                return true;
            }

            var hasDecimal = false;
            var hasExponent = false;
            var forceTake = false;
            var format = NumberFormat.Decimal;
            text = this.TakeWhile( delegate ( char ch )
            {
                if( forceTake )
                {
                    forceTake = false;
                    return true;
                }

                var next = this.Peek( 1 );

                if( ch == '.' && Char.IsDigit( next ) )
                {
                    if( hasDecimal )
                        throw new SiiSyntaxException( this.MarkEnd(), "Number already has a decimal point" );

                    format = NumberFormat.Float;
                    hasDecimal = true;
                    return true;
                }

                if( ( ch == 'e' || ch == 'E' ) )
                {
                    var second = this.Peek( 2 );
                    if( !Char.IsDigit( next ) && !( ( next == '-' || next == '+' ) && Char.IsDigit( second ) ) )
                        return false;

                    if( hasExponent )
                        throw new SiiSyntaxException( this.MarkEnd(), "Number already has exponent" );

                    if( ( next == '-' || next == '+' ) && Char.IsDigit( second ) )
                        forceTake = true;

                    format = NumberFormat.Float;
                    hasExponent = true;
                    return Char.IsDigit( next ) || Char.IsDigit( second );
                }

                return Char.IsDigit( ch );
            } );

            token = this.MakeToken( TokenKind.Number, text, format );
            return true;
        }

        private bool TryLexString( char c, out Token token )
        {
            if( c != '"' )
            {
                token = null;
                return false;
            }

            this.MarkStart();
            this.Take();
            var closed = false;
            var builder = new StringBuilder();
            var current = default( char );
            while( !this.EndOfInput )
            {
                if( ( current = this.Peek() ) == '"' )
                {
                    this.Take();
                    closed = true;
                    break;
                }

                if( current == '\\' )
                {
                    if( this.EndOfInput )
                        throw new SiiSyntaxException( this.MarkEnd(), "Unexpected end-of-input (unclosed string)" );

                    var escape = this.HandleEscapeSequence( this.Take() );
                    builder.Append( escape );
                    continue;
                }

                builder.Append( this.Take() );
            }

            if( !closed )
                throw new SiiSyntaxException( this.MarkEnd(), "Unexpected end-of-input (unclosed string)" );

            token = this.MakeToken( TokenKind.String, builder.ToString() );
            return true;
        }

        private char HandleEscapeSequence( char seq )
        {
            if( this.EndOfInput )
                throw new SiiSyntaxException( this.MarkEnd(), "Unexpected end-of-input (invalid escape sequence)" );

            switch( seq )
            {
                case 'a':
                    return '\a';
                case 'b':
                    return '\b';
                case 'f':
                    return '\f';
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                case 'v':
                    return '\v';
                case '0':
                    return '\0';
                case '\'':
                case '"':
                case '\\':
                case '?':
                    return seq;

                case 'x':
                    {
                        if( this.Index + 2 >= this.Length )
                            throw new SiiSyntaxException( this.MarkEnd(), $"Unexpected end-of-input (invalid \\x escape)" );

                        var hex = this.Take( 4 );
                        if( hex.Length != 2 && hex.Length != 4 )
                            throw new SiiSyntaxException( this.MarkEnd(), "Unexpected end-of-input (invalid \\x escape)" );

                        var code = UInt16.Parse( hex, NumberStyles.AllowHexSpecifier );
                        return Convert.ToChar( code );
                    }

                default:
                    {
                        var octal = this.Take( 3 );
                        if( octal.Length == 3 )
                        {
                            try
                            {
                                var code = Convert.ToUInt16( octal, 8 );
                                return Convert.ToChar( code );
                            }
                            catch
                            {
                                throw new SiiSyntaxException( this.MarkEnd(), $"Invalid octal escape sequence \\{octal}" );
                            }
                        }

                        throw new SiiSyntaxException( this.MarkEnd(), $"Unregocnized escape sequence \\{seq}" );
                    }
            }
        }

        private bool TryLexPunctuation( char c, out Token token )
        {
            foreach( var pair in Punctuation )
            {
                if( pair.Key == c )
                {
                    this.MarkStart();
                    this.Take();
                    token = this.MakeToken( pair.Value, pair.Key.ToString() );
                    return true;
                }
            }

            token = null;
            return false;
        }

        private char Peek( int distance = 0 )
        {
            var newIndex = this.Index + distance;
            if( newIndex < 0 || newIndex >= this.Length )
                return '\0';

            return this.Source[newIndex];
        }

        private bool IsNext( string search )
        {
            var len = search.Length;
            if( this.Index + len >= this.Length )
                return false;

            return this.Source.Substring( this.Index, len ) == search;
        }

        private char Take()
        {
            var current = this.Peek();
            var next = this.Peek( 1 );

            if( current == '\r' )
            {
                ++this.Line;
                this.Column = 0;

                if( next == '\n' )
                {
                    ++this.Index;
                    current = next;
                }
            }
            else if( current == '\n' )
            {
                ++this.Line;
                this.Column = 0;
            }

            ++this.Column;
            ++this.Index;

            return current;
        }

        private string Take( int amount )
        {
            var builder = new StringBuilder();
            for( var i = 0; i <= amount && !this.EndOfInput; ++i )
                builder.Append( this.Take() );

            return builder.ToString();
        }

        private bool TakeIfNext( string search )
        {
            if( this.IsNext( search ) )
            {
                this.Skip( search.Length );
                return true;
            }

            return false;
        }

        private string TakeWhile( Predicate<char> predicate )
        {
            var builder = new StringBuilder();
            while( !this.EndOfInput && predicate( this.Peek() ) )
                builder.Append( this.Take() );

            return builder.ToString();
        }

        private void Skip( int amount )
        {
            for( var i = 0; i <= amount; ++i )
                this.Take();
        }

        private void SkipWhile( Predicate<char> predicate )
        {
            while( !this.EndOfInput && predicate( this.Peek() ) )
                this.Take();
        }
    }
}
