using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace x3squaredcircles.PipelineGate.Container.Observability
{
    /// <summary>
    /// Represents the standardized, structured log message format to be sent to the firehose endpoint.
    /// </summary>
    public class FirehoseLogMessage
    {
        [JsonPropertyName("@timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("log.level")]
        public string Level { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("service.name")]
        public string ServiceName { get; set; }

        [JsonPropertyName("service.version")]
        public string ServiceVersion { get; set; }

        [JsonPropertyName("event.category")]
        public string Category { get; set; }

        [JsonPropertyName("error.message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ErrorMessage { get; set; }

        [JsonPropertyName("error.stack_trace")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ErrorStackTrace { get; set; }

        [JsonPropertyName("context")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object> Context { get; set; }

        public static FirehoseLogMessage FromLogEntry<TState>(
            string serviceName,
            string serviceVersion,
            string category,
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            var message = new FirehoseLogMessage
            {
                Timestamp = DateTime.UtcNow,
                Level = GetLogLevelString(logLevel),
                Message = formatter(state, exception),
                ServiceName = serviceName,
                ServiceVersion = serviceVersion,
                Category = category
            };

            if (exception != null)
            {
                message.ErrorMessage = exception.Message;
                message.ErrorStackTrace = exception.ToString();
            }

            if (state is IReadOnlyCollection<KeyValuePair<string, object>> stateDictionary)
            {
                message.Context = new Dictionary<string, object>();
                foreach (var item in stateDictionary)
                {
                    if (item.Key != "{OriginalFormat}")
                    {
                        message.Context[item.Key] = item.Value;
                    }
                }

                if (message.Context.Count == 0)
                {
                    message.Context = null;
                }
            }

            return message;
        }

        private static string GetLogLevelString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "trace",
                LogLevel.Debug => "debug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "error",
                LogLevel.Critical => "fatal",
                _ => "info"
            };
        }
    }
}