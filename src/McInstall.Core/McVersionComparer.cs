namespace McInstall.Core;

/// <summary>对 1.2.3 / 47.1.106 / 20.4.237-beta 这类版本号做「按数字段」比较。</summary>
public sealed class McVersionComparer : IComparer<string>
{
    public static McVersionComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var left = Split(x);
        var right = Split(y);
        var length = Math.Max(left.Count, right.Count);

        for (var i = 0; i < length; i++)
        {
            var a = i < left.Count ? left[i] : "0";
            var b = i < right.Count ? right[i] : "0";

            var aNumber = int.TryParse(a, out var an);
            var bNumber = int.TryParse(b, out var bn);

            int result;
            if (aNumber && bNumber) result = an.CompareTo(bn);
            else if (aNumber) result = 1;      // 数字段 > 字母段（1.0.0 > 1.0.0-beta）
            else if (bNumber) result = -1;
            else result = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

            if (result != 0) return result;
        }
        return 0;
    }

    private static List<string> Split(string value)
        => value.Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries).ToList();
}
