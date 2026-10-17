namespace XylemUI;

/// <summary>
/// Measures the content of a leaf node (a text node, an image) for its content box (design §4.3). It reads only what is
/// already written on the node, never a signal.
/// </summary>
/// <param name="available">The constraint of the content box: the node's constraint less its padding.</param>
/// <returns>The size of the content.</returns>
internal delegate Size MeasureFunc(AvailableSpace available);
