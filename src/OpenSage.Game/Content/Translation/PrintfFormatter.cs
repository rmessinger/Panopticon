#nullable enable

using System;
using System.Text;

namespace OpenSage.Content.Translation;

/// <summary>
/// Managed C-style printf formatter, replacing the native Sprintf.NET dependency
/// which lacks macOS runtime support.
/// </summary>
public static class PrintfFormatter
{
    public static string Format(string format, params object?[] args)
    {
        if (args.Length == 0)
        {
            return format;
        }

        var result = new StringBuilder(format.Length + 16);
        var argIndex = 0;
        var i = 0;

        while (i < format.Length)
        {
            if (format[i] != '%')
            {
                result.Append(format[i++]);
                continue;
            }

            i++; // skip '%'

            if (i >= format.Length)
            {
                result.Append('%');
                break;
            }

            if (format[i] == '%')
            {
                result.Append('%');
                i++;
                continue;
            }

            // Parse flags
            var leftAlign = false;
            var forceSign = false;
            var spaceSign = false;
            var zeroPad = false;
            var altForm = false;

            while (i < format.Length)
            {
                switch (format[i])
                {
                    case '-': leftAlign = true; i++; continue;
                    case '+': forceSign = true; i++; continue;
                    case ' ': spaceSign = true; i++; continue;
                    case '0': zeroPad = true; i++; continue;
                    case '#': altForm = true; i++; continue;
                }
                break;
            }

            // Parse width
            var width = 0;
            while (i < format.Length && char.IsAsciiDigit(format[i]))
            {
                width = width * 10 + (format[i++] - '0');
            }

            // Parse precision
            var precision = -1;
            if (i < format.Length && format[i] == '.')
            {
                i++;
                precision = 0;
                while (i < format.Length && char.IsAsciiDigit(format[i]))
                {
                    precision = precision * 10 + (format[i++] - '0');
                }
            }

            // Skip length modifiers (l, ll, h, hh, L, z, j, t)
            while (i < format.Length && "lLhHzjtq".Contains(format[i]))
            {
                i++;
            }

            if (i >= format.Length)
            {
                break;
            }

            var specifier = format[i++];
            var arg = argIndex < args.Length ? args[argIndex++] : null;

            var formatted = FormatArg(specifier, arg, precision, forceSign, spaceSign, altForm, zeroPad);

            // Apply width/alignment
            if (width > 0 && formatted.Length < width)
            {
                if (leftAlign)
                {
                    formatted = formatted.PadRight(width);
                }
                else if (zeroPad && IsNumericSpecifier(specifier))
                {
                    // Zero-pad: handle sign prefix specially
                    if (formatted.Length > 0 && (formatted[0] == '-' || formatted[0] == '+' || formatted[0] == ' '))
                    {
                        formatted = formatted[0] + formatted[1..].PadLeft(width - 1, '0');
                    }
                    else
                    {
                        formatted = formatted.PadLeft(width, '0');
                    }
                }
                else
                {
                    formatted = formatted.PadLeft(width);
                }
            }

            result.Append(formatted);
        }

        return result.ToString();
    }

    private static string FormatArg(char specifier, object? arg, int precision, bool forceSign, bool spaceSign, bool altForm, bool zeroPad)
    {
        switch (specifier)
        {
            case 'd':
            case 'i':
                {
                    var val = Convert.ToInt64(arg ?? 0);
                    var s = Math.Abs(val).ToString();
                    if (val < 0) return "-" + s;
                    if (forceSign) return "+" + s;
                    if (spaceSign) return " " + s;
                    return s;
                }
            case 'u':
                {
                    var val = Convert.ToUInt64(arg ?? 0UL);
                    return val.ToString();
                }
            case 'o':
                {
                    var val = Convert.ToUInt64(arg ?? 0UL);
                    var s = Convert.ToString((long)val, 8);
                    return altForm && val != 0 ? "0" + s : s;
                }
            case 'x':
                {
                    var val = Convert.ToUInt64(arg ?? 0UL);
                    var s = val.ToString("x");
                    return altForm && val != 0 ? "0x" + s : s;
                }
            case 'X':
                {
                    var val = Convert.ToUInt64(arg ?? 0UL);
                    var s = val.ToString("X");
                    return altForm && val != 0 ? "0X" + s : s;
                }
            case 'f':
            case 'F':
                {
                    var val = Convert.ToDouble(arg ?? 0.0);
                    var prec = precision >= 0 ? precision : 6;
                    var s = Math.Abs(val).ToString("F" + prec);
                    if (val < 0) return "-" + s;
                    if (forceSign) return "+" + s;
                    if (spaceSign) return " " + s;
                    return s;
                }
            case 'e':
                {
                    var val = Convert.ToDouble(arg ?? 0.0);
                    var prec = precision >= 0 ? precision : 6;
                    var s = val.ToString("e" + prec);
                    if (forceSign && val >= 0) return "+" + s;
                    if (spaceSign && val >= 0) return " " + s;
                    return s;
                }
            case 'E':
                {
                    var val = Convert.ToDouble(arg ?? 0.0);
                    var prec = precision >= 0 ? precision : 6;
                    var s = val.ToString("E" + prec);
                    if (forceSign && val >= 0) return "+" + s;
                    if (spaceSign && val >= 0) return " " + s;
                    return s;
                }
            case 'g':
            case 'G':
                {
                    var val = Convert.ToDouble(arg ?? 0.0);
                    var prec = precision > 0 ? precision : 6;
                    var fmt = specifier == 'g' ? "g" : "G";
                    var s = val.ToString(fmt + prec);
                    if (forceSign && val >= 0) return "+" + s;
                    if (spaceSign && val >= 0) return " " + s;
                    return s;
                }
            case 'c':
                {
                    if (arg is char c) return c.ToString();
                    return ((char)Convert.ToInt32(arg ?? 0)).ToString();
                }
            case 's':
                {
                    var s = arg?.ToString() ?? string.Empty;
                    if (precision >= 0 && s.Length > precision)
                    {
                        s = s[..precision];
                    }
                    return s;
                }
            default:
                return "%" + specifier;
        }
    }

    private static bool IsNumericSpecifier(char specifier) =>
        specifier is 'd' or 'i' or 'u' or 'f' or 'F' or 'e' or 'E' or 'g' or 'G' or 'x' or 'X' or 'o';
}
