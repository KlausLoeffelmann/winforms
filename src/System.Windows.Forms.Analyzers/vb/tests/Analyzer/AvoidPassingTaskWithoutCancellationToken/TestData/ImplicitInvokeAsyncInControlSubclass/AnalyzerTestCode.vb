Option Strict On
Option Explicit On

Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Namespace VisualBasicControls

    Public Class MyCustomControl
        Inherits Control

        Public Sub TestImplicitInvokeAsyncCalls()
            ' A sync Action delegate is fine
            Dim okAction As New Action(Sub() Text = "Hello, World!")

            ' A sync Func delegate is also fine
            Dim okFunc As New Func(Of Integer)(Function() 42)

            ' Task-returning delegate without CancellationToken - should trigger warning
            Dim notOkAsyncFunc As New Func(Of Task)(Function()
                                                        Text = "Hello, World!"
                                                        Return Task.CompletedTask
                                                    End Function)

            ' This should be flagged as it's using an implicit InvokeAsync call with a Task-returning delegate
            Dim task1 = InvokeAsync(notOkAsyncFunc, CancellationToken.None)

            ' Task<T>-returning delegate without CancellationToken - should trigger warning  
            Dim notOkAsyncFunc2 As New Func(Of Task(Of Integer))(Function()
                                                                     Text = "Hello, World!"
                                                                     Return Task.FromResult(42)
                                                                 End Function)

            ' This should be flagged as it's using an implicit InvokeAsync call with a Task<T>-returning delegate
            Dim task2 = InvokeAsync(notOkAsyncFunc2, CancellationToken.None)

            ' ValueTask-returning delegate without CancellationToken - should trigger warning
            Dim notOkAsyncFunc3 As New Func(Of ValueTask)(Function()
                                                              Text = "Hello, World!"
                                                              Return ValueTask.CompletedTask
                                                          End Function)

            ' This should be flagged as it's using an implicit InvokeAsync call with a ValueTask-returning delegate
            Dim task3 = InvokeAsync(notOkAsyncFunc3, CancellationToken.None)

            ' A proper implementation with CancellationToken - should not trigger warning
            Dim okAsyncFunc As New Func(Of CancellationToken, ValueTask)(Function(ct)
                                                                             Text = "Hello, World!"
                                                                             Return ValueTask.CompletedTask
                                                                         End Function)

            ' This should be fine as it properly handles CancellationToken
            Dim task4 = InvokeAsync(okAsyncFunc, CancellationToken.None)

            ' Another proper implementation with CancellationToken - should not trigger warning
            Dim okAsyncFunc2 As New Func(Of CancellationToken, ValueTask(Of Integer))(Function(ct)
                                                                                          Text = "Hello, World!"
                                                                                          Return ValueTask.FromResult(42)
                                                                                      End Function)

            ' This should be fine as it properly handles CancellationToken
            Dim task5 = InvokeAsync(okAsyncFunc2, CancellationToken.None)

            ' Finally, let's make sure we don't create false positives for non-InvokeAsync methods
            Dim result = SomeOtherMethod(Function() Task.CompletedTask, CancellationToken.None)
        End Sub

        ' This is just to verify we don't create false positives
        Private Function SomeOtherMethod(func As Func(Of Task), ct As CancellationToken) As Task
            Return Task.CompletedTask
        End Function
    End Class

    Public Module Program
        Public Sub Main()
            Dim control As New MyCustomControl()
            control.TestImplicitInvokeAsyncCalls()
        End Sub
    End Module

End Namespace
