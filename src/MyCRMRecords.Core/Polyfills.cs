#if NETFRAMEWORK
// Lets C# 9+ records and init-only setters compile for .NET Framework.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit;
}
#endif
