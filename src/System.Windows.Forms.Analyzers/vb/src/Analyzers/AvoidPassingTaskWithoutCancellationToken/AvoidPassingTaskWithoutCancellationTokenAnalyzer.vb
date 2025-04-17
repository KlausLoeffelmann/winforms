' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.CodeAnalysis.Diagnostics

Namespace Global.System.Windows.Forms.VisualBasic.Analyzers.AvoidPassingTaskWithoutCancellationToken

    <DiagnosticAnalyzer(LanguageNames.VisualBasic)>
    Public Class AvoidPassingTaskWithoutCancellationTokenAnalyzer
        Inherits DiagnosticAnalyzer

        Private Const InvokeAsyncString As String = "InvokeAsync"
        Private Const TaskString As String = "Task"
        Private Const ValueTaskString As String = "ValueTask"

        Public Overrides ReadOnly Property SupportedDiagnostics As ImmutableArray(Of DiagnosticDescriptor)
            Get
                Return ImmutableArray.Create(s_avoidFuncReturningTaskWithoutCancellationToken)
            End Get
        End Property

        Public Overrides Sub Initialize(context As AnalysisContext)
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)
            context.EnableConcurrentExecution()
            context.RegisterSyntaxNodeAction(AddressOf AnalyzeInvocation, SyntaxKind.InvocationExpression)
        End Sub

        Private Sub AnalyzeInvocation(context As SyntaxNodeAnalysisContext)
            Dim invocationExpr = DirectCast(context.Node, InvocationExpressionSyntax)
            Dim methodSymbol As IMethodSymbol = Nothing
            Dim targetExpression As ExpressionSyntax = Nothing

            ' Handle both cases: explicit member access (Me.InvokeAsync/Control.InvokeAsync) and implicit calls (InvokeAsync)
            If TypeOf invocationExpr.Expression Is MemberAccessExpressionSyntax Then
                ' Explicit case: obj.InvokeAsync(...)
                Dim memberAccessExpr = DirectCast(invocationExpr.Expression, MemberAccessExpressionSyntax)
                methodSymbol = TryCast(context.SemanticModel.GetSymbolInfo(memberAccessExpr).Symbol, IMethodSymbol)
                targetExpression = memberAccessExpr.Expression
            ElseIf TypeOf invocationExpr.Expression Is IdentifierNameSyntax Then
                ' Implicit case: InvokeAsync(...)
                Dim identifierNameSyntax = DirectCast(invocationExpr.Expression, IdentifierNameSyntax)
                methodSymbol = TryCast(context.SemanticModel.GetSymbolInfo(identifierNameSyntax).Symbol, IMethodSymbol)

                ' For implicit calls, we need to check if we're in a Control-derived class
                If methodSymbol IsNot Nothing AndAlso methodSymbol.Name = InvokeAsyncString AndAlso methodSymbol.Parameters.Length = 2 Then
                    ' Get the containing type of the current method
                    Dim containingType = context.SemanticModel.GetEnclosingSymbol(invocationExpr.SpanStart)?.ContainingType

                    ' Only proceed if we're in a Control-derived class
                    If containingType IsNot Nothing AndAlso Not IsAncestorOrSelfOfType(containingType, "System.Windows.Forms.Control") Then
                        Return
                    End If
                End If
            Else
                Return
            End If

            If methodSymbol Is Nothing OrElse methodSymbol.Name <> InvokeAsyncString OrElse methodSymbol.Parameters.Length <> 2 Then
                Return
            End If

            Dim funcParameter As IParameterSymbol = methodSymbol.Parameters(0)

            ' If the function delegate has a parameter (which makes then 2 type arguments),
            ' we can safely assume it's a CancellationToken, otherwise the compiler would have
            ' complained before, because this is the only overload type we're accepting in a
            ' func as a passed parameter.
            If Not (TypeOf funcParameter.Type Is INamedTypeSymbol) Then
                Return
            End If

            Dim funcType = DirectCast(funcParameter.Type, INamedTypeSymbol)

            If funcType.TypeArguments.Length <> 1 OrElse funcType.ContainingNamespace.ToString() <> "System" Then
                Return
            End If

            ' For explicit calls, check if the target is a Control
            If targetExpression IsNot Nothing Then
                Dim objectTypeInfo As TypeInfo = context.SemanticModel.GetTypeInfo(targetExpression)

                ' Let's make absolute clear, we're dealing with InvokeAsync of Control.
                If Not (TypeOf objectTypeInfo.Type Is INamedTypeSymbol) Then
                    Return
                End If

                Dim objectType = DirectCast(objectTypeInfo.Type, INamedTypeSymbol)

                If Not IsAncestorOrSelfOfType(objectType, "System.Windows.Forms.Control") Then
                    Return
                End If
            End If

            ' And finally, let's check if the return type is Task or ValueTask, because those
            ' can become now fire-and-forgets.
            If funcType.DelegateInvokeMethod?.ReturnType IsNot Nothing Then
                Dim returnType = TryCast(funcType.DelegateInvokeMethod.ReturnType, INamedTypeSymbol)

                If returnType IsNot Nothing AndAlso (returnType.Name = TaskString OrElse returnType.Name = ValueTaskString) Then
                    Dim diagnostic As Diagnostic = Diagnostic.Create(
                        s_avoidFuncReturningTaskWithoutCancellationToken,
                        invocationExpr.GetLocation())

                    context.ReportDiagnostic(diagnostic)
                End If
            End If
        End Sub

        ' Helper method to check if a type is of a certain type or a derived type.
        Private Shared Function IsAncestorOrSelfOfType(type As INamedTypeSymbol, typeName As String) As Boolean
            Return type IsNot Nothing AndAlso
                (type.ToString() = typeName OrElse
                IsAncestorOrSelfOfType(type.BaseType, typeName))
        End Function
    End Class
End Namespace
