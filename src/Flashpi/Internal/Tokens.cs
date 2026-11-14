using System;
using System.Threading;

namespace Flashpi.Internal;

/// <summary>Cancellation tokens of lists, which combine the list's token with each call's.</summary>
internal static class Tokens
{
    /// <summary>
    /// A token cancelled by either one, and what to dispose once it is no longer needed (null when
    /// one of them alone does).
    /// </summary>
    internal static IDisposable? Link(CancellationToken first, CancellationToken second, out CancellationToken token)
    {
        if (!first.CanBeCanceled || first == second)
        {
            token = second;
            return null;
        }
        if (!second.CanBeCanceled)
        {
            token = first;
            return null;
        }
        var linked = CancellationTokenSource.CreateLinkedTokenSource(first, second);
        token = linked.Token;
        return linked;
    }
}
