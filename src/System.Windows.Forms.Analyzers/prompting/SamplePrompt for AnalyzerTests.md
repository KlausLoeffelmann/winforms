Request a Bug Fix:
------------------

Could you update the #AvoidPassingTaskWithoutCancellationToken analyzer so that, in both C# and VB, 
it not only flags explicit `Control.InvokeAsync(...)` and `this.InvokeAsync(...)` (or `Me.InvokeAsync`), 
but also implicit `InvokeAsync(...)` calls inside a WinForms Control subclass when no 
CancellationToken is supplied? Please take the #Analyzer-Copilot-Instructions.md instructions for
fixing bugs in analyzers into account.

Request Tests for the Bug Fix:
--------------------------------------------------
Could you add or update unit tests for the `AvoidPassingTaskWithoutCancellationToken` analyzer in C# and VB to 
ensure it now flags plain InvokeAsync(...) without a `CancellationToken` inside a Control subclass, 
while still ensuring the existing correct behavior of `Control.InvokeAsync(...)`, `this.InvokeAsync(...)`,
`and Me.InvokeAsync(...)` remains unchanged? 
Please take the #Analyzer-Copilot-Instructions.md instructions for writing tests into account.
