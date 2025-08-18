
# 3SC Conduit: C# Developer Guide

This guide provides C#-specific examples for using the `3SC Conduit` attributes to define and generate event-driven services.

To get started, add a reference to the `3SC.Conduit.Attributes.dll` package or download the attribute source code from the running Conduit container at `/code/csharp`.

## 1. Defining a Service with `[DataConsumer]`

Mark any class that will contain business logic with the `[DataConsumer]` attribute. This is the top-level entry point for the tool's analyzer.

```csharp
using _3SC.Conduit.Attributes;

[DataConsumer(ServiceName = "order-processing-service")]
public class OrderProcessor
{
    // ... your trigger methods go here
}
```

## 2. Defining a Trigger with `[Trigger]`

Decorate any public method inside your DataConsumer class with a `[Trigger]` attribute to expose it as a service entry point. The first parameter of the method is always the data contract (DTO).

The TriggerType enum provides a list of supported event sources.

```csharp
[DataConsumer]
public class OrderProcessor
{
    // This method is triggered by a message on an Azure Service Bus Queue.
    [Trigger(Type = TriggerType.AzureServiceBusQueue, Name = "new-orders")]
    public async Task HandleNewOrder(OrderEvent order, IDbConnection db)
    {
        // Your business logic here...
        await db.InsertAsync(order);
    }

    // This method is triggered by an HTTP POST request.
    [Trigger(Type = TriggerType.Http, Name = "orders/manual-entry")]
    public async Task HandleManualOrder(OrderEvent order, ILogger log)
    {
        log.LogInformation("Manual order received.");
        // ...
    }
}
```

## 3. Requiring a Pre-Processing Gate with `[Requires]`

Use `[Requires]` to enforce a gatekeeper, like an authentication check, before your main logic is executed. The tool will generate the code to call your specified handler.

The handler method must return a `bool` or `Task<bool>`. If it returns `false`, execution is halted.

```csharp
// In your shared hooks library
public class MyAuthHooks
{
    public bool Validate(HttpRequestData request) { /* ... check JWT ... */ }
}

// In your business logic
[DataConsumer]
public class AdminService
{
    [Trigger(Type = TriggerType.Http, Name = "admin/run-job")]
    [Requires(
        Handler = typeof(MyAuthHooks),
        Method = nameof(MyAuthHooks.Validate)
    )]
    public void RunAdminJob(AdminJobPayload payload)
    {
        // This code only runs if MyAuthHooks.Validate returns true.
    }
}
```

## 4. Requiring Logging with `[RequiresLogger]`

Use `[RequiresLogger]` to declaratively add observability to your service. The tool will automatically wrap your trigger method in a try/catch block and call your specified logger.

The LoggingAction enum specifies when to log:
- **OnInbound**: Logs the incoming DTO at the start.
- **OnError**: Logs the exception in the catch block.
- **OnOutbound**: Logs the return value of your method upon success.

```csharp
// In your shared logging library
public class SplunkLogger
{
    // The tool will match the parameter type to know what to inject.
    public void Log(object payload) { /* ... */ }
    public void Log(Exception ex) { /* ... */ }
}

// In your business logic
[DataConsumer]
public class OrderProcessor
{
    [Trigger(Type = TriggerType.Http, Name = "orders")]
    [RequiresLogger(
        Handler = typeof(SplunkLogger),
        Action = LoggingAction.OnInbound
    )]
    [RequiresLogger(
        Handler = typeof(SplunkLogger),
        Action = LoggingAction.OnError
    )]
    public void HandleNewOrder(OrderEvent order)
    {
        // ... your logic ...
    }
}
```

## 5. Trace Logging with `[RequiresResultsLogger]`

Use `[RequiresResultsLogger]` to get deep insight into your method's execution by logging the state of local variables.

```csharp
[DataConsumer]
public class ComplexWorkflow
{
    [Trigger(Type = TriggerType.Http, Name = "workflows/start")]
    [RequiresResultsLogger(
        Handler = typeof(SplunkLogger),
        Method = "Log",
        Variable = "validated"
    )]
    [RequiresResultsLogger(
        Handler = typeof(SplunkLogger),
        Method = "Log",
        Variable = "enriched"
    )]
    public void StartWorkflow(InitialPayload payload)
    {
        var validated = Validate(payload);
        // The tool will inject a call to SplunkLogger.Log(validated) here.

        var enriched = Enrich(validated);
        // The tool will inject a call to SplunkLogger.Log(enriched) here.

        Save(enriched);
    }

    private ValidatedPayload Validate(InitialPayload p) { /*...*/ }
    private EnrichedPayload Enrich(ValidatedPayload p) { /*...*/ }
    private void Save(EnrichedPayload p) { /*...*/ }
}
```