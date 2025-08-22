# 3SC API Assembler: Java Quick Start & Examples

This guide provides practical examples of how to use the 3SC Assembler DSL annotations in a Java business logic project.

## 1. Getting Started

1. **Include the DSL:** Add the `Dsl.java` file (or the compiled `.jar`) to your business logic project. Ensure the package is `com.threese.assembler.annotations`.
2. **Annotate Your Code:** Add the annotations to your handler classes and methods to define your services and function entry points.

## 2. Core Concepts with Examples

### `@FunctionHandler`

This annotation marks a class as a container for function entry points. The Assembler will scan any class annotated with `@FunctionHandler`.

```java
import com.threese.assembler.annotations.Dsl.*;

@FunctionHandler
public class OrderProcessingHandler {
    // ... methods go here
}
```

### `@DeploymentGroup`

This annotation defines a deployable microservice. It groups related functions together and can be applied at the class or method level.

```java
@FunctionHandler
@DeploymentGroup(serviceName = "OrderServices", version = "v2.1")
public class OrderProcessingHandler {

    // This method is part of the "OrderServices" v2.1 deployment group.
    @EventSource(eventUrn = "aws:sqs:{newOrdersQueue}:MessageReceived")
    public void processNewOrder(SQSEvent sqsEvent, IOrderRepository repo) {
        // ... logic ...
    }

    // This method OVERRIDES the class-level group.
    @DeploymentGroup(serviceName = "InternalTools", version = "v1")
    @EventSource(eventUrn = "aws:apigateway:proxy:/tools/reprocess-order/{id}:POST")
    public void reprocessOrder(APIGatewayProxyRequestEvent request) {
        // ... logic ...
    }
}
```

### `@EventSource`

This is the primary annotation. It marks a method as a function entry point and defines its trigger with a URN. Placeholders like `{customerUploadsBucket}` are replaced by pipeline variables.

#### AWS S3 PUT Event:

```java
@EventSource(eventUrn = "aws:s3:{customerUploadsBucket}:ObjectCreated:Put")
public void handleNewUpload(S3Event s3Event, IFileMetadataService service) {
    // ... logic to process the new file ...
}
```

#### Azure HTTP GET Event:

```java
@EventSource(eventUrn = "azure:apigateway:proxy:/products/{id}:GET")
public Product getProductById(HttpRequestMessage<Optional<String>> request, String id, IProductRepository repo) {
    // ... logic to fetch a product ...
    return new Product();
}
```

#### GCP Pub/Sub Event:

```java
@EventSource(eventUrn = "gcp:pubsub:{newProductsTopic}:MessagePublished")
public void onNewProductPublished(PubSubMessage message, Context context) {
    // ... logic to handle the Pub/Sub message ...
}
```

## 3. Weaving Cross-Cutting Concerns

### `@Requires`

Use this to inject a pre-processing gate like an authentication check. The specified handler method must return a `boolean`. If it returns `false`, the main business logic is not executed.

```java
// In a separate "Hooks" project...
public class SecurityHooks {
    public boolean validateJwt(HttpRequestMessage<Optional<String>> request) {
        // logic to validate the JWT from the request header
        return true; // or false
    }
}

// In your Function Handler...
@FunctionHandler
public class AdminPanelHandler {

    @Requires(handler = SecurityHooks.class, method = "validateJwt")
    @EventSource(eventUrn = "azure:apigateway:proxy:/admin/dashboard:GET")
    public DashboardData getDashboard(HttpRequestMessage<Optional<String>> request) {
        // This code only runs if validateJwt returns true.
        return new DashboardData();
    }
}
```

### `@RequiresLogger`

Use this to inject observability and create an audit trail.

- **ON_INBOUND:** Logs the request payload before your business logic runs.
- **ON_ERROR:** Logs any exception that occurs during the pipeline.

```java
// In a separate "Logging" project...
public class AuditLogger {
    public void logRequest(Object payload, FunctionLogger logger) {
        // logic to serialize and log the incoming payload
    }

    public void logFailure(Exception ex, FunctionLogger logger) {
        // logic to log critical failure details
    }
}

// In your Function Handler...
@FunctionHandler
public class PaymentProcessingHandler {

    @RequiresLogger(handler = AuditLogger.class, action = LoggingAction.ON_INBOUND)
    @RequiresLogger(handler = AuditLogger.class, action = LoggingAction.ON_ERROR)
    @EventSource(eventUrn = "aws:sqs:{paymentQueue}:MessageReceived")
    public void processPayment(SQSEvent.SQSMessage message, IPaymentGateway gateway) {
        // Business logic that might throw an exception...
    }
}
```