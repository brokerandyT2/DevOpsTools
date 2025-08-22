# Mobile Adapter Generator

The Mobile Adapter Generator is a command-line tool designed to accelerate mobile development by automatically generating data transfer objects (DTOs) or models for target mobile platforms (Android/Kotlin, iOS/Swift) from your existing backend source code.

It operates on the principle of **Declarative Intent in Code**. You, the developer, are the source of truth. You declare which classes should be transformed directly in your source code using language-idiomatic constructs (like Attributes or Decorators), and the generator handles the rest.

## Core Concepts

The generator's behavior is governed by a strict set of architectural principles, ensuring a consistent, secure, and extensible experience.

### 1. Configuration: Environment-Driven Supremacy

**All configuration is provided via environment variables.** There are no configuration files. This makes the tool's behavior explicit and auditable directly within your CI/CD pipeline definition.

- **Tool-Specific Variables:** Use the `ADAPTERGEN_` prefix (e.g., `ADAPTERGEN_LANGUAGE_CSHARP=true`).
- **Universal Variables:** Use the `3SC_` prefix for cross-cutting concerns (e.g., `3SC_LICENSE_SERVER`).
- **Precedence:** A tool-specific variable will always override a universal one.

#### Key Variables:

| Variable | Description | Example |
|----------|-------------|---------|
| `ADAPTERGEN_LANGUAGE_<LANG>` | Sets the source language to analyze. (e.g., CSHARP, JAVA) | `ADAPTERGEN_LANGUAGE_CSHARP=true` |
| `ADAPTERGEN_PLATFORM_<PLAT>` | Sets the target platform for code generation. (e.g., ANDROID, IOS) | `ADAPTERGEN_PLATFORM_ANDROID=true` |
| `ADAPTERGEN_TRACK_ATTRIBUTE` | The name of the Attribute/Decorator/Comment Directive to discover. | `ADAPTERGEN_TRACK_ATTRIBUTE=TrackableDTO` |
| `ADAPTERGEN_SOURCE_PATHS` | A semicolon-separated list of paths to your source code. | `ADAPTERGEN_SOURCE_PATHS=/src/MyProject.Api` |
| `ADAPTERGEN_ANDROID_PACKAGE_NAME` | The Kotlin package name for generated Android files. | `com.mycompany.mobile.models` |
| `ADAPTERGEN_CUSTOM_<PLACEHOLDER>` | A user-defined variable for placeholder resolution. | `ADAPTERGEN_CUSTOM_USERMODEL=CustomerV2` |

### 2. The Execution Environment: Docker

The tool is delivered as a single, self-contained Docker image (`3sc/mobile-adapter-generator:latest`). It includes all necessary runtimes.

#### Example `docker run` command:

```bash
docker run --rm \
  -v /path/to/your/repo:/src \
  -p 8080:8080 \
  -e ADAPTERGEN_LANGUAGE_CSHARP=true \
  -e ADAPTERGEN_PLATFORM_ANDROID=true \
  -e ADAPTERGEN_TRACK_ATTRIBUTE="TrackableDTO" \
  -e ADAPTERGEN_SOURCE_PATHS="/src/MyApi" \
  -e ADAPTERGEN_ANDROID_PACKAGE_NAME="com.mycompany.models" \
  -e ADAPTERGEN_CUSTOM_USERMODEL="CustomerV2" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  3sc/mobile-adapter-generator:latest
```

### 3. Developer Experience: Declarative Intent

You declare your intent in your source code. The primary mechanism is a Domain-Specific Language (DSL) in the form of attributes, decorators, or special comments.

This DSL supports placeholder resolution to decouple your logic from physical environments. A value like `{myPlaceholder}` in your code will be replaced by the value of the `ADAPTERGEN_CUSTOM_MYPLACEHOLDER` environment variable at runtime.

#### Conceptual Example (TypeScript):

```typescript
// With this code...
@TrackableDTO({ targetName: '{userModelName}' })
export class User {
  // ... properties
}

// ...and this environment variable...
// ADAPTERGEN_CUSTOM_USERMODELNAME=CustomerProfile

// ...the generator will create a model named 'CustomerProfile'.
```

### 4. Extensibility: The Control Point Architecture

The tool provides "escape hatches" via webhooks for integration with other systems. You can configure a URL for specific lifecycle events, and the tool will POST a JSON payload to that URL.

**Variable Format:** `ADAPTERGEN_CP_{STAGE}_{EVENT}`

**Example:** Set `ADAPTERGEN_CP_COMPLETION_ONSUCCESS=https://my-dashboard/api/notify` to send a notification when the tool successfully completes.

### 5. Standardized Observability: The DX Server

While running, the container exposes a Developer Experience (DX) Server on port 8080.

- `/health`: A simple health check endpoint.
- `/docs`: The main documentation page, which provides links to language-specific guides and helper files.

You can access it at `http://localhost:8080/docs` when running the container with the port mapped (`-p 8080:8080`).

## Language-Specific Guides

For detailed instructions on how to implement the DSL in your project's source language, including code examples and helper file downloads, please see the guides below. You will need to create your own attribute/decorator class as shown in these guides.

- [C# Guide](#)
- [Java Guide](#)
- [Kotlin Guide](#)
- [TypeScript Guide](#)
- [Python Guide](#)
- [Go Guide](#)
- [JavaScript Guide](#)