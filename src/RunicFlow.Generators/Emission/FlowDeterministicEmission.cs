using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RunicFlow.Generators;

/// <summary>Culture- and machine-independent primitives for generated C# text.</summary>
public static class FlowDeterministicEmission
{
    private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    /// <summary>Normalizes arbitrary UTF-16 text into a stable C# identifier.</summary>
    /// <remarks>Non-ASCII and otherwise invalid code units are encoded as <c>_XXXX</c>.</remarks>
    public static string NormalizeIdentifier(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StringBuilder builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            bool isAsciiLetter = (character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z');
            bool canCopy = isAsciiLetter || character == '_' ||
                (index > 0 && character >= '0' && character <= '9');
            if (canCopy)
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
                builder.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        if (builder.Length == 0)
        {
            builder.Append('_');
        }

        string identifier = builder.ToString();
        return Keywords.Contains(identifier) ? "_" + identifier : identifier;
    }

    /// <summary>Creates the stable hint name for a generated module.</summary>
    public static string CreateHintName(string fullyQualifiedModuleName) =>
        NormalizeIdentifier(fullyQualifiedModuleName) + ".g.cs";

    /// <summary>Normalizes generated text to line-feed endings and one final line feed.</summary>
    public static string NormalizeSourceText(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        string normalized = sourceText.Replace("\r\n", "\n").Replace('\r', '\n');
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }

    /// <summary>Encodes a value as a deterministic C# string literal.</summary>
    public static string ToStringLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StringBuilder builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (character < ' ' || character > '~')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
