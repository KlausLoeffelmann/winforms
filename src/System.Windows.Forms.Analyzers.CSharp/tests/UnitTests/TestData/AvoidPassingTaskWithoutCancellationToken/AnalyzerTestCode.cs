﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CSharpControls;

public static class Program
{
    public static void Main()
    {
        var control = new AsyncControl();

        // A sync Action delegate is always fine.
        var okAction = new Action(() => control.Text = "Hello, World!");

        // A sync Func delegate is also fine.
        var okFunc = new Func<int>(() => 42);

        // Just a Task we will get in trouble since it's handled as a fire and forget.
        var notOkAsyncFunc = new Func<Task>(() =>
        {
            control.Text = "Hello, World!";
            return Task.CompletedTask;
        });

        // A Task returning a value will also get us in trouble since it's handled as a fire and forget.
        var notOkAsyncFunc2 = new Func<Task<int>>(() =>
        {
            control.Text = "Hello, World!";
            return Task.FromResult(42);
        });

        // OK.
        var task1 = control.InvokeAsync(okAction);

        // Also OK.
        var task2 = control.InvokeAsync(okFunc);

        // Concerning. - Most likely fire and forget by accident. We should warn about this.
        var task3 = control.InvokeAsync(notOkAsyncFunc, System.Threading.CancellationToken.None);

        // Again: Concerning. - Most likely fire and forget by accident. We should warn about this.
        var task4 = control.InvokeAsync(notOkAsyncFunc, System.Threading.CancellationToken.None);

        // And again concerning. - We should warn about this, too.
        var task5 = control.InvokeAsync(notOkAsyncFunc2, System.Threading.CancellationToken.None);

        // This is OK, since we're passing a cancellation token.
        var okAsyncFunc = new Func<CancellationToken, ValueTask>((cancellation) =>
        {
            control.Text = "Hello, World!";
            return ValueTask.CompletedTask;
        });

        // This is also OK, again, because we're passing a cancellation token.
        var okAsyncFunc2 = new Func<CancellationToken, ValueTask<int>>((cancellation) =>
        {
            control.Text = "Hello, World!";
            return ValueTask.FromResult(42);
        });

        // And let's test that, too:
        var task6 = control.InvokeAsync(okAsyncFunc, System.Threading.CancellationToken.None);

        // And that, too:
        var task7 = control.InvokeAsync(okAsyncFunc2, System.Threading.CancellationToken.None);
    }
}