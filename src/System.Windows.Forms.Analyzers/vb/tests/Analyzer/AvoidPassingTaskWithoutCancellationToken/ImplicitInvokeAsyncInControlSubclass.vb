' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Windows.Forms.Analyzers.Diagnostics
Imports System.Windows.Forms.Analyzers.Tests.Microsoft.WinForms
Imports System.Windows.Forms.VisualBasic.Analyzers.AvoidPassingTaskWithoutCancellationToken
Imports Microsoft.CodeAnalysis.Testing
Imports Microsoft.WinForms.Test
Imports Microsoft.WinForms.Utilities.Shared
Imports Xunit

Namespace System.Windows.Forms.Analyzers.VisualBasic.Tests.AnalyzerTests.AvoidPassingTaskWithoutCancellationToken

    ''' <summary>
    ''' Tests for the AvoidPassingTaskWithoutCancellationTokenAnalyzer analyzer
    ''' focusing on implicit InvokeAsync calls within a Control subclass.
    ''' </summary>
    Public Class ImplicitInvokeAsyncInControlSubclass
        Inherits RoslynAnalyzerAndCodeFixTestBase(Of AvoidPassingTaskWithoutCancellationTokenAnalyzer, DefaultVerifier)

        ''' <summary>
        ''' Initializes a new instance of the <see cref="ImplicitInvokeAsyncInControlSubclass"/> class.
        ''' </summary>
        Public Sub New()
            MyBase.New(SourceLanguage.VisualBasic)
        End Sub

        ''' <summary>
        ''' Retrieves reference assemblies for the latest target framework versions.
        ''' </summary>
        Public Shared Iterator Function GetReferenceAssemblies() As IEnumerable(Of Object())
            Dim tfms As NetVersion() = {
                NetVersion.Net9_0
            }

            For Each refAssembly In ReferenceAssemblyGenerator.GetForLatestTFMs(tfms)
                Yield New Object() {refAssembly}
            Next
        End Function

        ''' <summary>
        ''' Tests the diagnostics produced by the analyzer for implicit InvokeAsync calls in a Control subclass.
        ''' </summary>
        <Theory>
        <CodeTestData(NameOf(GetReferenceAssemblies))>
        Public Async Function TestImplicitInvokeAsyncCalls(
                referenceAssemblies As ReferenceAssemblies,
                fileSet As TestDataFileSet) As Task

            ' Make sure we can resolve the assembly we're testing against
            Dim referenceAssembly = Await referenceAssemblies.ResolveAsync(
                language:=String.Empty,
                cancellationToken:=CancellationToken.None)

            Dim diagnosticId As String = DiagnosticIDs.AvoidPassingFuncReturningTaskWithoutCancellationToken

            Dim context = GetVisualBasicAnalyzerTestContext(fileSet, referenceAssemblies)

            ' Add expectations for diagnostics in specific locations in the test file
            ' context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(28, 20, 28, 74))
            context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(28, 25, 28, 76))
            ' context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(41, 20, 41, 74))
            context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(37, 25, 37, 77))
            ' context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(54, 20, 54, 74))
            context.ExpectedDiagnostics.Add(DiagnosticResult.CompilerWarning(diagnosticId).WithSpan(46, 25, 46, 77))

            Await context.RunAsync()
        End Function
    End Class

End Namespace
