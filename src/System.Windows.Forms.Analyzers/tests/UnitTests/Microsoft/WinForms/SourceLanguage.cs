﻿// -------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All Rights Reserved.
// -------------------------------------------------------------------

using System.ComponentModel;

namespace Microsoft.WinForms.Utilities.Shared;

// Note: This is marked as EditorBrowsableState.Never to keep it from appearing in
// the completion list for users of the SDK. However, it will still appear in the
// completion list within the designer repo because of a C# editor feature that always
// shows symbols defined in source, regardless of EditorBrowsableState.

[EditorBrowsable(EditorBrowsableState.Never)]
public enum SourceLanguage
{
    None,
    CSharp,
    VisualBasic
}