# 3SC API Assembler: Go Quick Start & Examples

This guide provides practical examples of how to use the 3SC Assembler comment directives in a Go business logic project.

## 1. Getting Started

1. **Reference the DSL:** Keep the `three_sc_dsl.go` file handy as a reference for the correct syntax.
2. **Annotate Your Code:** Add comment directives (`// @Directive ...`) on the line immediately preceding your structs and methods to define your services and function entry points.

## 2. Core Concepts with Examples

### `@FunctionHandler`

This directive marks a struct as a container for function entry points. The Assembler will scan any struct preceded by this comment.

```go
package handlers

// @FunctionHandler
type OrderProcessingHandler struct {
    // ... dependencies like database connections go here
}
```

### `@DeploymentGroup`

This directive defines a deployable microservice. It groups related functions together and can be applied to a struct or a single method.

```go
package handlers

// @DeploymentGroup serviceName="OrderServices" version="v2.1"
// @FunctionHandler
type OrderProcessingHandler struct {
    OrderRepo repositories.IOrderRepository
}

// This method is part of the "OrderServices" v2.1 deployment group.
// @EventSource eventUrn="aws:sqs:{newOrdersQueue}:MessageReceived"
func (h *OrderProcessingHandler) ProcessNewOrder(sqsEvent events.SQSEvent) error {
    // ... logic ...
    return nil
}

// This method OVERRIDES the struct-level group.
// @DeploymentGroup serviceName="InternalTools" version="v1"
// @EventSource eventUrn="aws:apigateway:proxy:/tools/reprocess-order/{id}:POST"
func (h *OrderProcessingHandler) ReprocessOrder(request events.APIGatewayProxyRequest) (events.APIGatewayProxyResponse, error) {
    // ... logic ...
    return events.APIGatewayProxyResponse{StatusCode: 200}, nil
}
```

### `@EventSource`

This is the primary directive. It marks a method as a function entry point and defines its trigger with a URN. Placeholders like `{customerUploadsBucket}` are replaced by pipeline variables.

#### AWS S3 PUT Event:

```go
// @EventSource eventUrn="aws:s3:{customerUploadsBucket}:ObjectCreated:Put"
func (h *FileHandler) HandleNewUpload(s3Event events.S3Event) error {
    // ... logic to process the new file ...
    return nil
}
```

#### Azure HTTP GET Event:

```go
// @EventSource eventUrn="azure:apigateway:proxy:/products/{id}:GET"
func (h *ProductHandler) GetProductByID(req messages.HttpRequest) (*messages.HttpResponse, error) {
    // ... logic to fetch a product ...
    return &messages.HttpResponse{Body: "product data"}, nil
}
```

#### GCP Pub/Sub Event:

```go
// @EventSource eventUrn="gcp:pubsub:{newProductsTopic}:MessagePublished"
func (h *ProductHandler) OnNewProductPublished(ctx context.Context, m event.Message) error {
    // ... logic to handle the Pub/Sub message ...
    return nil
}
```

## 3. Weaving Cross-Cutting Concerns

### `@Requires`

Use this to inject a pre-processing gate. The specified handler function should have a signature like `func(payload interface{}) (bool, error)`. If it returns `false`, the main business logic is not executed.

```go
// In a separate "hooks/security.go" package...
package hooks

func ValidateJwt(req events.APIGatewayProxyRequest) (bool, error) {
    // logic to validate the JWT from the request header
    log.Println("Validating JWT...")
    return true, nil // or false, nil
}

// In your Function Handler...
// @FunctionHandler
type AdminPanelHandler struct{}

// @Requires handler="github.com/my-org/project/hooks.ValidateJwt"
// @EventSource eventUrn="aws:apigateway:proxy:/admin/dashboard:GET"
func (h *AdminPanelHandler) GetDashboard(req events.APIGatewayProxyRequest) (events.APIGatewayProxyResponse, error) {
    // This code only runs if ValidateJwt returns true.
    return events.APIGatewayProxyResponse{StatusCode: 200}, nil
}
```

### `@RequiresLogger`

Use this to inject observability and create an audit trail.

- **action="OnInbound":** Logs the request payload before your business logic runs.
- **action="OnError":** Logs any exception that occurs during the pipeline.

```go
// In a separate "hooks/auditing.go" package...
package hooks

func LogRequest(payload interface{}) error {
    // logic to serialize and log the incoming payload
    log.Printf("AUDIT - Inbound Payload: %+v", payload)
    return nil
}

func LogFailure(err error) error {
    // logic to log critical failure details
    log.Printf("AUDIT - Critical Failure: %v", err)
    return nil
}

// In your Function Handler...
// @FunctionHandler
type PaymentProcessingHandler struct{}

// @RequiresLogger handler="github.com/my-org/project/hooks.LogRequest" action="OnInbound"
// @RequiresLogger handler="github.com/my-org/project/hooks.LogFailure" action="OnError"
// @EventSource eventUrn="aws:sqs:{paymentQueue}:MessageReceived"
func (h *PaymentProcessingHandler) ProcessPayment(message events.SQSMessage) error {
    // Business logic that might return an error...
    return fmt.Errorf("payment gateway timed out")
}
```