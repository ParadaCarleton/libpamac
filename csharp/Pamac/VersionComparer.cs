using System.Text.RegularExpressions;

namespace Pamac;

/// <summary>Small, dependency-free implementation of the comparison operations used by pacman.</summary>
public static class AlpmVersionComparer
{
    private static readonly Regex DependencyRegex = new(
        @"^(?<name>[^<>=\s]+?)\s*(?:(?<op>>=|<=|=|>|<)\s*(?<version>.+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var leftEpoch = ExtractEpoch(left, out var leftVersion);
        var rightEpoch = ExtractEpoch(right, out var rightVersion);
        var epochResult = leftEpoch.CompareTo(rightEpoch);
        if (epochResult != 0) return epochResult;

        // Pacman compares the upstream version and then the package release. This
        // token comparison covers the numeric and alpha portions of Arch package
        // versions without bringing a native libalpm dependency into the library.
        var leftRelease = SplitRelease(leftVersion, out var leftUpstream);
        var rightRelease = SplitRelease(rightVersion, out var rightUpstream);
        var upstreamResult = ComparePart(leftUpstream, rightUpstream);
        return upstreamResult != 0 ? upstreamResult : ComparePart(leftRelease, rightRelease);
    }

    public static bool Satisfies(string version, string dependency)
    {
        var match = DependencyRegex.Match(dependency.Trim());
        if (!match.Success || !match.Groups["op"].Success)
            return string.Equals(GetName(dependency), GetName(version), StringComparison.Ordinal);

        var op = match.Groups["op"].Value;
        var required = match.Groups["version"].Value.Trim();
        var result = Compare(version, required);
        return op switch
        {
            "=" => result == 0,
            ">" => result > 0,
            ">=" => result >= 0,
            "<" => result < 0,
            "<=" => result <= 0,
            _ => false
        };
    }

    public static string GetName(string dependency)
    {
        var match = DependencyRegex.Match(dependency.Trim());
        if (!match.Success)
            return dependency.Trim();

        var name = match.Groups["name"].Value;
        var colon = name.IndexOf(':');
        // Architecture-qualified dependencies use name:architecture. Keep a
        // colon that is part of an unusual package name, but remove known arch
        // suffixes just as libalpm does.
        if (colon > 0 && (name[(colon + 1)..] is "any" or "x86_64" or "i686" or "aarch64"))
            name = name[..colon];
        return name;
    }

    private static long ExtractEpoch(string value, out string remainder)
    {
        var colon = value.IndexOf(':');
        if (colon > 0 && long.TryParse(value[..colon], out var epoch))
        {
            remainder = value[(colon + 1)..];
            return epoch;
        }

        remainder = value;
        return 0;
    }

    private static string SplitRelease(string value, out string upstream)
    {
        var dash = value.LastIndexOf('-');
        if (dash > 0 && dash < value.Length - 1 && value[(dash + 1)..].Any(char.IsDigit))
        {
            upstream = value[..dash];
            return value[(dash + 1)..];
        }

        upstream = value;
        return string.Empty;
    }

    private static int ComparePart(string left, string right)
    {
        var leftTokens = Tokenize(left).ToArray();
        var rightTokens = Tokenize(right).ToArray();
        var count = Math.Max(leftTokens.Length, rightTokens.Length);
        for (var i = 0; i < count; i++)
        {
            if (i == leftTokens.Length) return CompareEmpty(rightTokens[i]);
            if (i == rightTokens.Length) return -CompareEmpty(leftTokens[i]);

            var result = CompareToken(leftTokens[i], rightTokens[i]);
            if (result != 0) return result;
        }

        return 0;
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is '.' or '_' or '+' or '~' or '^')
            {
                if (i > start) yield return value[start..i];
                yield return value[i].ToString();
                start = i + 1;
            }
            else if (i > start && char.IsDigit(value[i]) != char.IsDigit(value[i - 1]))
            {
                yield return value[start..i];
                start = i;
            }
        }

        if (start < value.Length)
            yield return value[start..];
    }

    private static int CompareToken(string left, string right)
    {
        if (left == right) return 0;
        if (left == "~" || right == "~") return left == "~" ? -1 : 1;
        if (left == "^" || right == "^") return left == "^" ? 1 : -1;

        var leftNumeric = left.All(char.IsDigit);
        var rightNumeric = right.All(char.IsDigit);
        if (leftNumeric && rightNumeric)
        {
            left = left.TrimStart('0');
            right = right.TrimStart('0');
            if (left.Length != right.Length) return left.Length.CompareTo(right.Length);
            return string.CompareOrdinal(left, right);
        }
        if (leftNumeric != rightNumeric) return leftNumeric ? 1 : -1;
        return string.CompareOrdinal(left, right);
    }

    private static int CompareEmpty(string token)
        => token == "~" ? 1 : token == "^" ? -1 : -1;
}
