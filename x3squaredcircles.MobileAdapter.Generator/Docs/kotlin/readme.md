# Mobile Adapter Generator: Kotlin Guide

This guide provides instructions for using the Mobile Adapter Generator with a Kotlin backend codebase. The discovery mechanism for Kotlin is **source code analysis**, where the tool inspects your `.kt` files.

## 1. Create the Tracking Annotation

The generator identifies which classes to process by looking for a specific annotation. **You must define this annotation in your Kotlin project.** This approach ensures the generator has zero compile-time dependencies on your code.

Create the following file in your project. The package is not important, but the annotation name (`TrackableDTO`) and property name (`targetName`) are.

### `TrackableDTO.kt`

```kotlin
package com.mycompany.annotations // Your package here

/**
 * Marks a class as a Data Transfer Object (DTO) to be discovered by the
 * Mobile Adapter Generator.
 */
@Retention(AnnotationRetention.SOURCE)
@Target(AnnotationTarget.CLASS)
annotation class TrackableDTO(
    /**
     * A logical name for the target model that will be generated.
     * This name can contain placeholders (e.g., "{MyModelName}") that will be
     * resolved by environment variables at generation time.
     * If not set, the original class name will be used.
     */
    val targetName: String = ""
)
```

## 2. Apply the Annotation to Your DTOs

Apply the `@TrackableDTO` annotation to any class (including data classes) you want the generator to process.

### Example without Placeholders:

This will generate a mobile model named `UserProfile`.

```kotlin
import com.mycompany.annotations.TrackableDTO

@TrackableDTO
data class UserProfile(
    val userId: java.util.UUID,
    val fullName: String,
    val isActive: Boolean
)
```

### Example with Placeholder Resolution:

This allows the CI/CD pipeline to control the name of the generated model.

```kotlin
import com.mycompany.annotations.TrackableDTO

@TrackableDTO(targetName = "{userModelName}")
data class User(
    val id: java.util.UUID,
    val email: String
)
```

## 3. Configure the Generator

In your CI/CD pipeline, you will configure the generator to find your source code and process the annotated classes.

### Environment Variables for Kotlin:

| Variable | Description | Example |
|----------|-------------|---------|
| `ADAPTERGEN_LANGUAGE_KOTLIN` | **Required.** Tells the generator to use the Kotlin discovery engine. | `true` |
| `ADAPTERGEN_TRACK_ATTRIBUTE` | **Required.** The name of the annotation class to look for. | `TrackableDTO` |
| `ADAPTERGEN_SOURCE_PATHS` | **Required.** Path inside the container to the directory containing your `.kt` source files. | `/src/my-api-service/src/main/kotlin` |
| `ADAPTERGEN_CUSTOM_USERMODELNAME` | **(If using placeholders).** The value for the `{userModelName}` placeholder. | `CustomerProfileV2` |

### Example `docker run` command:

This command runs the generator, targeting an iOS/Swift output. It assumes your repository is mounted at `/src`.

```bash
docker run --rm \
  -v $(pwd):/src \
  -e ADAPTERGEN_LANGUAGE_KOTLIN=true \
  -e ADAPTERGEN_PLATFORM_IOS=true \
  -e ADAPTERGEN_TRACK_ATTRIBUTE="TrackableDTO" \
  -e ADAPTERGEN_SOURCE_PATHS="/src/my-api-service/src/main/kotlin" \
  -e ADAPTERGEN_IOS_MODULE_NAME="MyApiModels" \
  -e ADAPTERGEN_CUSTOM_USERMODELNAME="CustomerProfileV2" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  3sc/mobile-adapter-generator:latest
```

When this command runs against the placeholder example above, it will generate a Swift struct named `CustomerProfileV2.swift`.