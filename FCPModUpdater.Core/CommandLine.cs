namespace FCPModUpdater;

public static class CommandLine
{
    /// <summary>Value of <c>--name value</c> or <c>--name=value</c> in <paramref name="args"/>, or null.</summary>
    public static string? FindOption(IReadOnlyList<string> args, params string[] names)
    {
        for (var i = 0; i < args.Count; i++)
        {
            foreach (var name in names)
            {
                if (args[i] == name && i + 1 < args.Count)
                    return args[i + 1];
                if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                    return args[i][(name.Length + 1)..];
            }
        }

        return null;
    }
}
