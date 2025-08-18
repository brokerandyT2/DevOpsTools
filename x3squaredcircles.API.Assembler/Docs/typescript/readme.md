# 3SC Conduit: TypeScript Developer Guide

This guide provides TypeScript-specific examples for using the `3SC Conduit` decorators to define and generate event-driven services.

To get started, download the `datalink.decorators.ts` file from the running Conduit container at `/code/typescript` and include it in your business logic project.

## 1. Defining a Service with `@DataConsumer`

Mark any class that will contain business logic with the `@DataConsumer` decorator. This is the top-level entry point for the tool's analyzer.

```typescript
import { DataConsumer, Trigger, TriggerType } from './datalink.decorators';
import { OrderEvent } from './dtos';

@DataConsumer({ serviceName: "order-processing-service" })
export class OrderProcessor {
    // ... your trigger methods go here
}
```

## 2. Defining a Trigger with `@Trigger`

Decorate any public method inside your `@DataConsumer` class with a `@Trigger` decorator to expose it as a service entry point. The first parameter of the method is always the data contract (DTO).

The TriggerType enum provides a list of supported event sources.

```typescript
@DataConsumer()
export class OrderProcessor {
    
    // This method is triggered by a message on an AWS SQS Queue.
    @Trigger({ type: TriggerType.AwsSqsQueue, name: "new-orders" })
    public async handleNewOrder(order: OrderEvent, db: DbConnection): Promise<void> {
        // Your business logic here...
        // await db.execute("...");
    }

    // This method is triggered by an HTTP POST request.
    @Trigger({ type: TriggerType.Http, name: "orders/manual-entry" })
    public async handleManualOrder(order: OrderEvent, logger: ILogger): Promise<void> {
        logger.info("Manual order received.");
        // ...
    }
}
```

## 3. Requiring a Pre-Processing Gate with `@Requires`

Use `@Requires` to enforce a gatekeeper, like an authentication check, before your main logic is executed. The handler should be a reference to a class and method that returns a boolean.

```typescript
// In your shared hooks library (e.g., hooks/auth.ts)
export class MyAuthHooks {
    public validate(request: HttpRequest): boolean { /* ... check JWT ... */ return true; }
}

// In your business logic
import { MyAuthHooks } from './hooks/auth';

@DataConsumer()
export class AdminService {

    @Trigger({ type: TriggerType.Http, name: "admin/run-job" })
    @Requires({
        handler: MyAuthHooks,
        method: "validate"
    })
    public runAdminJob(payload: AdminJobPayload): void {
        // This code only runs if MyAuthHooks.validate returns true.
    }
}
```

## 4. Requiring Logging with `@RequiresLogger`

Use `@RequiresLogger` to declaratively add observability. The tool will automatically wrap your trigger method in a try/catch block and call your specified logger.

The LoggingAction enum specifies when to log:
- **OnInbound**: Logs the incoming DTO at the start.
- **OnError**: Logs the exception in the catch block.
- **OnOutbound**: Logs the return value of your method upon success.

```typescript
// In your shared logging library (e.g., loggers/splunk.ts)
export class SplunkLogger {
    public log(payload: any | Error): void { /* Logic to send data to Splunk */ }
}

// In your business logic
import { SplunkLogger } from './loggers/splunk';

@DataConsumer()
export class OrderProcessor {

    @Trigger({ type: TriggerType.Http, name: "orders" })
    @RequiresLogger({
        handler: SplunkLogger,
        action: LoggingAction.OnInbound
    })
    @RequiresLogger({
        handler: SplunkLogger,
        action: LoggingAction.OnError
    })
    public handleNewOrder(order: OrderEvent): void {
        // ... your logic ...
    }
}
```

## 5. Trace Logging with `@RequiresResultsLogger`

Use `@RequiresResultsLogger` to get deep insight into your method's execution by logging the state of local variables. This feature is more limited in transpiled languages like TypeScript compared to C#.

```typescript
@DataConsumer()
export class ComplexWorkflow {

    @Trigger({ type: TriggerType.Http, name: "workflows/start" })
    @RequiresResultsLogger({
        handler: SplunkLogger,
        method: "log",
        variable: "validated"
    })
    @RequiresResultsLogger({
        handler: SplunkLogger,
        method: "log",
        variable: "enriched"
    })
    public startWorkflow(payload: InitialPayload): void {
        const validated = this.validate(payload);
        // The tool will inject a call to SplunkLogger.log(validated) here.

        const enriched = this.enrich(validated);
        // The tool will inject a call to SplunkLogger.log(enriched) here.

        this.save(enriched);
    }

    private validate(p: InitialPayload): ValidatedPayload { 
        // Validation logic
        return {} as ValidatedPayload;
    }
    
    private enrich(p: ValidatedPayload): EnrichedPayload { 
        // Enrichment logic
        return {} as EnrichedPayload;
    }
    
    private save(p: EnrichedPayload): void { 
        // Save logic
    }
}
```