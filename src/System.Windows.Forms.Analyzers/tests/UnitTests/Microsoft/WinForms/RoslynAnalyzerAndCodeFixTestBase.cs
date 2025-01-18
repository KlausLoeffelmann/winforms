// -------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All Rights Reserved.
// -------------------------------------------------------------------

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Microsoft.WinForms.Test;

public abstract class RoslynAnalyzerAndCodeFixTestBase<TAnalyzer, TVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TVerifier : IVerifier, new()
{
    private string? _codeFixTestCode;
    private string? _fixedCode;
    private string? _analyzerTestCode;
    private string? _globalUsing;
    private IEnumerable<string>? _contextDocuments;

    public async Task EnsureInitializedAsync()
    {
        string analyzerTestFilePath = TestFileLoader.GetTestFilePath(GetType());

        // Do we have a directory for the test?
        if (!Directory.Exists(analyzerTestFilePath))
        {
            throw new DirectoryNotFoundException($"The directory '{analyzerTestFilePath}' does not exist.");
        }

        List<string> contextDocuments = [];

        // Get back all the files in the directory.
        foreach (var fileItem in TestFileLoader.EnumerateEntries(analyzerTestFilePath))
        {
            // Load the file content.
            string currentDocument = await LoadTestFileAsync(fileItem.FilePath)
                .ConfigureAwait(false);

            // Now, let's see what kind of file it is.
            switch (fileItem.FileType)
            {
                case TestFileType.AnalyzerTestCode:
                    _analyzerTestCode = currentDocument;
                    break;

                case TestFileType.CodeFixTestCode:
                    _codeFixTestCode = currentDocument;
                    break;

                case TestFileType.FixedCode:
                    _fixedCode = currentDocument;
                    break;

                case TestFileType.GlobalUsing:
                    _globalUsing = currentDocument;
                    break;

                case TestFileType.AdditionalCodeFile:
                    contextDocuments.Add(currentDocument);

                    break;
            }

            _contextDocuments = contextDocuments;
        }
    }

    protected async Task<CSharpAnalyzerTest<TAnalyzer, TVerifier>> GetAnalyzerTestContextAsync(ReferenceAssemblies referenceAssemblies)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        CSharpAnalyzerTest<TAnalyzer, TVerifier> context
            = new CSharpAnalyzerTest<TAnalyzer, TVerifier>
            {
                // Note: The ProblematicCode includes the expected Diagnostic's span in the areas
                // where the code is enclosed in limiting characters ("[|...|]"),
                // like `public SizeF [|ScaledSize|]`.
                TestCode = _analyzerTestCode ?? throw new ArgumentException("AnalyzerTestCoder.c# could not be found!"),
                TestState =
                {
                    OutputKind = OutputKind.WindowsApplication,
                },
                ReferenceAssemblies = referenceAssemblies
            };

        if (_globalUsing is not null)
        {
            context.TestState.Sources.Add(_globalUsing);
        }

        if (_contextDocuments is not null && _contextDocuments.Any())
        {
            foreach (string contextDocument in _contextDocuments)
            {
                context.TestState.Sources.Add(contextDocument);
            }
        }

        return context;
    }

    protected async Task<CSharpCodeFixTest<TAnalyzer, TCodeFix, TVerifier>> GetCodeFixTestContextAsync<TCodeFix>(ReferenceAssemblies referenceAssemblies, int numberOfFixAllIterations)
        where TCodeFix : CodeFixProvider, new()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        CSharpCodeFixTest<TAnalyzer, TCodeFix, TVerifier> context = new CSharpCodeFixTest<TAnalyzer, TCodeFix, TVerifier>
        {
            TestCode = _codeFixTestCode ?? throw new ArgumentException("CodeFixTestCode.c# could not be found!"),
            FixedCode = _fixedCode ?? throw new ArgumentException("FixedCode.c# could not be found!"),
            TestState =
            {
                OutputKind = OutputKind.WindowsApplication,
            },

            ReferenceAssemblies = referenceAssemblies,
            NumberOfFixAllInDocumentIterations = numberOfFixAllIterations
        };

        if (_globalUsing is not null)
        {
            context.TestState.Sources.Add(_globalUsing);
        }

        if (_contextDocuments is not null && _contextDocuments.Any())
        {
            foreach (string contextDocument in _contextDocuments)
            {
                context.TestState.Sources.Add(contextDocument);
            }
        }

        return context;
    }

    private static async Task<string> LoadTestFileAsync(string name)
    {
        string filePath = TestFileLoader.GetTestFilePath(typeof(TAnalyzer), name);

        string fileContent = await TestFileLoader
            .LoadTestFileAsync(filePath)
            .ConfigureAwait(false);

        return fileContent;
    }
}
