using System.IO.Enumeration;
using System.Text;
using Microsoft.WinForms.Utilities.Shared;

namespace Microsoft.WinForms.Test;

/// <summary>
///  Utility that handles loading of test files from a folder called 'TestData'.
/// </summary>
public static class TestFileLoader
{
    private const string TestData = nameof(TestData);

    private const string AnalyzerTestCode = nameof(TestFileType.AnalyzerTestCode);
    private const string CodeFixTestCode = nameof(TestFileType.CodeFixTestCode);
    private const string FixedCode = nameof(TestFileType.FixedCode);
    private const string GlobalUsing = nameof(TestFileType.GlobalUsing);

    /// <summary>
    ///  Gets the file extension for the specified source language.
    /// </summary>
    /// <param name="language">The source language.</param>
    /// <returns>The file extension for the specified source language.</returns>
    private static string GetExtension(SourceLanguage language)
        => language switch
        {
            SourceLanguage.CSharp => ".cs",
            SourceLanguage.VisualBasic => ".vb",
            _ => ".txt"
        };

    /// <summary>
    ///  Enumerates the test file entries in the specified base path.
    /// </summary>
    /// <param name="basePath">The base path to enumerate.</param>
    /// <param name="excludeAttributes">The file attributes to exclude.</param>
    /// <returns>An enumerable collection of test file entries.</returns>
    /// <exception cref="ArgumentException">Thrown when the base path is null or empty.</exception>
    public static IEnumerable<TestFileEntry> EnumerateEntries(
           string basePath,
           FileAttributes excludeAttributes = default)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            throw new ArgumentException("The base path must be a valid directory.", nameof(basePath));
        }

        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false
        };

        return EnumerateEntriesInternal(basePath, enumOptions, excludeAttributes);

        IEnumerable<TestFileEntry> EnumerateEntriesInternal(
            string basePath,
            EnumerationOptions enumOptions,
            FileAttributes excludeAttributes)
        {
            FileSystemEnumerable<TestFileEntry> enumeration = new FileSystemEnumerable<TestFileEntry>(
                directory: basePath,
                transform: (ref FileSystemEntry entry) =>
                {
                    TestFileType fileType = Path.GetFileName(basePath) switch
                    {
                        AnalyzerTestCode => TestFileType.AnalyzerTestCode,
                        CodeFixTestCode => TestFileType.CodeFixTestCode,
                        FixedCode => TestFileType.FixedCode,
                        GlobalUsing => TestFileType.GlobalUsing,
                        _ => TestFileType.AdditionalCodeFile
                    };

                    return new TestFileEntry(entry.ToFullPath(), fileType);
                },
                options: enumOptions);

            foreach (var fileEntry in enumeration)
            {
                yield return fileEntry;
            }
        }
    }

    /// <summary>
    ///  Asynchronously loads the content of a test file.
    /// </summary>
    /// <param name="testFilePath">The path of the test file to load.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the content of the test file.</returns>
    public static async Task<string> LoadTestFileAsync(string testFilePath)
    {
        using var reader = new StreamReader(testFilePath, Encoding.UTF8);

        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///  Gets the test file path for the specified analyzer type and additional path.
    /// </summary>
    /// <param name="analyzerType">The type of the analyzer.</param>
    /// <param name="additionalPath">The additional path to include in the test file path.</param>
    /// <returns>The test file path.</returns>
    public static string GetTestFilePath(
        Type analyzerType,
        string? additionalPath = default)
    {
        var builder = new StringBuilder();

        builder.Append(TestData);
        builder.Append('\\');

        if (additionalPath is not null)
        {
            builder.Append($"{additionalPath}");
            builder.Append('\\');
        }

        builder.Append(analyzerType.Name);

        return builder.ToString();
    }
}
