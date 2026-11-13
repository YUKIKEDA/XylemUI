namespace XylemUI;

/// <summary>
/// What the keymap says at a node of a tree, apart from a key that goes down (design §7, "Shortcuts and actions"): the contexts on
/// the route to the node, and the row that asks for an action there.
/// </summary>
internal static class KeymapLookup
{
    private static readonly KeyContext Window = KeyContext.Parse(CoreKeyBindings.WindowContext);

    /// <summary>
    /// Collects the contexts of the nodes from the root to a node, on the route a key takes, after the tree's own context
    /// (<see cref="CoreKeyBindings.WindowContext"/>), which is the first.
    /// </summary>
    /// <param name="tree">The tree.</param>
    /// <param name="target">The node the route starts from.</param>
    /// <param name="contexts">Where the contexts go; replaced by a longer array when it is too short.</param>
    /// <returns>The number of contexts.</returns>
    public static int CollectContexts(UiTree tree, NodeHandle target, ref KeyContext[] contexts)
    {
        // Filled from the target up and turned around: the tree's own context is the last here and the first after.
        int count = 0;
        for (NodeHandle node = target; !node.IsNone && tree.Nodes.IsAlive(node); node = tree.KeyParent(node))
        {
            if (tree.Nodes.GetRef(node).Input?.KeyContext is not { } context)
            {
                continue;
            }

            if (count == contexts.Length)
            {
                Array.Resize(ref contexts, count * 2);
            }

            contexts[count++] = context;
        }

        if (count == contexts.Length)
        {
            Array.Resize(ref contexts, count * 2);
        }

        contexts[count++] = Window;
        contexts.AsSpan(0, count).Reverse();
        return count;
    }

    /// <summary>
    /// Finds the row whose keys ask for an action of a type at the node the keys are routed from now: its condition holds there,
    /// and no row with the same keys wins over it, a row with no action or with another action included. Among several, the one
    /// that wins the others is returned. It is the row a key would find, so what a menu shows is what the keys do. Whether a
    /// node handles a row's action is not asked (PR #811's review): a key goes on to the next row when nothing handles the
    /// winning one's, and that next row is not returned here.
    /// </summary>
    /// <param name="tree">The tree.</param>
    /// <param name="from">The node the keys are routed from.</param>
    /// <param name="actionType">The type of the action.</param>
    /// <param name="value">The action a row's action is to be equal to, when the type compares values; <see langword="null"/> for the type alone.</param>
    /// <returns>The row; <see langword="null"/> when no keys ask for the action there.</returns>
    public static KeyBinding? BindingFor(UiTree tree, NodeHandle from, Type actionType, IAction? value = null)
    {
        KeyBinding[] rows = tree.Keymap.Rows;
        if (rows.Length == 0)
        {
            return null;
        }

        ref KeyContext[] contexts = ref tree.Actions.ReadContexts;
        int count = CollectContexts(tree, from, ref contexts);
        ReadOnlySpan<KeyContext> stack = contexts.AsSpan(0, count);
        KeyBinding? best = null;
        int bestDepth = 0;
        foreach (KeyBinding row in rows)
        {
            if (row.Action?.GetType() != actionType || !SameValue(row.Action, value) || !Applies(row, stack, out int depth))
            {
                continue;
            }

            if (best is not null && !Wins(depth, row.Order, bestDepth, best.Order))
            {
                continue;
            }

            bool shadowed = false;
            foreach (KeyBinding other in rows)
            {
                if (
                    !ReferenceEquals(other, row)
                    && SameKeys(other, row)
                    && Applies(other, stack, out int otherDepth)
                    && Wins(otherDepth, other.Order, depth, row.Order)
                )
                {
                    shadowed = true;
                    break;
                }
            }

            if (!shadowed)
            {
                best = row;
                bestDepth = depth;
            }
        }

        return best;
    }

    // Whether a row's condition holds for the stack, and how deep: a row with no condition counts as the deepest context.
    private static bool Applies(KeyBinding row, ReadOnlySpan<KeyContext> stack, out int depth)
    {
        depth = stack.Length;
        if (row.Predicate is not { } predicate)
        {
            return true;
        }

        depth = predicate.Depth(stack);
        return depth > 0;
    }

    // The order of the matching: the deeper condition, then the row added later.
    private static bool Wins(int depth, int order, int otherDepth, int otherOrder) =>
        depth > otherDepth || (depth == otherDepth && order > otherOrder);

    // Whether a row's action is the one asked for: of the type alone when no value was given, and with the same arguments
    // otherwise, as the type's generated comparison says (ActionRegistry.Compare).
    private static bool SameValue(IAction action, IAction? value) => value is null || ActionRegistry.SameArguments(action, value);

    private static bool SameKeys(KeyBinding left, KeyBinding right) => left.Steps.AsSpan().SequenceEqual(right.Steps);
}
