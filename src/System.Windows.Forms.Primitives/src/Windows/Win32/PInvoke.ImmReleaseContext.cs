// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Windows.Win32;

internal static partial class PInvoke
{
    /// <inheritdoc cref="ImmReleaseContext(HWND, UI.Input.Ime.HIMC)"/>
    public static BOOL ImmReleaseContext<T>(T hWnd, UI.Input.Ime.HIMC hIMC) where T : IHandle<HWND>
    {
        BOOL result = ImmReleaseContext(hWnd.Handle, hIMC);
        GC.KeepAlive(hWnd.Wrapper);
        return result;
    }
}
