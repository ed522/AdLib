using System.IO;

using Avalonia.Data.Converters;

namespace AdLib.View.Converters;

public static class StringConversions
{
    public static readonly FuncValueConverter<string, string> ToFileName =
        new(path => Path.GetFileName(path) ?? "");
}
