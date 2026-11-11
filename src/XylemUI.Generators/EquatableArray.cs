using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace XylemUI.Generators;

/// <summary>
/// An immutable array that compares by its items, so that a step of an incremental generator whose items did not change is not
/// run again.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="EquatableArray{T}"/> struct.
    /// </summary>
    /// <param name="items">The items.</param>
    public EquatableArray(ImmutableArray<T> items) => _items = items;

    /// <summary>
    /// Gets the number of items.
    /// </summary>
    public int Count => _items.IsDefault ? 0 : _items.Length;

    /// <inheritdoc/>
    public bool Equals(EquatableArray<T> other)
    {
        if (Count != other.Count)
        {
            return false;
        }

        for (int i = 0; i < Count; i++)
        {
            if (!_items[i].Equals(other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 17;
        for (int i = 0; i < Count; i++)
        {
            hash = unchecked((hash * 31) + _items[i].GetHashCode());
        }

        return hash;
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items.IsDefault ? ImmutableArray<T>.Empty : _items)).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A source location kept by value: a <see cref="Location"/> holds the syntax tree, which would keep a step of an incremental
/// generator from comparing equal.
/// </summary>
/// <param name="Path">The file.</param>
/// <param name="Span">The span of the text.</param>
/// <param name="Lines">The span in lines.</param>
internal sealed record LocationInfo(string Path, TextSpan Span, LinePositionSpan Lines)
{
    /// <summary>
    /// Takes the values of a location.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <returns>The values; <see langword="null"/> when the location is not in a source file.</returns>
    public static LocationInfo? From(Location location) =>
        location.SourceTree is null ? null : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);

    /// <summary>
    /// Makes the location again.
    /// </summary>
    /// <returns>The location.</returns>
    public Location ToLocation() => Location.Create(Path, Span, Lines);
}
