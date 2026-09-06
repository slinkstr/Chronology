using System.Globalization;

namespace Chronology.Services;

public static class TimeExpressionParser
{
    public static bool TryParse(string input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var tokens = Tokenize(input);
        if (tokens.Count == 0)
        {
            return false;
        }

        if (!TryParseToken(tokens[0].Text, out result))
        {
            return false;
        }

        for (var i = 1; i < tokens.Count; i++)
        {
            if (!TryParseToken(tokens[i].Text, out var tokenValue))
            {
                return false;
            }
            result = tokens[i].IsSubtract ? result - tokenValue : result + tokenValue;
        }

        return result >= TimeSpan.Zero;
    }

    private readonly record struct Token(string Text, bool IsSubtract);

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var span = input.AsSpan();
        var start = 0;
        var isSubtract = false;

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] is '+' or '-')
            {
                var tokenText = span[start..i].Trim();
                if (tokenText.IsEmpty)
                {
                    return new List<Token>(); // leading operator or consecutive operators
                }

                tokens.Add(new Token(tokenText.ToString(), isSubtract));
                isSubtract = span[i] == '-';
                start = i + 1;
            }
        }

        var lastTokenText = span[start..].Trim();
        if (lastTokenText.IsEmpty)
        {
            return new List<Token>(); // trailing operator
        }

        tokens.Add(new Token(lastTokenText.ToString(), isSubtract));
        return tokens;
    }

    private static bool TryParseToken(string token, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        token = token.Trim();

        if (token.Contains(':'))
        {
            var parts = token.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours))
            {
                return false;
            }
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            {
                return false;
            }
            if (minutes >= 60)
            {
                return false;
            }
            result = new TimeSpan(hours, minutes, 0);
            return true;
        }

        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var bareMinutes))
        {
            return false;
        }
        result = TimeSpan.FromMinutes(bareMinutes);
        return true;
    }
}
