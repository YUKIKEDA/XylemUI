using System.Text;

namespace XylemUI;

/// <content>
/// The keymap's part of a key's delivery (design §7, "Shortcuts and actions"): a key that goes down is looked up in the keymap
/// before the key handlers get it, and keys pressed one after another are kept until they are a binding or stop being one.
/// </content>
internal sealed partial class WindowInput
{
    /// <summary>
    /// How long the keys of an unfinished binding are kept before they are delivered as plain keys (GPUI's one second).
    /// </summary>
    internal static readonly TimeSpan PendingKeysDelay = TimeSpan.FromSeconds(1);

    // The keys of a binding that is not finished yet, in the order they went down.
    private KeyInput[] _pendingKeys = new KeyInput[4];
    private int _pendingCount;
    private ITimer? _pendingTimer;
    private SynchronizationContext? _pendingUi;

    // The contexts of the nodes from the root to the key's target, and the rows that match the keys, best first. Kept and used
    // again, so that a key allocates nothing.
    private KeyContext[] _contexts = new KeyContext[8];
    private int _contextCount;
    private KeyBinding[] _matches = new KeyBinding[4];
    private int[] _matchDepths = new int[4];
    private int _matchCount;

    /// <summary>
    /// Gets the number of keys kept for a binding that is not finished yet.
    /// </summary>
    internal int PendingKeyCount => _pendingCount;

    // Looks a key that went down up in the keymap. Returns whether the keymap took it: an action handled it, or it is kept as
    // the start of a longer binding.
    private bool TryKeymap(in KeyInput input)
    {
        if (input.Kind != KeyEventKind.Down || IsModifierKey(input.Key))
        {
            return false;
        }

        if (_pendingCount == 0 && (_tree.Keymap.Rows.Length == 0 || TextTakesKey(input)))
        {
            return false;
        }

        Match(_pendingKeys.AsSpan(0, _pendingCount), input, out bool longer);
        if (longer)
        {
            Keep(input);
            return true;
        }

        if (_pendingCount > 0)
        {
            if (_matchCount == 0)
            {
                // The keys kept are no binding with this one: they are delivered as they would have been, and this key starts
                // again.
                FlushPendingKeys();
                return TryKeymap(input);
            }

            ClearPendingKeys();
        }

        return DispatchMatches();
    }

    // Dispatches the actions of the rows that match, best first, until one is handled. A row with no action ends it: the keys
    // have no binding there.
    private bool DispatchMatches()
    {
        NodeHandle target = _tree.Focus.KeyTarget;

        // A handler may change the keymap or press keys; the rows are copied out of the fields first.
        int count = _matchCount;
        for (int i = 0; i < count; i++)
        {
            if (_matches[i].Action is not { } action)
            {
                return false;
            }

            if (_tree.Actions.Dispatch(target, action))
            {
                return true;
            }

            if (_matchCount != count)
            {
                return false;
            }
        }

        return false;
    }

    // Fills _matches with the rows whose keys are `before` followed by `input` and whose condition holds at the key's target,
    // best first (deeper condition, then added later). `longer` is whether a row has those keys and more after them.
    private void Match(ReadOnlySpan<KeyInput> before, in KeyInput input, out bool longer)
    {
        longer = false;
        _matchCount = 0;
        KeyBinding[] rows = _tree.Keymap.Rows;
        if (rows.Length == 0)
        {
            return;
        }

        CollectContexts(_tree.Focus.KeyTarget);
        ReadOnlySpan<KeyContext> contexts = _contexts.AsSpan(0, _contextCount);
        foreach (KeyBinding row in rows)
        {
            Keystroke[] steps = row.Steps;
            if (steps.Length <= before.Length || !steps[before.Length].Matches(input.Key, input.KeyChar, input.Modifiers))
            {
                continue;
            }

            bool same = true;
            for (int i = 0; i < before.Length && same; i++)
            {
                same = steps[i].Matches(before[i].Key, before[i].KeyChar, before[i].Modifiers);
            }

            if (!same)
            {
                continue;
            }

            // A row with no condition counts as the deepest context there is (GPUI), so that among such rows and the rows
            // whose condition holds at the target itself, the one added later wins.
            int depth = contexts.Length;
            if (row.Predicate is { } predicate)
            {
                depth = predicate.Depth(contexts);
                if (depth == 0)
                {
                    continue;
                }
            }

            if (steps.Length > before.Length + 1)
            {
                longer = true;
                continue;
            }

            AddMatch(row, depth);
        }
    }

    private void AddMatch(KeyBinding row, int depth)
    {
        if (_matchCount == _matches.Length)
        {
            Array.Resize(ref _matches, _matchCount * 2);
            Array.Resize(ref _matchDepths, _matchCount * 2);
        }

        int at = _matchCount;
        while (at > 0 && (_matchDepths[at - 1] < depth || (_matchDepths[at - 1] == depth && _matches[at - 1].Order < row.Order)))
        {
            _matches[at] = _matches[at - 1];
            _matchDepths[at] = _matchDepths[at - 1];
            at--;
        }

        _matches[at] = row;
        _matchDepths[at] = depth;
        _matchCount++;
    }

    // The contexts of the nodes from the root to target, on the route a key takes.
    private void CollectContexts(NodeHandle target)
    {
        _contextCount = 0;
        for (NodeHandle node = target; !node.IsNone && _tree.Nodes.IsAlive(node); node = _tree.KeyParent(node))
        {
            if (_tree.Nodes.GetRef(node).Input?.KeyContext is not { } context)
            {
                continue;
            }

            if (_contextCount == _contexts.Length)
            {
                Array.Resize(ref _contexts, _contextCount * 2);
            }

            _contexts[_contextCount++] = context;
        }

        _contexts.AsSpan(0, _contextCount).Reverse();
    }

    // A key that types a character goes to a field that takes text before the keymap, and so does every key while the input
    // method composes (design §7, "The order of keys and text input"): the keymap is not tried for it.
    private bool TextTakesKey(in KeyInput input)
    {
        FocusManager focus = _tree.Focus;
        if (!focus.IsWindowActive || _tree.TextInputClientOf(focus.Focused) is not { ReadOnly: false } client)
        {
            return false;
        }

        if (client.Composition is not null)
        {
            return true;
        }

        return input.PrefersCharacter || TypesCharacter(input);
    }

    private static bool TypesCharacter(in KeyInput input) =>
        input.KeyChar.Value != 0 && (input.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0;

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    // Keeps a key as the next of an unfinished binding, and starts the wait again.
    private void Keep(in KeyInput input)
    {
        if (_pendingCount == _pendingKeys.Length)
        {
            Array.Resize(ref _pendingKeys, _pendingCount * 2);
        }

        _pendingKeys[_pendingCount++] = input;
        _pendingUi = SynchronizationContext.Current;
        if (_pendingTimer is null)
        {
            _pendingTimer = _tree.Time.CreateTimer(
                static state => ((WindowInput)state!).PendingKeysTick(),
                this,
                PendingKeysDelay,
                Timeout.InfiniteTimeSpan
            );
        }
        else
        {
            _ = _pendingTimer.Change(PendingKeysDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void ClearPendingKeys()
    {
        _pendingCount = 0;
        _ = _pendingTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    // The timer may fire on another thread: its work runs on the UI thread.
    private void PendingKeysTick()
    {
        if (_pendingUi is { } ui && SynchronizationContext.Current != ui)
        {
            ui.Post(static state => ((WindowInput)state!).PendingKeysElapsed(), this);
            return;
        }

        PendingKeysElapsed();
    }

    private void PendingKeysElapsed()
    {
        if (_pendingCount == 0)
        {
            return;
        }

        ReactiveRuntime.EnterBatch();
        try
        {
            FlushPendingKeys();
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }

    // Delivers the keys kept as they would have been with no longer binding: the longest run of them that is a binding asks for
    // its action, and a key that is in none goes to the key handlers and, when it types a character, to the text input.
    private void FlushPendingKeys()
    {
        int count = _pendingCount;
        if (count == 0)
        {
            return;
        }

        // Copied out: delivering a key may keep keys again.
        KeyInput[] keys = _pendingKeys.AsSpan(0, count).ToArray();
        ClearPendingKeys();
        int at = 0;
        while (at < count)
        {
            int taken = 0;
            for (int length = count - at; length > 0 && taken == 0; length--)
            {
                Match(keys.AsSpan(at, length - 1), keys[at + length - 1], out _);
                if (_matchCount > 0 && DispatchMatches())
                {
                    taken = length;
                }
            }

            if (taken > 0)
            {
                at += taken;
                continue;
            }

            KeyInput key = keys[at++];
            if (!DeliverKey(key, altUpAlone: false) && TypesCharacter(key))
            {
                _ = RouteText(_tree.Focus.KeyTarget, new TextInput(key.KeyChar.ToString(), key.Timestamp));
            }
        }
    }
}
