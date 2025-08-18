# 3SC Conduit: Java Developer Guide

This guide provides Java-specific examples for using the `3SC Conduit` annotations to define and generate event-driven services.

To get started, add the `3SC.Conduit.Annotations.jar` package as a dependency or download the annotation source code from the running Conduit container at `/code/java`.

## 1. Defining a Service with `@DataConsumer`

Mark any class that will contain business logic with the `@DataConsumer` annotation. This is the top-level entry point for the tool's analyzer.

```java
import com.threese.conduit.annotations.*;

@DataConsumer(serviceName = "order-processing-service")
public class OrderProcessor {
    // ... your trigger methods go here
}
```

## 2. Defining a Trigger with `@Trigger`

Decorate any public method inside your `@DataConsumer` class with a `@Trigger` annotation to expose it as a service entry point. The first parameter of the method is always the data contract (POJO).

The TriggerType enum provides a list of supported event sources.

```java
@DataConsumer
public class OrderProcessor {

    // This method is triggered by a message on an AWS SQS Queue.
    @Trigger(type = TriggerType.AWS_SQS_QUEUE, name = "new-orders")
    public void handleNewOrder(OrderEvent order, Connection db) {
        // Your business logic here...
        // db.execute("...");
    }

    // This method is triggered by an HTTP POST request.
    @Trigger(type = TriggerType.HTTP, name = "orders/manual-entry")
    public void handleManualOrder(OrderEvent order, Logger log) {
        log.info("Manual order received.");
        // ...
    }
}
```

## 3. Requiring a Pre-Processing Gate with `@Requires`

Use `@Requires` to enforce a gatekeeper, like an authentication check, before your main logic is executed. The tool will generate the code to call your specified handler.

The handler method must return a `boolean`. If it returns `false`, execution is halted.

```java
// In your shared hooks library
public class MyAuthHooks {
    public boolean validate(APIGatewayProxyRequestEvent request) { /* ... check JWT ... */ }
}

// In your business logic
@DataConsumer
public class AdminService {

    @Trigger(type = TriggerType.HTTP, name = "admin/run-job")
    @Requires(
        handler = MyAuthHooks.class,
        method = "validate"
    )
    public void runAdminJob(AdminJobPayload payload) {
        // This code only runs if MyAuthHooks.validate returns true.
    }
}
```

## 4. Requiring Logging with `@RequiresLogger`

Use `@RequiresLogger` to declaratively add observability to your service. The tool will automatically wrap your trigger method in a try/catch block and call your specified logger.

The LoggingAction enum specifies when to log:
- **ON_INBOUND**: Logs the incoming POJO at the start.
- **ON_ERROR**: Logs the exception in the catch block.
- **ON_OUTBOUND**: Logs the return value of your method upon success.

```java
// In your shared logging library
public class SplunkLogger {
    // The tool will match the parameter type to know what to inject.
    public void log(Object payload) { /* ... */ }
    public void log(Exception ex) { /* ... */ }
}

// In your business logic
@DataConsumer
public class OrderProcessor {

    @Trigger(type = TriggerType.HTTP, name = "orders")
    @RequiresLogger(
        handler = SplunkLogger.class,
        action = LoggingAction.ON_INBOUND
    )
    @RequiresLogger(
        handler = SplunkLogger.class,
        action = LoggingAction.ON_ERROR
    )
    public void handleNewOrder(OrderEvent order) {
        // ... your logic ...
    }
}
```

## 5. Trace Logging with `@RequiresResultsLogger`

Use `@RequiresResultsLogger` to get deep insight into your method's execution by logging the state of local variables. Note that due to Java's compiled nature, this feature relies on bytecode analysis and may have limitations compared to C#.

```java
@DataConsumer
public class ComplexWorkflow {

    @Trigger(type = TriggerType.HTTP, name = "workflows/start")
    @RequiresResultsLogger(
        handler = SplunkLogger.class,
        method = "log",
        variable = "validated"
    )
    @RequiresResultsLogger(
        handler = SplunkLogger.class,
        method = "log",
        variable = "enriched"
    )
    public void startWorkflow(InitialPayload payload) {
        ValidatedPayload validated = this.validate(payload);
        // The tool will inject a call to SplunkLogger.log(validated) here.

        EnrichedPayload enriched = this.enrich(validated);
        // The tool will inject a call to SplunkLogger.log(enriched) here.

        this.save(enriched);
    }

    private ValidatedPayload validate(InitialPayload p) { /*...*/ }
    private EnrichedPayload enrich(ValidatedPayload p) { /*...*/ }
    private void save(EnrichedPayload p) { /*...*/ }
}
```