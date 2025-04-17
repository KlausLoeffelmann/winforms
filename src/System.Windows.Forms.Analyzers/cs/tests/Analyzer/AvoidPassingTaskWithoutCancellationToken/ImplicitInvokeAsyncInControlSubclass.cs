// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms.Analyzers.Diagnostics;
using System.Windows.Forms.Analyzers.Tests.Microsoft.WinForms;
using System.Windows.Forms.CSharp.Analyzers.AvoidPassingTaskWithoutCancellationToken;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.WinForms.Test;
using Microsoft.WinForms.Utilities.Shared;

namespace System.Windows.Forms.Analyzers.CSharp.Tests.AnalyzerTests.AvoidPassingTaskWithoutCancellationToken;

/// <summary>
/// Tests for the AvoidPassingTaskWithoutCancellationTokenAnalyzer analyzer
/// focusing on implicit InvokeAsync calls within a Control subclass.
/// </summary>
public class ImplicitInvokeAsyncInControlSubclass
    : RoslynAnalyzerAndCodeFixTestBase<AvoidPassingTaskWithoutCancellationTokenAnalyzer, DefaultVerifier>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImplicitInvokeAsyncInControlSubclass"/> class.
    /// </summary>
    public ImplicitInvokeAsyncInControlSubclass()
        : base(SourceLanguage.CSharp) { }

    /// <summary>
    /// Retrieves reference assemblies for the latest target framework versions.
    /// </summary>
    public static IEnumerable<object[]> GetReferenceAssemblies()
    {
        NetVersion[] tfms =
        [
            NetVersion.Net9_0
        ];

        foreach (ReferenceAssemblies refAssembly in ReferenceAssemblyGenerator.GetForLatestTFMs(tfms))
        {
            yield return new object[] { refAssembly };
        }
    }

    /// <summary>
    /// Tests the diagnostics produced by the analyzer for implicit InvokeAsync calls in a Control subclass.
    /// </summary>
    [Theory]
    [CodeTestData(nameof(GetReferenceAssemblies))]
    public async Task TestImplicitInvokeAsyncCalls(
        ReferenceAssemblies referenceAssemblies,
        TestDataFileSet fileSet)
    {
        // Make sure we can resolve the assembly we're testing against
        var referenceAssembly = await referenceAssemblies.ResolveAsync(
            language: string.Empty,
            cancellationToken: CancellationToken.None);

        string diagnosticId = DiagnosticIDs.AvoidPassingFuncReturningTaskWithoutCancellationToken;

        var context = GetAnalyzerTestContext(fileSet, referenceAssemblies);
        
        // Add expectations for diagnostics in specific locations in the test file
        // context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(25, 13, 25, 67));
        context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(26, 21, 26, 72));
        // context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(38, 13, 38, 67));
        context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(36, 21, 36, 73));
        // context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(51, 13, 51, 67));
        context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(46, 21, 46, 73));

        await context.RunAsync();
    }
}
