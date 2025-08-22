using System;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    /// <summary>
    /// Defines a standardized logging contract for the application.
    /// </summary>
    public interface IAppLogger
    {
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
        void LogCritical(string message, Exception? exception = null);
        void LogStartPhase(string phaseName);
        void LogEndPhase(string phaseName, bool success);
    }
}