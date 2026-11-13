using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// A key of the reactive context (design §2.13): a value provided around a body is found by everything made inside that body,
/// through the scopes, nearest first.
/// </summary>
/// <typeparam name="T">The type of the value. A value that changes is an <see cref="IReadOnlySignal{T}"/>.</typeparam>
public sealed class Context<T> : IContextStore
{
    private readonly bool _hasDefault;
    private readonly T _default;
    private readonly List<int> _free = [];
    private T[] _values = [];
    private int _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="Context{T}"/> class with no default value.
    /// </summary>
    public Context() => _default = default!;

    /// <summary>
    /// Initializes a new instance of the <see cref="Context{T}"/> class with a default value.
    /// </summary>
    /// <param name="defaultValue">The value <see cref="Use"/> returns where no value is provided.</param>
    public Context(T defaultValue)
    {
        _default = defaultValue;
        _hasDefault = true;
    }

    /// <summary>
    /// Runs <paramref name="body"/> in a new scope that holds <paramref name="value"/> and returns the element it made.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="body">The code that makes the elements that find the value.</param>
    /// <param name="owner">The name <see cref="ScopeTracker"/> shows for the scope; the caller's name by default.</param>
    /// <returns>The element <paramref name="body"/> returned.</returns>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, or a computed value is computing.
    /// </exception>
    public Element Provide(T value, Func<Element> body, [CallerMemberName] string owner = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        ScopeRef scope = Scope.AllocateOwned(owner);
        int slot = Store(value);
        Scope.PutContext(scope, this, slot);
        ScopeRef previous = Scope.CurrentRef;
        Scope.CurrentRef = scope;
        try
        {
            return body();
        }
        catch (Exception bodyError) when (bodyError is not InvariantViolationException)
        {
            Scope.CurrentRef = previous;
            Scope.DisposeAfterBodyThrew(scope, bodyError);
            throw;
        }
        finally
        {
            Scope.CurrentRef = previous;
        }
    }

    /// <summary>
    /// Returns the nearest provided value, or the default value of the key.
    /// </summary>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">
    /// No value is provided and the key has no default; or there is no current scope; or a computed value is computing; or the
    /// calling thread is not the UI thread.
    /// </exception>
    public T Use()
    {
        if (!TryUse(out T? value))
        {
            ThrowHelper.ThrowContextHasNoValue();
        }

        return value;
    }

    /// <summary>
    /// Finds the nearest provided value, or the default value of the key.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="false"/> when no value is provided and the key has no default.</returns>
    /// <exception cref="InvalidOperationException">
    /// There is no current scope; or a computed value is computing; or the calling thread is not the UI thread.
    /// </exception>
    public bool TryUse([MaybeNullWhen(false)] out T value)
    {
        if (Scope.FindContext(this, out int slot))
        {
            value = _values[slot];
            return true;
        }

        value = _default;
        return _hasDefault;
    }

    /// <inheritdoc/>
    void IContextStore.Release(int slot)
    {
        _values[slot] = default!;
        _free.Add(slot);
    }

    private int Store(T value)
    {
        int slot;
        if (_free.Count > 0)
        {
            slot = _free[^1];
            _free.RemoveAt(_free.Count - 1);
        }
        else
        {
            if (_count == _values.Length)
            {
                Array.Resize(ref _values, Math.Max(4, _count * 2));
            }

            slot = _count++;
        }

        _values[slot] = value;
        return slot;
    }
}
