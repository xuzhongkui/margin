// NOTE: HeboTech.ATLib is no longer used in the COM scan runtime path.
// This file is kept only to avoid breaking builds if some legacy code still references the old extension method names.
// If you remove the HeboTech.ATLib package later, this file can remain as-is.

namespace Margin.Services;

internal static class AtChannelExtensions
{
    internal static Task<object?> SafeSendSingleLineAsync(
        this object _,
        ILogger logger,
        string command,
        int timeoutMs)
    {
        logger.LogWarning(
            "ATLib extension called but ATLib is not used anymore. Command={Command}, TimeoutMs={TimeoutMs}",
            command,
            timeoutMs);
        return Task.FromResult<object?>(null);
    }

    internal static Task<string?> ReadSingleLinePayloadAsync(
        this object _,
        ILogger logger,
        string command,
        int timeoutMs)
    {
        logger.LogWarning(
            "ATLib extension called but ATLib is not used anymore. Command={Command}, TimeoutMs={TimeoutMs}",
            command,
            timeoutMs);
        return Task.FromResult<string?>(null);
    }
}
