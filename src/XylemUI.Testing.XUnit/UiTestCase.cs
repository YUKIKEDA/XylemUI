using System.ComponentModel;
using Xunit.Sdk;
using Xunit.v3;

namespace XylemUI.Testing.XUnit;

/// <summary>
/// The test case of a <see cref="UiFactAttribute"/>, or of one row of a <see cref="UiTheoryAttribute"/>.
/// </summary>
internal sealed class UiTestCase : XunitTestCase, ISelfExecutingXunitTestCase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UiTestCase"/> class with what discovery found.
    /// </summary>
    /// <param name="testMethod">The method.</param>
    /// <param name="testCaseDisplayName">The display name.</param>
    /// <param name="uniqueID">The unique ID.</param>
    /// <param name="explicit">Whether it runs only when asked for.</param>
    /// <param name="skipExceptions">The exceptions that skip it.</param>
    /// <param name="skipReason">Why it is skipped.</param>
    /// <param name="skipType">The type that holds <paramref name="skipUnless"/> and <paramref name="skipWhen"/>.</param>
    /// <param name="skipUnless">The property that runs it only when true.</param>
    /// <param name="skipWhen">The property that skips it when true.</param>
    /// <param name="traits">Its traits.</param>
    /// <param name="testMethodArguments">The row's arguments.</param>
    /// <param name="sourceFilePath">Its file.</param>
    /// <param name="sourceLineNumber">Its line.</param>
    /// <param name="timeout">Its timeout in milliseconds.</param>
    public UiTestCase(
        IXunitTestMethod testMethod,
        string testCaseDisplayName,
        string uniqueID,
        bool @explicit,
        Type[]? skipExceptions = null,
        string? skipReason = null,
        Type? skipType = null,
        string? skipUnless = null,
        string? skipWhen = null,
        Dictionary<string, HashSet<string>>? traits = null,
        object?[]? testMethodArguments = null,
        string? sourceFilePath = null,
        int? sourceLineNumber = null,
        int? timeout = null
    )
        : base(
            testMethod,
            testCaseDisplayName,
            uniqueID,
            @explicit,
            skipExceptions,
            skipReason,
            skipType,
            skipUnless,
            skipWhen,
            traits,
            testMethodArguments,
            sourceFilePath,
            sourceLineNumber,
            timeout
        ) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UiTestCase"/> class for the deserializer.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Called by the deserializer.")]
    public UiTestCase() { }

    /// <inheritdoc/>
    public ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        ParallelMode parallelMode,
        ExecutionScheduler executionScheduler,
        FixtureMappingManager fixtureMappings
    ) =>
        UiTestCaseRunner.Run(
            this,
            explicitOption,
            messageBus,
            constructorArguments,
            aggregator,
            cancellationTokenSource,
            parallelMode,
            executionScheduler,
            fixtureMappings
        );
}

/// <summary>
/// The test case of a <see cref="UiTheoryAttribute"/> whose rows are enumerated when it runs.
/// </summary>
internal sealed class UiDelayEnumeratedTheoryTestCase : XunitDelayEnumeratedTheoryTestCase, ISelfExecutingXunitTestCase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UiDelayEnumeratedTheoryTestCase"/> class with what discovery found.
    /// </summary>
    /// <param name="testMethod">The method.</param>
    /// <param name="testCaseDisplayName">The display name.</param>
    /// <param name="uniqueID">The unique ID.</param>
    /// <param name="explicit">Whether it runs only when asked for.</param>
    /// <param name="skipTestWithoutData">Whether a theory with no rows is skipped instead of failing.</param>
    /// <param name="skipExceptions">The exceptions that skip it.</param>
    /// <param name="skipReason">Why it is skipped.</param>
    /// <param name="skipType">The type that holds <paramref name="skipUnless"/> and <paramref name="skipWhen"/>.</param>
    /// <param name="skipUnless">The property that runs it only when true.</param>
    /// <param name="skipWhen">The property that skips it when true.</param>
    /// <param name="traits">Its traits.</param>
    /// <param name="sourceFilePath">Its file.</param>
    /// <param name="sourceLineNumber">Its line.</param>
    /// <param name="timeout">Its timeout in milliseconds.</param>
    public UiDelayEnumeratedTheoryTestCase(
        IXunitTestMethod testMethod,
        string testCaseDisplayName,
        string uniqueID,
        bool @explicit,
        bool skipTestWithoutData,
        Type[]? skipExceptions = null,
        string? skipReason = null,
        Type? skipType = null,
        string? skipUnless = null,
        string? skipWhen = null,
        Dictionary<string, HashSet<string>>? traits = null,
        string? sourceFilePath = null,
        int? sourceLineNumber = null,
        int? timeout = null
    )
        : base(
            testMethod,
            testCaseDisplayName,
            uniqueID,
            @explicit,
            skipTestWithoutData,
            skipExceptions,
            skipReason,
            skipType,
            skipUnless,
            skipWhen,
            traits,
            sourceFilePath,
            sourceLineNumber,
            timeout
        ) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UiDelayEnumeratedTheoryTestCase"/> class for the deserializer.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Called by the deserializer.")]
    public UiDelayEnumeratedTheoryTestCase() { }

    /// <inheritdoc/>
    public ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        ParallelMode parallelMode,
        ExecutionScheduler executionScheduler,
        FixtureMappingManager fixtureMappings
    ) =>
        UiTestCaseRunner.Run(
            this,
            explicitOption,
            messageBus,
            constructorArguments,
            aggregator,
            cancellationTokenSource,
            parallelMode,
            executionScheduler,
            fixtureMappings
        );
}

/// <summary>
/// Runs a test case with xUnit's own runner, on the UI thread of <see cref="HeadlessSession"/>: each test's class is made, its method
/// runs, and its class is disposed there.
/// </summary>
internal static class UiTestCaseRunner
{
    /// <summary>
    /// Runs a test case.
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="explicitOption">Whether explicit tests run.</param>
    /// <param name="messageBus">Where the results go.</param>
    /// <param name="constructorArguments">The arguments of the test class's constructor.</param>
    /// <param name="aggregator">Where the exceptions go.</param>
    /// <param name="cancellationTokenSource">The cancellation of the run.</param>
    /// <param name="parallelMode">How the tests run in parallel.</param>
    /// <param name="executionScheduler">What schedules them.</param>
    /// <param name="fixtureMappings">The fixtures of the collection and the class.</param>
    /// <returns>The summary.</returns>
    public static async ValueTask<RunSummary> Run(
        IXunitTestCase testCase,
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        ParallelMode parallelMode,
        ExecutionScheduler executionScheduler,
        FixtureMappingManager fixtureMappings
    )
    {
        IReadOnlyCollection<IXunitTest> tests = await aggregator.RunAsync(testCase.CreateTests, []);
        RunSummary summary = default;
        await HeadlessSession.Run(async () =>
            summary = await XunitTestCaseRunner.Instance.Run(
                testCase,
                tests,
                messageBus,
                aggregator,
                cancellationTokenSource,
                parallelMode,
                executionScheduler,
                testCase.TestCaseDisplayName,
                testCase.SkipReason,
                explicitOption,
                constructorArguments,
                fixtureMappings
            )
        );
        return summary;
    }
}
