# 3SC Conduit: Go Developer Guide

This guide provides Go-specific examples for using `3SC Conduit` comment directives to define and generate event-driven services.

Go does not have annotations or decorators. Instead, Conduit parses structured comments placed directly above your types and functions. To get started, download the `datalink.go` helper file from the running Conduit container at `/code/go`.

## 1. Defining a Service with `// @DataConsumer`

Mark a `struct` that will contain your business logic with the `@DataConsumer` comment directive. This is the top-level entry point for the tool's analyzer.

```go
package handlers

// @DataConsumer serviceName="order-processing-service"
type OrderProcessor struct {
    // ... dependencies would be defined here
    // DB *sql.DB
}
```

## 2. Defining a Trigger with `// @Trigger`

Place a `@Trigger` comment directive directly above a public method of your DataConsumer struct to expose it as a service entry point. The first parameter of the method is always the data contract (DTO).

```go
package handlers

import "my-project/dtos"

// @DataConsumer
type OrderProcessor struct{}

// @Trigger type="AwsSqsQueue" name="new-orders"
func (p *OrderProcessor) HandleNewOrder(order dtos.OrderEvent, db *sql.DB) error {
    // Your business logic here...
    // _, err := db.Exec(...)
    return nil
}

// @Trigger type="Http" name="/orders/manual-entry"
func (p *OrderProcessor) HandleManualOrder(order dtos.OrderEvent, log *log.Logger) error {
    log.Println("Manual order received.")
    // ...
    return nil
}
```

## 3. Requiring a Pre-Processing Gate with `// @Requires`

Use `@Requires` to enforce a gatekeeper, like an authentication check, before your main logic is executed. The handler should be a reference to a function that returns a boolean.

```go
// In your shared hooks package (e.g., hooks/auth.go)
package hooks

func ValidateRequest(req events.APIGatewayProxyRequest) (bool, error) {
    // ... check JWT ...
    return true, nil
}

// In your business logic
package handlers

import "my-project/hooks"

// @DataConsumer
type AdminService struct{}

// @Trigger type="Http" name="/admin/run-job"
// @Requires handler="hooks.ValidateRequest"
func (s *AdminService) RunAdminJob(payload dtos.AdminJobPayload) error {
    // This code only runs if hooks.ValidateRequest returns true.
    return nil
}
```

**Note**: The handler is specified as a string "packageName.FunctionName".

## 4. Requiring Logging with `// @RequiresLogger`

Use `@RequiresLogger` to declaratively add observability. The tool will automatically wrap your trigger method in a try/catch-equivalent block (using Go's panic/recover) and call your specified logger function.

The action specifies when to log:
- **OnInbound**: Logs the incoming DTO at the start.
- **OnError**: Logs the error if one occurs.
- **OnOutbound**: Logs the return value of your method upon success.

```go
// In your shared logging library (e.g., loggers/splunk.go)
package loggers

func LogEvent(payload interface{}, err error) {
    // Logic to send data to Splunk
}

// In your business logic
package handlers

import "my-project/loggers"

// @DataConsumer
type OrderProcessor struct{}

// @Trigger type="Http" name="/orders"
// @RequiresLogger handler="loggers.LogEvent" action="OnInbound"
// @RequiresLogger handler="loggers.LogEvent" action="OnError"
func (p *OrderProcessor) HandleNewOrder(order dtos.OrderEvent) error {
    // ... your logic ...
    return nil
}
```

## 5. Trace Logging with `// @RequiresResultsLogger`

Use `@RequiresResultsLogger` to get deep insight into your method's execution by logging the state of local variables. This feature is more limited in compiled languages like Go.

```go
// @DataConsumer
type ComplexWorkflow struct{}

// @Trigger type="Http" name="/workflows/start"
// @RequiresResultsLogger handler="loggers.LogEvent" variable="validated"
// @RequiresResultsLogger handler="loggers.LogEvent" variable="enriched"
func (w *ComplexWorkflow) StartWorkflow(payload dtos.InitialPayload) error {
    validated, err := w.validate(payload)
    // The tool will inject a call to loggers.LogEvent(validated, nil) here.
    if err != nil {
        return err
    }

    enriched, err := w.enrich(validated)
    // The tool will inject a call to loggers.LogEvent(enriched, nil) here.
    if err != nil {
        return err
    }

    w.save(enriched)
    return nil
}

func (w *ComplexWorkflow) validate(payload dtos.InitialPayload) (dtos.ValidatedPayload, error) {
    // Validation logic
    return dtos.ValidatedPayload{}, nil
}

func (w *ComplexWorkflow) enrich(payload dtos.ValidatedPayload) (dtos.EnrichedPayload, error) {
    // Enrichment logic
    return dtos.EnrichedPayload{}, nil
}

func (w *ComplexWorkflow) save(payload dtos.EnrichedPayload) {
    // Save logic
}
```