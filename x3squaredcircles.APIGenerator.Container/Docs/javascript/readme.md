# 3SC API Assembler: JavaScript Quick Start & Examples

This guide provides practical examples of how to use the 3SC Assembler DSL functions in a JavaScript business logic project.

## 1. Getting Started

1. **Include the DSL:** `require` the `three_sc_dsl.js` file into your business logic project.
2. **Annotate Your Code:** Wrap your handler classes and methods with the DSL functions to define your services and function entry points.

## 2. Core Concepts with Examples

### `FunctionHandler`

This function marks a class as a container for function entry points. The Assembler will scan any class wrapped with `FunctionHandler`.

```javascript
const { FunctionHandler } = require('./dsl/three_sc_dsl');

class OrderProcessingHandler {
    // ... methods go here
}

module.exports = { OrderProcessingHandler: FunctionHandler(OrderProcessingHandler) };
```

### `DeploymentGroup`

This function defines a deployable microservice. It can be applied at the class or method level.

```javascript
const { FunctionHandler, DeploymentGroup, EventSource } = require('./dsl/three_sc_dsl');

// Apply @DeploymentGroup at the class level
let OrderProcessingHandler = DeploymentGroup({ serviceName: 'OrderServices', version: 'v2.1' })(
    class OrderProcessingHandler {
        // This method is part of "OrderServices" v2.1
        async processNewOrder(sqsEvent, repo) {
            // ... logic ...
        }

        // This method will be individually wrapped to override the group
        async reprocessOrder(request) {
            // ... logic ...
        }
    }
);

// Override @DeploymentGroup at the method level
OrderProcessingHandler.prototype.reprocessOrder = EventSource('aws:apigateway:proxy:/tools/reprocess-order/{id}:POST')(
    DeploymentGroup({ serviceName: 'InternalTools', version: 'v1' })(
        OrderProcessingHandler.prototype.reprocessOrder
    )
);

module.exports = { OrderProcessingHandler: FunctionHandler(OrderProcessingHandler) };
```

### `EventSource`

This is the primary function. It marks a method as a function entry point and defines its trigger with a URN.

#### AWS S3 PUT Event:

```javascript
// In your handler class
const { EventSource } = require('./dsl/three_sc_dsl');

class FileHandler {
    // Note: In JS, decorators are functions that wrap other functions.
    // The DSL is applied after the class definition.
}

FileHandler.prototype.handleNewUpload = EventSource('aws:s3:{customerUploadsBucket}:ObjectCreated:Put')(
    async function(s3Event, metadataService) {
        // ... logic to process the new file ...
    }
);
```

#### Azure HTTP GET Event:

```javascript
// In your handler class
const { EventSource } = require('./dsl/three_sc_dsl');

ProductHandler.prototype.getProductById = EventSource('azure:apigateway:proxy:/products/{id}:GET')(
    async function(context, req) {
        // ... logic to fetch a product ...
        return { productId: req.params.id, name: 'Widget' };
    }
);
```

#### GCP Pub/Sub Event:

```javascript
// In your handler class
const { EventSource } = require('./dsl/three_sc_dsl');

ProductHandler.prototype.onNewProductPublished = EventSource('gcp:pubsub:{newProductsTopic}:MessagePublished')(
    async function(message, context) {
        // ... logic to handle the Pub/Sub message ...
    }
);
```

## 3. Weaving Cross-Cutting Concerns

### `Requires`

Use this to inject a pre-processing gate. The specified handler method must return a `boolean`.

```javascript
// In a separate "hooks/security.js" file...
class SecurityHooks {
    validateJwt(req) {
        console.log('Validating JWT...');
        return true; // or false
    }
}
module.exports = { SecurityHooks };

// In your Function Handler...
const { Requires, EventSource } = require('./dsl/three_sc_dsl');
const { SecurityHooks } = require('./hooks/security');

AdminPanelHandler.prototype.getDashboard = Requires({ handler: SecurityHooks, method: 'validateJwt' })(
    EventSource('azure:apigateway:proxy:/admin/dashboard:GET')(
        async function(context, req) {
            // This code only runs if validateJwt returns true.
        }
    )
);
```

### `RequiresLogger`

Use this to inject observability and create an audit trail.

- **LoggingAction.OnInbound:** Logs the request payload before your business logic runs.
- **LoggingAction.OnError:** Logs any exception that occurs during the pipeline.

```javascript
// In a separate "hooks/auditing.js" file...
class AuditLogger {
    logRequest(payload) {
        console.log(`AUDIT - Inbound Payload: ${JSON.stringify(payload)}`);
    }
    
    logFailure(error) {
        console.error(`AUDIT - Critical Failure: ${error.message}`);
    }
}
module.exports = { AuditLogger };

// In your Function Handler...
const { RequiresLogger, EventSource, LoggingAction } = require('./dsl/three_sc_dsl');
const { AuditLogger } = require('./hooks/auditing');

PaymentHandler.prototype.processPayment = RequiresLogger({ handler: AuditLogger, action: LoggingAction.OnError })(
    RequiresLogger({ handler: AuditLogger, action: LoggingAction.OnInbound })(
        EventSource('aws:sqs:{paymentQueue}:MessageReceived')(
            async function(event) {
                // Business logic that might throw an error...
                throw new Error('Payment gateway timed out');
            }
        )
    )
);
```