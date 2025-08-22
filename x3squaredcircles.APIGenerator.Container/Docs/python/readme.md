# 3SC API Assembler: Python Quick Start & Examples

This guide provides practical examples of how to use the 3SC Assembler DSL decorators in a Python business logic project.

## 1. Getting Started

1. **Include the DSL:** Add the `three_sc_dsl.py` file to your business logic project, for example in a `dsl` sub-package.
2. **Decorate Your Code:** Import and apply the decorators to your handler classes and methods to define your services and function entry points.

## 2. Core Concepts with Examples

### `@function_handler`

This decorator marks a class as a container for function entry points. The Assembler will scan any class decorated with `@function_handler`.

```python
from dsl.three_sc_dsl import function_handler

@function_handler
class OrderProcessingHandler:
    # ... methods go here
    pass
```

### `@deployment_group`

This decorator defines a deployable microservice. It groups related functions together and can be applied at the class or method level.

```python
from dsl.three_sc_dsl import function_handler, deployment_group, event_source

@function_handler
@deployment_group(service_name="OrderServices", version="v2.1")
class OrderProcessingHandler:

    # This method is part of the "OrderServices" v2.1 deployment group.
    @event_source(event_urn="aws:sqs:{newOrdersQueue}:MessageReceived")
    def process_new_order(self, sqs_event, order_repo):
        # ... logic ...
        pass

    # This method OVERRIDES the class-level group.
    @deployment_group(service_name="InternalTools", version="v1")
    @event_source(event_urn="aws:apigateway:proxy:/tools/reprocess-order/{id}:POST")
    def reprocess_order(self, request, order_id):
        # ... logic ...
        pass
```

### `@event_source`

This is the primary decorator. It marks a method as a function entry point and defines its trigger with a URN. Placeholders like `{customerUploadsBucket}` are replaced by pipeline variables.

#### AWS S3 PUT Event:

```python
@event_source(event_urn="aws:s3:{customerUploadsBucket}:ObjectCreated:Put")
def handle_new_upload(self, s3_event, metadata_service):
    # ... logic to process the new file ...
    pass
```

#### Azure HTTP GET Event:

```python
@event_source(event_urn="azure:apigateway:proxy:/products/{id}:GET")
def get_product_by_id(self, req, product_id, product_repo):
    # ... logic to fetch a product ...
    return {"productId": product_id, "name": "Widget"}
```

#### GCP Pub/Sub Event:

```python
@event_source(event_urn="gcp:pubsub:{newProductsTopic}:MessagePublished")
def on_new_product_published(self, cloud_event):
    # ... logic to handle the Pub/Sub message ...
    pass
```

## 3. Weaving Cross-Cutting Concerns

### `@requires`

Use this to inject a pre-processing gate like an authentication check. The specified handler function must return a `boolean`. If it returns `False`, the main business logic is not executed.

```python
# In a separate "hooks/security.py" file...
class SecurityHooks:
    def validate_jwt(self, req):
        # logic to validate the JWT from the request header
        print("Validating JWT...")
        return True  # or False

# In your Function Handler...
from hooks.security import SecurityHooks

@function_handler
class AdminPanelHandler:
    @requires(handler=SecurityHooks, method="validate_jwt")
    @event_source(event_urn="azure:apigateway:proxy:/admin/dashboard:GET")
    def get_dashboard(self, req):
        # This code only runs if validate_jwt returns True.
        pass
```

### `@requires_logger`

Use this to inject observability and create an audit trail.

- **LoggingAction.ON_INBOUND:** Logs the request payload before your business logic runs.
- **LoggingAction.ON_ERROR:** Logs any exception that occurs during the pipeline.

```python
# In a separate "hooks/auditing.py" file...
class AuditLogger:
    def log_request(self, payload):
        # logic to serialize and log the incoming payload
        print(f"AUDIT - Inbound Payload: {payload}")
    
    def log_failure(self, exception):
        # logic to log critical failure details
        print(f"AUDIT - Critical Failure: {exception}")

# In your Function Handler...
from dsl.three_sc_dsl import LoggingAction
from hooks.auditing import AuditLogger

@function_handler
class PaymentProcessingHandler:
    @requires_logger(handler=AuditLogger, action=LoggingAction.ON_INBOUND)
    @requires_logger(handler=AuditLogger, action=LoggingAction.ON_ERROR)
    @event_source(event_urn="aws:sqs:{paymentQueue}:MessageReceived")
    def process_payment(self, message, payment_gateway):
        # Business logic that might raise an exception...
        raise ValueError("Payment gateway timed out")
```