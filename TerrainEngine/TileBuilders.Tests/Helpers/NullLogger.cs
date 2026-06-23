using Kuoste.TerrainEngine.Common.Interfaces;
using System;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;

internal sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();

    public void LogDebug(string message) { }
    public void LogInfo(string message) { }
    public void LogWarning(string message) { }
    public void LogError(string message) { }
    public void LogException(Exception exception) { }
}
