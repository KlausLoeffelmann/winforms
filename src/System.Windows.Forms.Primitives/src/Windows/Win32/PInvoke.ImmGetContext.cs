// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Windows.Win32;

internal static partial class PInvoke
{
    /// <inheritdoc cref="ImmGetContext(HWND)"/>
    public static UI.Input.Ime.HIMC ImmGetContext<T>(T hWnd) where T : IHandle<HWND>
    {
        UI.Input.Ime.HIMC result = ImmGetContext(hWnd.Handle);
        GC.KeepAlive(hWnd.Wrapper);
        return result;
    }
}
