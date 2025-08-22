# Mobile Adapter Generator: Go Guide

This guide provides instructions for using the Mobile Adapter Generator with a Go codebase. The discovery mechanism for Go is **source code analysis**, where the tool inspects your `.go` files for special comment directives.

## 1. Understand the Comment Directive

The generator identifies which structs to process by looking for a specific comment directive immediately preceding the `type` definition. There is **no code to create or import**. You simply add a formatted comment.

The required format is `// @<TrackAttribute> [parameters]`.

The `@` symbol is critical. The name you use for the track attribute must match the `ADAPTERGEN_TRACK_ATTRIBUTE` environment variable.

## 2. Apply the Directive to Your Structs

Add the comment directive directly above any `struct` you want the generator to process. The generator will analyze the exported fields of the struct.

### Example without Placeholders:

This will generate a mobile model named `UserProfile`.

```go
package models

import "github.com/google/uuid"

// @TrackableDTO
type UserProfile struct {
	UserID   uuid.UUID `json:"userId"`
	FullName string    `json:"fullName"`
	IsActive bool      `json:"isActive"`
}
```

### Example with Placeholder Resolution:

This allows the CI/CD pipeline to control the name of the generated model using a `targetName` parameter.

```go
package models

import "github.com/google/uuid"

// @TrackableDTO targetName="{userModelName}"
type User struct {
	ID    uuid.UUID `json:"id"`
	Email string    `json:"email"`
}
```

## 3. Configure the Generator

In your CI/CD pipeline, you will configure the generator to find your source code and process the structs with comment directives.

### Environment Variables for Go:

| Variable | Description | Example |
|----------|-------------|---------|
| `ADAPTERGEN_LANGUAGE_GO` | **Required.** Tells the generator to use the Go discovery engine. | `true` |
| `ADAPTERGEN_TRACK_ATTRIBUTE` | **Required.** The name of the comment directive to look for. | `TrackableDTO` |
| `ADAPTERGEN_SOURCE_PATHS` | **Required.** Path inside the container to the directory containing your `.go` source files. | `/src/my-api-service/models` |
| `ADAPTERGEN_CUSTOM_USERMODELNAME` | **(If using placeholders).** The value for the `{userModelName}` placeholder. | `CustomerProfileV2` |

### Example `docker run` command:

This command runs the generator, targeting an iOS/Swift output. It assumes your repository is mounted at `/src`.

```bash
docker run --rm \
  -v $(pwd):/src \
  -e ADAPTERGEN_LANGUAGE_GO=true \
  -e ADAPTERGEN_PLATFORM_IOS=true \
  -e ADAPTERGEN_TRACK_ATTRIBUTE="TrackableDTO" \
  -e ADAPTERGEN_SOURCE_PATHS="/src/my-api-service/models" \
  -e ADAPTERGEN_IOS_MODULE_NAME="MyApiModels" \
  -e ADAPTERGEN_CUSTOM_USERMODELNAME="CustomerProfileV2" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  3sc/mobile-adapter-generator:latest
```

When this command runs against the placeholder example above, it will generate a Swift struct named `CustomerProfileV2.swift`.