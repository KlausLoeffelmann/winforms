// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms.CSharp.Analyzers.MissingPropertySerializationConfiguration;
using System.Windows.Forms.CSharp.CodeFixes.AddDesignerSerializationVisibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.WinForms.Test;

namespace System.Windows.Forms.Analyzers.Test;

public class MissingPropertySerializationConfigurationTests
    : RoslynAnalyzerAndCodeFixTestBase<MissingPropertySerializationConfigurationAnalyzer,
             DefaultVerifier>

{

    // We are testing the analyzer with all versions of the .NET SDK from 6.0 on.
    public static IEnumerable<object[]> GetReferenceAssemblies()
    {
        yield return [ReferenceAssemblies.Net.Net60Windows];
        yield return [ReferenceAssemblies.Net.Net70Windows];
        yield return [ReferenceAssemblies.Net.Net80Windows];
        yield return [ReferenceAssemblies.Net.Net90Windows];
    }

    [Theory]
    [MemberData(nameof(GetReferenceAssemblies))]
    public async Task ControlPropertySerializationConfigurationDiagnosticsEngage(ReferenceAssemblies referenceAssemblies)
    {
        var context = await GetAnalyzerTestContextAsync(referenceAssemblies);
        await context.RunAsync();
    }

    [Theory]
    [MemberData(nameof(GetReferenceAssemblies))]
    public async Task ControlPropertySerializationConfigurationDiagnosticPass(ReferenceAssemblies referenceAssemblies)
    {
        var context=SetContextAsync(
            TestFileType.AnalyzerTestCode |
            TestFileType.GlobalUsing |
            TestFileType.AdditionalCodeFile);

        await context.RunAsync();

        var context = new CSharpAnalyzerTest
            <MissingPropertySerializationConfigurationAnalyzer,
             DefaultVerifier>
        {
            TestCode = AnalyzerTestCode,
            TestState =
                {
                    OutputKind = OutputKind.WindowsApplication,
                },
            ReferenceAssemblies = referenceAssemblies
        };

        context.TestState.Sources.Add(GlobalUsingCode);

        await context.RunAsync();
    }

    [Theory]
    [MemberData(nameof(GetReferenceAssemblies))]
    public async Task AddDesignerSerializationVisibilityCodeFix(ReferenceAssemblies referenceAssemblies, int numberOfFixAllIterations)
    {

        await context.RunAsync();
    }
}
