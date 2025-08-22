# 3SC API Assembler: TypeScript Quick Start & Examples

This guide provides practical examples of how to use the 3SC Assembler DSL decorators in a TypeScript business logic project.

## 1. Getting Started

1. **Include the DSL:** Add the `three_sc_dsl.ts` file to your project's source directory.
2. **Configure `tsconfig.json`:** Ensure `"experimentalDecorators": true` and `"emitDecoratorMetadata": true` are enabled in your `compilerOptions`.
3. **Install `reflect-metadata`:** Run `npm install reflect-metadata`. Import it once at the top level of your application (e.g., in your main handler file).
4. **Decorate Your Code:** Import and apply the decorators to your handler classes and methods.

## 2. Core Concepts with Examples

### `@FunctionHandler()`

This decorator marks a class as a container for function entry points. The Assembler will scan any class decorated with `@FunctionHandler()`.

```typescript
import { FunctionHandler } from "./dsl/three_sc_dsl";
import 'reflect-metadata'; // Import once

@FunctionHandler()
export class OrderProcessingHandler {
    // ... methods go here
}
```

### `@DeploymentGroup()`

This decorator defines a deployable microservice. It groups related functions together and can be applied at the class or method level.

```typescript
import { FunctionHandler, DeploymentGroup, EventSource } from "./dsl/three_sc_dsl";

@FunctionHandler()
@DeploymentGroup({ serviceName: "OrderServices", version: "v2.1" })
export class OrderProcessingHandler {

    // This method is part of the "OrderServices" v2.1 deployment group.
    @EventSource("aws:sqs:{newOrdersQueue}:MessageReceived")
    public async processNewOrder(sqsEvent: SQSEvent, repo: IOrderRepository): Promise<void> {
        // ... logic ...
    }

    // This method OVERRIDES the class-level group.
    @DeploymentGroup({ serviceName: "InternalTools", version: "v1" })
    @EventSource("aws:apigateway:proxy:/tools/reprocess-order/{id}:POST")
    public async reprocessOrder(request: APIGatewayProxyEvent): Promise<any> {
        // ... logic ...
    }
}
```

### `@EventSource()`

This is the primary decorator. It marks a method as a function entry point and defines its trigger with a URN. Placeholders like `{customerUploadsBucket}` are replaced by pipeline variables.

#### AWS S3 PUT Event:

```typescript
@EventSource("aws:s3:{customerUploadsBucket}:ObjectCreated:Put")
public async handleNewUpload(s3Event: S3Event, service: IFileMetadataService): Promise<void> {
    // ... logic to process the new file ...
}
```

#### Azure HTTP GET Event:

```typescript
@EventSource("azure:apigateway:proxy:/products/{id}:GET")
public async getProductById(req: HttpRequest, id: string, repo: IProductRepository): Promise<Product> {
    // ... logic to fetch a product ...
    return new Product();
}
```

#### GCP Pub/Sub Event:

```typescript
@EventSource("gcp:pubsub:{newProductsTopic}:MessagePublished")
public async onNewProductPublished(message: PubsubMessage): Promise<void> {
    // ... logic to handle the Pub/Sub message ...
}
```

## 3. Weaving Cross-Cutting Concerns

### `@Requires()`

Use this to inject a pre-processing gate. The specified handler method must return a `boolean` or `Promise<boolean>`. If it returns `false`, the main business logic is not executed.

```typescript
// In a separate "hooks/security.ts" file...
export class SecurityHooks {
    public validateJwt(req: HttpRequest): boolean {
        // logic to validate the JWT from the request header
        console.log("Validating JWT...");
        return true; // or false
    }
}

// In your Function Handler...
import { SecurityHooks } from "./hooks/security";

@FunctionHandler()
export class AdminPanelHandler {
    @Requires({ handler: SecurityHooks, method: "validateJwt" })
    @EventSource("azure:apigateway:proxy:/admin/dashboard:GET")
    public async getDashboard(req: HttpRequest): Promise<DashboardData> {
        // This code only runs if validateJwt returns true.
        return new DashboardData();
    }
}
```

### `@RequiresLogger()`

Use this to inject observability and create an audit trail.

- **LoggingAction.OnInbound:** Logs the request payload before your business logic runs.
- **LoggingAction.OnError:** Logs any exception that occurs during the pipeline.

```typescript
// In a separate "hooks/auditing.ts" file...
export class AuditLogger {
    public logRequest(payload: any): void {
        // logic to serialize and log the incoming payload
        console.log(`AUDIT - Inbound Payload: ${JSON.stringify(payload)}`);
    }

    public logFailure(error: Error): void {
        // logic to log critical failure details
        console.error(`AUDIT - Critical Failure: ${error.message}`);
    }
}

// In your Function Handler...
import { LoggingAction } from "./dsl/three_sc_dsl";
import { AuditLogger } from "./hooks/auditing";

@FunctionHandler()
export class PaymentProcessingHandler {
    @RequiresLogger({ handler: AuditLogger, action: LoggingAction.OnInbound })
    @RequiresLogger({ handler: AuditLogger, action: LoggingAction.OnError })
    @EventSource("aws:sqs:{paymentQueue}:MessageReceived")
    public async processPayment(message: SQSMessage, gateway: IPaymentGateway): Promise<void> {
        // Business logic that might throw an exception...
        throw new Error("Payment gateway timed out");
    }
}
```