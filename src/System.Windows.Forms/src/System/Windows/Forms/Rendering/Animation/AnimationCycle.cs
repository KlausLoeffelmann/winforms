// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Rendering.Animation;

internal enum AnimationCycle
{
    /// <summary>
    ///  Animation cycle is one time, then stops.
    /// </summary>
    Once,

    /// <summary>
    ///  Animation cycle completes, then restarts.
    /// </summary>
    Loop,

    /// <summary>
    ///  Animation cycle completes, then reverses, then restarts.
    /// </summary>
    Bounce
}
