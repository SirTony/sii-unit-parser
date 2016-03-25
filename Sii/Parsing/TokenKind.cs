﻿namespace Sii.Parsing
{
    internal enum TokenKind
    {
        Identifier,
        String,
        Number,
        True,
        False,

        LeftParen,
        RightParen,
        LeftSquare,
        RightSquare,
        LeftBrace,
        RightBrace,
        Colon,
        Dot,
        Comma,

        EndOfInput,
    }
}