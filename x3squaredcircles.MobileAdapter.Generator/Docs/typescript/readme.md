# Mobile Adapter Generator: TypeScript Guide

This guide provides instructions for using the Mobile Adapter Generator with a TypeScript codebase. The discovery mechanism for TypeScript is **source code analysis**, where the tool inspects your `.ts` files.

## 1. Create the Tracking Decorator

The generator identifies which classes to process by looking for a specific decorator. **You must define this decorator in your TypeScript project.** Decorators are functions, and this simple implementation ensures the generator has zero compile-time dependencies on your code.

Create the following file in your project. The file/module name is not important, but the exported function name (`TrackableDTO`) is.

### `decorators.ts`

```typescript
/**
 * Interface describing the optional parameters for the TrackableDTO decorator.
 */
interface TrackableDTOOptions {
  /**
   * A logical name for the target model that will be generated.
   * This name can contain placeholders (e.g., "{MyModelName}") that will be
   * resolved by environment variables at generation time.
   * If not set, the original class name will be used.
   */
  targetName?: string;
}

/**
 * A class decorator that marks a class for discovery by the Mobile Adapter Generator.
 * This is a no-op decorator at runtime; its only purpose is to provide a static
 * marker and metadata for the generator's parsing process.
 * @param options - Optional configuration for the generator.
 */
export function TrackableDTO(options?: TrackableDTOOptions): ClassDecorator {
  // eslint-disable-next-line @typescript-eslint/no-empty-function
  return (target: object) => {};
}
```

You can also download this file directly from the DX server: `http://localhost:8080/typescript` (when the container is running).

## 2. Apply the Decorator to Your DTOs

Apply the `@TrackableDTO` decorator to any exported class you want the generator to process.

### Example without Placeholders:

This will generate a mobile model named `UserProfile`.

```typescript
import { TrackableDTO } from './decorators';

@TrackableDTO()
export class UserProfile {
  userId: string; // Assuming UUID is serialized as string
  fullName: string;
  isActive: boolean;
}
```

### Example with Placeholder Resolution:

This allows the CI/CD pipeline to control the name of the generated model.

```typescript
import { TrackableDTO } from './decorators';

@TrackableDTO({ targetName: '{userModelName}' })
export class User {
  id: string;
  email: string;
}
```

## 3. Configure the Generator

In your CI/CD pipeline, you will configure the generator to find your source code and process the decorated classes.

### Environment Variables for TypeScript:

| Variable | Description | Example |
|----------|-------------|---------|
| `ADAPTERGEN_LANGUAGE_TYPESCRIPT` | **Required.** Tells the generator to use the TypeScript discovery engine. | `true` |
| `ADAPTERGEN_TRACK_ATTRIBUTE` | **Required.** The name of the decorator function to look for. | `TrackableDTO` |
| `ADAPTERGEN_SOURCE_PATHS` | **Required.** Path inside the container to the directory containing your `.ts` source files. | `/src/my-api-service/src/models` |
| `ADAPTERGEN_CUSTOM_USERMODELNAME` | **(If using placeholders).** The value for the `{userModelName}` placeholder. | `CustomerProfileV2` |

### Example `docker run` command:

This command runs the generator, targeting an Android/Kotlin output. It assumes your repository is mounted at `/src`.

```bash
docker run --rm \
  -v $(pwd):/src \
  -e ADAPTERGEN_LANGUAGE_TYPESCRIPT=true \
  -e ADAPTERGEN_PLATFORM_ANDROID=true \
  -e ADAPTERGEN_TRACK_ATTRIBUTE="TrackableDTO" \
  -e ADAPTERGEN_SOURCE_PATHS="/src/my-api-service/src/models" \
  -e ADAPTERGEN_ANDROID_PACKAGE_NAME="com.mycompany.models" \
  -e ADAPTERGEN_CUSTOM_USERMODELNAME="CustomerProfileV2" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  3sc/mobile-adapter-generator:latest
```

When this command runs against the placeholder example above, it will generate a Kotlin data class named `CustomerProfileV2.kt`.