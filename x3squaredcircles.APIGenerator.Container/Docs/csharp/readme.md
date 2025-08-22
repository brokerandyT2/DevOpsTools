# 3SC API Assembler: C# Quick Start & Examples

This guide provides practical examples of how to use the 3SC Assembler DSL attributes in a C# business logic project.

## 1. Getting Started

1. **Include the DSL:** Copy the `ThreeScDsl.cs` file into your business logic project. Ensure the namespace is `_3SC.Assembler.Attributes`.
2. **Annotate Your Code:** Add the attributes to your handler classes and methods to define your services and function entry points.

## 2. Core Concepts with Examples

### `[FunctionHandler]`

This attribute marks a class as a container for function entry points. The Assembler will scan any class decorated with `[FunctionHandler]`.

```csharp
using _3SC.Assembler.Attributes;

[FunctionHandler]
public class OrderProcessingHandler
{
    // ... methods go here
}
```

### `[DeploymentGroup]`

This attribute defines a deployable microservice. It groups related functions together. It can be applied at the class level (as a default for all methods) or on a specific method to override the class-level group.

```csharp
[FunctionHandler]
[DeploymentGroup("OrderServices", "v2.1")] // All methods belong to OrderServices-v2.1 by default
public class OrderProcessingHandler
{
    // This method is part of the "OrderServices" v2.1 deployment group.
    [EventSource("aws:sqs:{newOrdersQueue}:MessageReceived")]
    public async Task ProcessNewOrder(SQSEvent sqsEvent, IOrderRepository repo)
    {
        // ... logic ...
    }

    // This method OVERRIDES the class-level group and belongs to a different service.
    [DeploymentGroup("InternalTools", "v1")]
    [EventSource("aws:apigateway:proxy:/tools/reprocess-order/{id}:POST")]
    public async Task ReprocessOrder(APIGatewayProxyRequest request, string id)
    {
        // ... logic ...
    }
}
```

### `[EventSource]`

This is the primary attribute. It marks a method as a function entry point and defines its trigger with a URN. Placeholders like `{newOrdersQueue}` will be replaced by pipeline variables at deploy time.

#### AWS S3 PUT Event:

```csharp
[EventSource("aws:s3:{customerUploadsBucket}:ObjectCreated:Put")]
public async Task HandleNewUpload(S3Event s3Event, IFileMetadataService service)
{
    // ... logic to process the new file ...
}
```

#### Azure HTTP GET Event:

```csharp
[EventSource("azure:apigateway:proxy:/products/{id}:GET")]
public async Task<Product> GetProductById(HttpRequestData req, string id, IProductRepository repo)
{
    // ... logic to fetch a product ...
}
```

#### GCP Pub/Sub Event:

```csharp
[EventSource("gcp:pubsub:{newProductsTopic}:MessagePublished")]
public async Task OnNewProductPublished(CloudEvent cloudEvent, ILogger log)
{
    // ... logic to handle the Pub/Sub message ...
}
```

## 3. Weaving Cross-Cutting Concerns

### `[Requires]`

Use this to inject a pre-processing gate, like an authentication or validation check. The specified method must return a `bool` (or `Task<bool>`). If it returns `false`, the pipeline short-circuits and the main business logic is not executed.

```csharp
// In a separate "Hooks" project...
public class SecurityHooks
{
    public bool ValidateJwt(HttpRequestData req)
    {
        // logic to validate the JWT from the request header
        return true; // or false
    }
}

// In your Function Handler...
[FunctionHandler]
public class AdminPanelHandler
{
    [Requires(typeof(SecurityHooks), nameof(SecurityHooks.ValidateJwt))]
    [EventSource("azure:apigateway:proxy:/admin/dashboard:GET")]
    public async Task<DashboardData> GetDashboard(HttpRequestData req)
    {
        // This code only runs if ValidateJwt returns true.
    }
}
```

### `[RequiresLogger]`

Use this to inject observability and create an audit trail.

- **OnInbound:** Logs the request payload before your business logic runs.
- **OnError:** Logs any exception that occurs during the pipeline.

```csharp
// In a separate "Logging" project...
public class AuditLogger
{
    public async Task LogRequest(object payload, ILogger log)
    {
        // logic to serialize and log the incoming payload to a secure location
    }

    public async Task LogFailure(Exception ex, ILogger log)
    {
        // logic to log critical failure details to a monitoring system
    }
}

// In your Function Handler...
[FunctionHandler]
public class PaymentProcessingHandler
{
    [RequiresLogger(typeof(AuditLogger), LoggingAction.OnInbound)]
    [RequiresLogger(typeof(AuditLogger), LoggingAction.OnError)]
    [EventSource("aws:sqs:{paymentQueue}:MessageReceived")]
    public async Task ProcessPayment(SQSEvent.SQSMessage message, IPaymentGateway gateway)
    {
        // Business logic that might throw an exception...
    }
}
```