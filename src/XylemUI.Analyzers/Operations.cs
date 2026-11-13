using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace XylemUI.Analyzers;

/// <summary>
/// Walks the operations of one function.
/// </summary>
internal static class Operations
{
    /// <summary>
    /// Returns the operations under <paramref name="root"/>, the root included, without those inside a lambda, an anonymous method,
    /// or a local function under it: those run when they are called, not where they are written.
    /// </summary>
    /// <param name="root">The operation to start from.</param>
    /// <returns>The operations, parents before their children.</returns>
    internal static IEnumerable<IOperation> OfFunction(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            IOperation operation = pending.Pop();
            yield return operation;
            foreach (IOperation child in operation.ChildOperations)
            {
                if (child is not IAnonymousFunctionOperation and not ILocalFunctionOperation)
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>
    /// Returns the value an argument was given, without the conversions the compiler added around it.
    /// </summary>
    /// <param name="operation">The argument's value.</param>
    /// <returns>The operation inside the conversions.</returns>
    internal static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation { IsImplicit: true } conversion:
                    operation = conversion.Operand;
                    break;
                case IDelegateCreationOperation creation:
                    operation = creation.Target;
                    break;
                default:
                    return operation;
            }
        }
    }
}
