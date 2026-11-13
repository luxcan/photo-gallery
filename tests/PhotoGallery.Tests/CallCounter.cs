using System.Collections.Concurrent;
using System.Reflection;

namespace PhotoGallery.Tests;

/// <summary>
/// Passes every call on an interface through to the real thing, counting each
/// by the method's name.
/// </summary>
/// <remarks>
/// For the tests that pin how often a screen asks the library something
/// rather than what it was told: a fake would answer differently from the
/// real query, and the point is that the real query is asked fewer times. A
/// proxy rather than a hand-written wrapper, because the interfaces worth
/// counting have twenty members and a test cares about one.
/// </remarks>
public class CallCounter<T> : DispatchProxy
    where T : class
{
    private T _inner = null!;
    private ConcurrentDictionary<string, int> _calls = null!;

    /// <summary>The real thing, counted into <paramref name="calls"/>.</summary>
    public static T Wrap(T inner, ConcurrentDictionary<string, int> calls)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(calls);

        T proxy = Create<T, CallCounter<T>>();
        var counter = (CallCounter<T>)(object)proxy;
        counter._inner = inner;
        counter._calls = calls;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        _calls.AddOrUpdate(targetMethod.Name, 1, (_, count) => count + 1);

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // What the real method threw, not the reflection wrapped round it,
            // so a test sees the same exception the screen would.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
