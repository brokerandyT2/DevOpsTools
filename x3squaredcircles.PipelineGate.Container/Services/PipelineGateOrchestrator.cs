using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.PipelineGate.Container.Models;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    public class PipelineGateOrchestrator : IPipelineGateOrchestrator
    {
        private readonly ILogger<PipelineGateOrchestrator> _logger;
        private readonly GateConfiguration _config;
        private readonly IHttpService _httpService;
        private readonly IEvaluationEngine _evaluationEngine;
        private readonly IControlPointService _controlPointService;

        public PipelineGateOrchestrator(
            ILogger<PipelineGateOrchestrator> logger,
            GateConfiguration config,
            IHttpService httpService,
            IEvaluationEngine evaluationEngine,
            IControlPointService controlPointService)
        {
            _logger = logger;
            _config = config;
            _httpService = httpService;
            _evaluationEngine = evaluationEngine;
            _controlPointService = controlPointService;
        }

        public async Task<int> RunAsync()
        {
            try
            {
                ValidateConfiguration();

                var finalAction = _config.Mode switch
                {
                    GateMode.Basic => await ExecuteBasicModeAsync(),
                    GateMode.Advanced => await ExecuteAdvancedModeAsync(),
                    GateMode.Custom => throw new NotImplementedException("Custom mode is not yet implemented."),
                    _ => throw new PipelineGateException(GateExitCode.InvalidConfiguration, $"Unsupported gate mode: {_config.Mode}")
                };

                return GetExitCodeForAction(finalAction);
            }
            catch (PipelineGateException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during orchestration.");
                throw new PipelineGateException(GateExitCode.UnhandledException, "Orchestration failed with an unexpected error.", ex);
            }
        }

        private async Task<GateAction> ExecuteBasicModeAsync()
        {
            _logger.LogInformation("Executing in BASIC mode. Target URL: {Url}", _config.Basic.Url);

            using var response = await _httpService.SendRequestAsync(_config.Basic.Url, _config.Basic.SecretName);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";

            var success = await _evaluationEngine.EvaluateConditionAsync(_config.Basic.SuccessEval, content, contentType);

            var proposedAction = success ? GateAction.Pass : _config.Basic.DefaultAction;

            var controlPointData = new ControlPointData
            {
                Mode = GateMode.Basic,
                EndpointUrl = _config.Basic.Url,
                HttpResponseStatusCode = (int)response.StatusCode,
                HttpResponseBody = content,
                InitialEvaluation = new EvaluationResult { Condition = _config.Basic.SuccessEval, Passed = success },
                ProposedAction = proposedAction
            };

            return await _controlPointService.InterceptDecisionAsync(controlPointData);
        }

        private async Task<GateAction> ExecuteAdvancedModeAsync()
        {
            return _config.Advanced.Action switch
            {
                AdvancedModeAction.Notify => await ExecuteNotifyAsync(),
                AdvancedModeAction.WaitFor => await ExecuteWaitForAsync(),
                _ => throw new PipelineGateException(GateExitCode.InvalidConfiguration, $"Unsupported advanced mode action: {_config.Advanced.Action}")
            };
        }

        private async Task<GateAction> ExecuteNotifyAsync()
        {
            _logger.LogInformation("Executing in ADVANCED-NOTIFY mode. Target URL: {Url}", _config.Advanced.NotifyUrl);
            await _httpService.NotifyAsync(_config.Advanced.NotifyUrl, _config.Advanced.NotifyPayload);
            _logger.LogInformation("Notify event sent.");
            return GateAction.Pass; // Notify always passes immediately
        }

        private async Task<GateAction> ExecuteWaitForAsync()
        {
            _logger.LogInformation("Executing in ADVANCED-WAITFOR mode. Polling URL: {Url}", _config.Advanced.WaitUrl);
            _logger.LogInformation("Timeout: {Timeout} minutes. Polling Interval: {Interval} seconds.", _config.Advanced.WaitTimeoutMinutes, _config.Advanced.WaitPollIntervalSeconds);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_config.Advanced.WaitTimeoutMinutes));
            HttpResponseMessage response = null;
            string content = null;
            bool success = false;
            bool failure = false;

            while (!timeoutCts.IsCancellationRequested)
            {
                try
                {
                    response = await _httpService.SendRequestAsync(_config.Advanced.WaitUrl, _config.Advanced.WaitSecretName);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Polling request failed with status code {StatusCode}. Retrying...", response.StatusCode);
                    }
                    else
                    {
                        content = await response.Content.ReadAsStringAsync();
                        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";

                        if (!string.IsNullOrWhiteSpace(_config.Advanced.WaitSuccessEval))
                        {
                            success = await _evaluationEngine.EvaluateConditionAsync(_config.Advanced.WaitSuccessEval, content, contentType);
                        }
                        if (!string.IsNullOrWhiteSpace(_config.Advanced.WaitFailureEval))
                        {
                            failure = await _evaluationEngine.EvaluateConditionAsync(_config.Advanced.WaitFailureEval, content, contentType);
                        }

                        if (success || failure) break;

                        _logger.LogInformation("Gate conditions not met. Will poll again in {Interval}s...", _config.Advanced.WaitPollIntervalSeconds);
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "An error occurred during polling. Retrying...");
                }

                await Task.Delay(TimeSpan.FromSeconds(_config.Advanced.WaitPollIntervalSeconds), timeoutCts.Token);
            }

            if (timeoutCts.IsCancellationRequested)
            {
                throw new PipelineGateException(GateExitCode.Timeout, $"Gate timed out after {_config.Advanced.WaitTimeoutMinutes} minutes.");
            }

            var proposedAction = success ? GateAction.Pass : (failure ? GateAction.Break : _config.Advanced.WaitDefaultAction);

            var controlPointData = new ControlPointData
            {
                Mode = GateMode.Advanced,
                EndpointUrl = _config.Advanced.WaitUrl,
                HttpResponseStatusCode = response != null ? (int)response.StatusCode : 500,
                HttpResponseBody = content,
                InitialEvaluation = new EvaluationResult { Condition = success ? _config.Advanced.WaitSuccessEval : _config.Advanced.WaitFailureEval, Passed = success },
                ProposedAction = proposedAction
            };

            return await _controlPointService.InterceptDecisionAsync(controlPointData);
        }

        private int GetExitCodeForAction(GateAction action)
        {
            _logger.LogInformation("Final Action: {Action}", action);
            return action switch
            {
                GateAction.Pass => (int)GateExitCode.Pass,
                GateAction.Pause => (int)GateExitCode.Pause,
                GateAction.Break => (int)GateExitCode.Break,
                _ => (int)GateExitCode.Pass
            };
        }

        private void ValidateConfiguration()
        {
            switch (_config.Mode)
            {
                case GateMode.Basic:
                    if (string.IsNullOrWhiteSpace(_config.Basic.Url)) throw new PipelineGateException(GateExitCode.InvalidConfiguration, "GATE_BASIC_URL is required for Basic mode.");
                    if (string.IsNullOrWhiteSpace(_config.Basic.SuccessEval)) throw new PipelineGateException(GateExitCode.InvalidConfiguration, "GATE_BASIC_SUCCESS_EVAL is required for Basic mode.");
                    break;
                case GateMode.Advanced:
                    if (_config.Advanced.Action == AdvancedModeAction.Notify && string.IsNullOrWhiteSpace(_config.Advanced.NotifyUrl))
                        throw new PipelineGateException(GateExitCode.InvalidConfiguration, "GATE_ADVANCED_NOTIFY_URL is required for Advanced-Notify action.");
                    if (_config.Advanced.Action == AdvancedModeAction.WaitFor && string.IsNullOrWhiteSpace(_config.Advanced.WaitUrl))
                        throw new PipelineGateException(GateExitCode.InvalidConfiguration, "GATE_ADVANCED_WAIT_URL is required for Advanced-WaitFor action.");
                    if (_config.Advanced.Action == AdvancedModeAction.WaitFor && string.IsNullOrWhiteSpace(_config.Advanced.WaitSuccessEval))
                        throw new PipelineGateException(GateExitCode.InvalidConfiguration, "GATE_ADVANCED_WAIT_SUCCESS_EVAL is required for Advanced-WaitFor action.");
                    break;
            }
        }
    }
}