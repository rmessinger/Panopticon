#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Panopticon.Content.Importing.Units.Ee2;

/// <summary>
/// Card WI-H2a-15: bounded reader for EE2 <c>.ddf</c> files. Supports the record subset unit
/// content needs: <c>Type Name { ... }</c> records whose members are <c>key = value</c> or
/// <c>key { ... }</c> / <c>key [ ... ]</c>, scalar, quoted, block, named-block, and array values,
/// <c>//</c> and <c>/* */</c> comments, and <c>;</c>/<c>,</c> separators. Input beyond the size and
/// nesting limits, unbalanced brackets, and truncation fail with a source position.
/// </summary>
internal sealed class DdfReader
{
    public const int MaxFileBytes = 4 * 1024 * 1024;
    public const int MaxDepth = 32;

    private readonly string _text;
    private readonly string _file;
    private int _index;
    private int _line = 1;
    private int _column = 1;

    private DdfReader(string text, string file)
    {
        _text = text;
        _file = file;
    }

    public static IReadOnlyList<DdfRecord> Read(string text, string file)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes)
        {
            throw new InvalidDataException($"{file}: DDF file exceeds {MaxFileBytes} bytes.");
        }

        return new DdfReader(text, file).ReadRecords();
    }

    private IReadOnlyList<DdfRecord> ReadRecords()
    {
        var records = new List<DdfRecord>();

        while (Peek() is { } token)
        {
            if (token.Kind != TokenKind.Word)
            {
                throw Error(token, $"expected a record type, found '{token.Text}'");
            }

            Next();
            var name = Expect(TokenKind.Word, "a record name");
            var open = Expect(TokenKind.OpenBrace, "'{'");
            records.Add(new DdfRecord(token.Text, name.Text, ReadBlockBody(null, open.Position, depth: 1), token.Position));
        }

        return records;
    }

    private DdfBlock ReadBlockBody(string? name, DdfPosition position, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException($"{position}: DDF nesting exceeds {MaxDepth} levels.");
        }

        var members = new List<DdfMember>();

        while (true)
        {
            var token = Next() ?? throw new InvalidDataException($"{position}: DDF block is not closed before end of file.");

            switch (token.Kind)
            {
                case TokenKind.CloseBrace:
                    return new DdfBlock(name, members, position);
                case TokenKind.Separator:
                    continue;
                case TokenKind.Word:
                    if (Peek() is { Kind: TokenKind.Equals })
                    {
                        Next();
                    }

                    members.Add(new DdfMember(token.Text, ReadValue(depth), token.Position));
                    continue;
                default:
                    throw Error(token, $"expected a member key, found '{token.Text}'");
            }
        }
    }

    private DdfValue ReadValue(int depth)
    {
        var token = Next() ?? throw new InvalidDataException($"{_file}: value expected before end of file.");

        switch (token.Kind)
        {
            case TokenKind.OpenBrace:
                return ReadBlockBody(null, token.Position, depth + 1);
            case TokenKind.OpenBracket:
                return ReadArray(token.Position, depth + 1);
            case TokenKind.Word:
            case TokenKind.Quoted:
                return new DdfScalar(token.Text, token.Position);
            default:
                throw Error(token, $"expected a value, found '{token.Text}'");
        }
    }

    private DdfArray ReadArray(DdfPosition position, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException($"{position}: DDF nesting exceeds {MaxDepth} levels.");
        }

        var items = new List<DdfValue>();

        while (true)
        {
            var token = Next() ?? throw new InvalidDataException($"{position}: DDF array is not closed before end of file.");

            switch (token.Kind)
            {
                case TokenKind.CloseBracket:
                    return new DdfArray(items, position);
                case TokenKind.Separator:
                    continue;
                case TokenKind.OpenBrace:
                    items.Add(ReadBlockBody(null, token.Position, depth + 1));
                    continue;
                case TokenKind.OpenBracket:
                    items.Add(ReadArray(token.Position, depth + 1));
                    continue;
                case TokenKind.Word when Peek() is { Kind: TokenKind.OpenBrace }:
                    var open = Next()!.Value;
                    items.Add(ReadBlockBody(token.Text, open.Position, depth + 1));
                    continue;
                case TokenKind.Word:
                case TokenKind.Quoted:
                    items.Add(new DdfScalar(token.Text, token.Position));
                    continue;
                default:
                    throw Error(token, $"unexpected '{token.Text}' in array");
            }
        }
    }

    private Token Expect(TokenKind kind, string description)
    {
        var token = Next() ?? throw new InvalidDataException($"{_file}: {description} expected before end of file.");
        return token.Kind == kind ? token : throw Error(token, $"expected {description}, found '{token.Text}'");
    }

    private InvalidDataException Error(Token token, string message) => new($"{token.Position}: {message}.");

    private Token? _peeked;

    private Token? Peek() => _peeked ??= Scan();

    private Token? Next()
    {
        var token = Peek();
        _peeked = null;
        return token;
    }

    private Token? Scan()
    {
        SkipTrivia();
        if (_index >= _text.Length)
        {
            return null;
        }

        var position = new DdfPosition(_file, _line, _column);
        var c = _text[_index];

        switch (c)
        {
            case '{':
                Advance();
                return new Token(TokenKind.OpenBrace, "{", position);
            case '}':
                Advance();
                return new Token(TokenKind.CloseBrace, "}", position);
            case '[':
                Advance();
                return new Token(TokenKind.OpenBracket, "[", position);
            case ']':
                Advance();
                return new Token(TokenKind.CloseBracket, "]", position);
            case '=':
                Advance();
                return new Token(TokenKind.Equals, "=", position);
            case ';' or ',':
                Advance();
                return new Token(TokenKind.Separator, c.ToString(), position);
            case '"':
                return ScanQuoted(position);
        }

        var start = _index;
        while (_index < _text.Length && !char.IsWhiteSpace(_text[_index]) && "{}[]=;,\"".IndexOf(_text[_index]) < 0 && !IsCommentStart())
        {
            Advance();
        }

        return new Token(TokenKind.Word, _text[start.._index], position);
    }

    private Token ScanQuoted(DdfPosition position)
    {
        Advance();
        var start = _index;
        while (_index < _text.Length && _text[_index] != '"')
        {
            if (_text[_index] == '\n')
            {
                throw new InvalidDataException($"{position}: quoted string is not closed on its line.");
            }

            Advance();
        }

        if (_index >= _text.Length)
        {
            throw new InvalidDataException($"{position}: quoted string is not closed before end of file.");
        }

        var text = _text[start.._index];
        Advance();
        return new Token(TokenKind.Quoted, text, position);
    }

    private void SkipTrivia()
    {
        while (_index < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_index]))
            {
                Advance();
            }
            else if (Matches("//"))
            {
                while (_index < _text.Length && _text[_index] != '\n')
                {
                    Advance();
                }
            }
            else if (Matches("/*"))
            {
                var position = new DdfPosition(_file, _line, _column);
                Advance();
                Advance();
                while (_index < _text.Length && !Matches("*/"))
                {
                    Advance();
                }

                if (_index >= _text.Length)
                {
                    throw new InvalidDataException($"{position}: block comment is not closed before end of file.");
                }

                Advance();
                Advance();
            }
            else
            {
                return;
            }
        }
    }

    private bool IsCommentStart() => Matches("//") || Matches("/*");

    private bool Matches(string value) => string.CompareOrdinal(_text, _index, value, 0, value.Length) == 0;

    private void Advance()
    {
        if (_text[_index] == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        _index++;
    }

    private enum TokenKind
    {
        Word,
        Quoted,
        OpenBrace,
        CloseBrace,
        OpenBracket,
        CloseBracket,
        Equals,
        Separator,
    }

    private readonly record struct Token(TokenKind Kind, string Text, DdfPosition Position);
}
