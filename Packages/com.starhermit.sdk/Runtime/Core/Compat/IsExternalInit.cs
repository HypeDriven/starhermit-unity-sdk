#if !NET5_0_OR_GREATER
using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Compiler shim that enables C# 9 <c>init</c> accessors on .NET Standard 2.1, which is the API
    /// compatibility level Unity uses. Never referenced directly.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif
