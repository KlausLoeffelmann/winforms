using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CSharpControls;

public class MyCustomControl : Control
{
    public void TestImplicitInvokeAsyncCalls()
    {
        // A sync Action delegate is fine
        var okAction = new Action(() => Text = "Hello, World!");
        
        // A sync Func delegate is also fine
        var okFunc = new Func<int>(() => 42);

        // Task-returning delegate without CancellationToken - should trigger warning
        var notOkAsyncFunc = new Func<Task>(() =>
        {
            Text = "Hello, World!";
            return Task.CompletedTask;
        });

        // This should be flagged as it's using an implicit InvokeAsync call with a Task-returning delegate
        var task1 = InvokeAsync(notOkAsyncFunc, CancellationToken.None);

        // TaskT<T>-returning delegate without CancellationToken - should trigger warning  
        var notOkAsyncFunc2 = new Func<Task<int>>(() =>
        {
            Text = "Hello, World!";
            return Task.FromResult(42);
        });

        // This should be flagged as it's using an implicit InvokeAsync call with a Task<T>-returning delegate
        var task2 = InvokeAsync(notOkAsyncFunc2, CancellationToken.None);

        // ValueTask-returning delegate without CancellationToken - should trigger warning
        var notOkAsyncFunc3 = new Func<ValueTask>(() =>
        {
            Text = "Hello, World!";
            return ValueTask.CompletedTask;
        });

        // This should be flagged as it's using an implicit InvokeAsync call with a ValueTask-returning delegate
        var task3 = InvokeAsync(notOkAsyncFunc3, CancellationToken.None);

        // Should have generated: This is OK, since we're passing a cancellation token.
        var okAsyncFunc = new Func<CancellationToken, ValueTask>((cancellation) =>
        {
            Text = "Hello, World!";
            return ValueTask.CompletedTask;
        });

        //// Had generated: A proper implementation with CancellationToken - should not trigger warning
        //var okAsyncFunc = new Func<CancellationToken, Task>((ct) =>
        //{
        //    Text = "Hello, World!";
        //    return Task.CompletedTask;
        //});

        // This should be fine as it properly handles CancellationToken
        var task4 = InvokeAsync(okAsyncFunc, CancellationToken.None);

        // Another proper implementation with CancellationToken - should not trigger warning
        var okAsyncFunc2 = new Func<CancellationToken, ValueTask<int>>((ct) =>
        {
            Text = "Hello, World!";
            return ValueTask.FromResult(42);
        });

        // This should be fine as it properly handles CancellationToken
        var task5 = InvokeAsync(okAsyncFunc2, CancellationToken.None);

        // Finally, let's make sure we don't create false positives for non-InvokeAsync methods
        var result = SomeOtherMethod(() => Task.CompletedTask, CancellationToken.None);
    }

    // This is just to verify we don't create false positives
    private Task SomeOtherMethod(Func<Task> func, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public static class Program
{
    public static void Main()
    {
        var control = new MyCustomControl();
        control.TestImplicitInvokeAsyncCalls();
    }
}
