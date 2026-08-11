using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.UnitTests.TestSupport;

/// <summary>
/// Test logger that records every log entry (level, formatted message,
/// structured state values, exception) and every scope pushed via
/// <see cref="ILogger.BeginScope{TState}"/>.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyList<KeyValuePair<string, object?>> StateValues);

    public List<LogEntry> Entries { get; } = [];

    public List<object?> Scopes { get; } = [];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        Scopes.Add(state);
        return new NoopScope();
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var stateValues = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, stateValues));
    }

    private sealed class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
