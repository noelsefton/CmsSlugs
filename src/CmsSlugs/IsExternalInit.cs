#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    // Polyfill so `init` accessors and records compile on netstandard2.0.
    internal static class IsExternalInit { }
}
#endif
