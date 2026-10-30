using System.ComponentModel;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Internal;
using Xunit.Sdk;
using Xunit.v3;

namespace XylemUI.Testing.XUnit;

/// <summary>
/// A fact whose test class is made, and whose body runs, on the UI thread of <see cref="HeadlessSession"/> (design §12.5,
/// "Threads"), so that it can start a <see cref="HeadlessApp"/> and call it directly. Avalonia's <c>[AvaloniaFact]</c> is the same.
/// </summary>
/// <param name="sourceFilePath">The file of the test; the compiler fills it.</param>
/// <param name="sourceLineNumber">The line of the test; the compiler fills it.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer(typeof(UiFactDiscoverer))]
public sealed class UiFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
    : FactAttribute(sourceFilePath, sourceLineNumber);

/// <summary>
/// A theory whose test class is made, and whose body runs for each row, on the UI thread of <see cref="HeadlessSession"/>.
/// </summary>
/// <param name="sourceFilePath">The file of the test; the compiler fills it.</param>
/// <param name="sourceLineNumber">The line of the test; the compiler fills it.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer(typeof(UiTheoryDiscoverer))]
public sealed class UiTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
    : TheoryAttribute(sourceFilePath, sourceLineNumber);

/// <summary>
/// Makes the test case of a <see cref="UiFactAttribute"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class UiFactDiscoverer : FactDiscoverer
{
    /// <inheritdoc/>
    protected override IXunitTestCase CreateTestCase(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute
    )
    {
        var details = TestIntrospectionHelper.GetTestCaseDetails(discoveryOptions, testMethod, factAttribute);
        return new UiTestCase(
            details.ResolvedTestMethod,
            details.TestCaseDisplayName,
            details.UniqueID,
            details.Explicit,
            details.SkipExceptions,
            details.SkipReason,
            details.SkipType,
            details.SkipUnless,
            details.SkipWhen,
            testMethod.Traits.ToReadWrite(StringComparer.OrdinalIgnoreCase),
            sourceFilePath: details.SourceFilePath,
            sourceLineNumber: details.SourceLineNumber,
            timeout: details.Timeout
        );
    }
}

/// <summary>
/// Makes the test cases of a <see cref="UiTheoryAttribute"/>: one for each row known at discovery, or one that enumerates its
/// rows when it runs.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class UiTheoryDiscoverer : TheoryDiscoverer
{
    /// <inheritdoc/>
    protected override ValueTask<IReadOnlyCollection<IXunitTestCase>> CreateTestCasesForDataRow(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        ITheoryAttribute theoryAttribute,
        ITheoryDataRow dataRow,
        object?[] testMethodArguments,
        string? index
    )
    {
        var details = TestIntrospectionHelper.GetTestCaseDetailsForTheoryDataRow(
            discoveryOptions,
            testMethod,
            theoryAttribute,
            dataRow,
            testMethodArguments,
            index
        );
        var testCase = new UiTestCase(
            details.ResolvedTestMethod,
            details.TestCaseDisplayName,
            details.UniqueID,
            details.Explicit,
            details.SkipExceptions,
            details.SkipReason,
            details.SkipType,
            details.SkipUnless,
            details.SkipWhen,
            TestIntrospectionHelper.GetTraits(testMethod, dataRow),
            testMethodArguments,
            sourceFilePath: details.SourceFilePath,
            sourceLineNumber: details.SourceLineNumber,
            timeout: details.Timeout
        );
        return ValueTask.FromResult<IReadOnlyCollection<IXunitTestCase>>([testCase]);
    }

    /// <inheritdoc/>
    protected override ValueTask<IReadOnlyCollection<IXunitTestCase>> CreateTestCasesForTheory(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        ITheoryAttribute theoryAttribute
    )
    {
        var details = TestIntrospectionHelper.GetTestCaseDetails(discoveryOptions, testMethod, theoryAttribute);
        IXunitTestCase testCase =
            details.SkipReason is not null && details.SkipUnless is null && details.SkipWhen is null
                ? new UiTestCase(
                    details.ResolvedTestMethod,
                    details.TestCaseDisplayName,
                    details.UniqueID,
                    details.Explicit,
                    details.SkipExceptions,
                    details.SkipReason,
                    details.SkipType,
                    details.SkipUnless,
                    details.SkipWhen,
                    testMethod.Traits.ToReadWrite(StringComparer.OrdinalIgnoreCase),
                    sourceFilePath: details.SourceFilePath,
                    sourceLineNumber: details.SourceLineNumber,
                    timeout: details.Timeout
                )
                : new UiDelayEnumeratedTheoryTestCase(
                    details.ResolvedTestMethod,
                    details.TestCaseDisplayName,
                    details.UniqueID,
                    details.Explicit,
                    theoryAttribute.SkipTestWithoutData,
                    details.SkipExceptions,
                    details.SkipReason,
                    details.SkipType,
                    details.SkipUnless,
                    details.SkipWhen,
                    testMethod.Traits.ToReadWrite(StringComparer.OrdinalIgnoreCase),
                    sourceFilePath: details.SourceFilePath,
                    sourceLineNumber: details.SourceLineNumber,
                    timeout: details.Timeout
                );
        return ValueTask.FromResult<IReadOnlyCollection<IXunitTestCase>>([testCase]);
    }
}
