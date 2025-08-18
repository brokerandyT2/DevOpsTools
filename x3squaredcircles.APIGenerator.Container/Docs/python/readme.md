# 3SC Conduit: Python Developer Guide

This guide provides Python-specific examples for using the `3SC Conduit` decorators to define and generate event-driven services.

To get started, download the `datalink_decorators.py` file from the running Conduit container at `/code/python` and place it in your business logic project.

## 1. Defining a Service with `@data_consumer`

Mark any class that will contain business logic with the `@data_consumer` decorator. This is the top-level entry point for the tool's analyzer.

```python
from datalink_decorators import data_consumer, TriggerType

@data_consumer(service_name="order-processing-service")
class OrderProcessor:
    # ... your trigger methods go here
    pass
```

## 2. Defining a Trigger with `@trigger`

Decorate any method inside your `@data_consumer` class with a `@trigger` decorator to expose it as a service entry point. The first parameter of the method is always the data contract (e.g., a dictionary or a dataclass).

The TriggerType enum provides a list of supported event sources.

```python
from datalink_decorators import data_consumer, trigger, TriggerType
from my_dtos import OrderEvent # Your DTO class

@data_consumer
class OrderProcessor:

    # This method is triggered by a message on an AWS SQS Queue.
    @trigger(type=TriggerType.AWS_SQS_QUEUE, name="new-orders")
    def handle_new_order(self, order: OrderEvent, db_connection):
        # Your business logic here...
        # db_connection.execute(...)
        pass

    # This method is triggered by an HTTP POST request.
    @trigger(type=TriggerType.HTTP, name="orders/manual-entry")
    def handle_manual_order(self, order: OrderEvent, logger):
        logger.info("Manual order received.")
        # ...
        pass
```

## 3. Requiring a Pre-Processing Gate with `@requires`

Use `@requires` to enforce a gatekeeper, like an authentication check, before your main logic is executed. The handler should be a reference to a callable function that returns a boolean.

```python
# In your shared hooks library (e.g., hooks/auth.py)
def validate_request(request):
    # ... check JWT ...
    return True

# In your business logic
from hooks.auth import validate_request

@data_consumer
class AdminService:

    @trigger(type=TriggerType.HTTP, name="admin/run-job")
    @requires(handler=validate_request)
    def run_admin_job(self, payload: dict):
        # This code only runs if validate_request returns True.
        pass
```

## 4. Requiring Logging with `@requires_logger`

Use `@requires_logger` to declaratively add observability. The tool will automatically wrap your trigger method in a try/except block and call your specified logger function.

The LoggingAction enum specifies when to log:
- **ON_INBOUND**: Logs the incoming data at the start.
- **ON_ERROR**: Logs the exception in the except block.
- **ON_OUTBOUND**: Logs the return value of your method upon success.

```python
# In your shared logging library (e.g., loggers/splunk.py)
def log_event(payload=None, error=None):
    # Logic to send data to Splunk
    pass

# In your business logic
from loggers.splunk import log_event

@data_consumer
class OrderProcessor:

    @trigger(type=TriggerType.HTTP, name="orders")
    @requires_logger(
        handler=log_event,
        action=LoggingAction.ON_INBOUND
    )
    @requires_logger(
        handler=log_event,
        action=LoggingAction.ON_ERROR
    )
    def handle_new_order(self, order: dict):
        # ... your logic ...
        pass
```

## 5. Trace Logging with `@requires_results_logger`

Use `@requires_results_logger` to get deep insight into your method's execution by logging the state of local variables. This feature is more limited in dynamically typed languages like Python compared to C#.

```python
@data_consumer
class ComplexWorkflow:

    @trigger(type=TriggerType.HTTP, name="workflows/start")
    @requires_results_logger(
        handler=log_event,
        variable="validated"
    )
    @requires_results_logger(
        handler=log_event,
        variable="enriched"
    )
    def start_workflow(self, payload: dict):
        validated = self.validate(payload)
        # The tool will inject a call to log_event(payload=validated) here.

        enriched = self.enrich(validated)
        # The tool will inject a call to log_event(payload=enriched) here.

        self.save(enriched)

    def validate(self, payload):
        # Validation logic
        return payload

    def enrich(self, validated_payload):
        # Enrichment logic
        return validated_payload

    def save(self, enriched_payload):
        # Save logic
        pass
```