using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AdLib.IO.Files;

public static class AsyncFiles
{
    /// <summary>
    ///     Asynchronously enumerates files in a directory.
    /// </summary>
    /// <param name="path">The path to search</param>
    /// <param name="searchPattern">The pattern to filter the results by (default *)</param>
    /// <param name="searchOption">
    ///     An option to control which directories are searched (default
    ///     <c>SearchOption.TopDirectoryOnly</c>
    /// </param>
    /// <param name="cancellationToken">The token to cancel this particular request</param>
    /// <returns>An async enumerable which yields the files in a given directory</returns>
    public static async IAsyncEnumerable<string> OrderedEnumerateFilesAsync(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        IEnumerable<string> files = await Task.Run(
            () => Directory.EnumerateFiles(path, searchPattern, searchOption)
                           .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase),
            cancellationToken
        );

        foreach (string file in files)
        {
            yield return file;
        }
    }

    /// <summary>
    ///     Asynchronously enumerates then sorts subdirectories in a directory.
    /// </summary>
    /// <param name="path">The path to search</param>
    /// <param name="searchPattern">The pattern to filter the results by (default *)</param>
    /// <param name="searchOption">
    ///     An option to control which directories are searched (default
    ///     <c>SearchOption.TopDirectoryOnly</c>
    /// </param>
    /// <param name="cancellationToken">The token to cancel this particular request</param>
    /// <returns>An async enumerable which yields the subdirectories in a given directory</returns>
    public static async IAsyncEnumerable<string> OrderedEnumerateDirectoriesAsync(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        IEnumerable<string> dirs = await Task.Run(
            () => Directory.EnumerateDirectories(path, searchPattern, searchOption)
                           .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase),
            cancellationToken
        );

        foreach (string file in dirs)
        {
            yield return file;
        }
    }
}
