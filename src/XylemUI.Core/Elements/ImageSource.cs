using System.Reflection;

namespace XylemUI;

/// <summary>
/// Where an image's bytes come from (design §5.0.4, "Sources" and "API"): a file, bytes the app holds, or a resource embedded in an
/// assembly. Nothing is read from the network; an app that fetches an image passes its bytes. Two sources are equal when they name
/// the same file, the same bytes (the same memory), or the same resource, and an image whose source changes to an equal one keeps
/// its image (IM1's Grill, Q20).
/// </summary>
public sealed class ImageSource : IEquatable<ImageSource>
{
    private readonly string? _path;
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly Assembly? _assembly;

    private ImageSource(string? path, ReadOnlyMemory<byte> bytes, Assembly? assembly)
    {
        _path = path;
        _bytes = bytes;
        _assembly = assembly;
    }

    /// <summary>
    /// Returns the source of an image file, read on a worker thread when the image is shown.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    public static ImageSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new ImageSource(path, default, null);
    }

    /// <summary>
    /// Returns the source of an image's bytes. The app does not write them again while an image reads them.
    /// </summary>
    /// <param name="data">The bytes of the image file.</param>
    /// <returns>The source.</returns>
    public static ImageSource FromBytes(ReadOnlyMemory<byte> data) => new(null, data, null);

    /// <summary>
    /// Returns the source of an image embedded in an assembly as a manifest resource.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    /// <param name="name">The resource's name.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static ImageSource FromResource(Assembly assembly, string name)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new ImageSource(name, default, assembly);
    }

    /// <inheritdoc/>
    public bool Equals(ImageSource? other) =>
        other is not null && _path == other._path && ReferenceEquals(_assembly, other._assembly) && _bytes.Equals(other._bytes);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ImageSource);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_path, _assembly, _bytes.Length);

    /// <summary>
    /// Reads the image's bytes, on a worker thread.
    /// </summary>
    /// <returns>The bytes, or <see langword="null"/> when the file or the resource could not be read.</returns>
    internal ReadOnlyMemory<byte>? Read()
    {
        if (_assembly is not null)
        {
            using Stream? stream = _assembly.GetManifestResourceStream(_path!);
            if (stream is null)
            {
                return null;
            }

            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }

        if (_path is not null)
        {
            try
            {
                return File.ReadAllBytes(_path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return null;
            }
        }

        return _bytes;
    }
}
