# Mobile Adapter Generator: JavaScript Guide

This guide provides instructions for using the Mobile Adapter Generator with a JavaScript (ES6+) codebase. The discovery mechanism for JavaScript is **source code analysis**, where the tool inspects your `.js` files for classes preceded by a special JSDoc comment block.

## 1. Understand the JSDoc Directive

The generator identifies which classes to process by looking for a specific tag within a JSDoc comment block: `@TrackableDTO`. **There is no code to create or import.** You simply add a formatted comment.

Custom parameters for the generator, like `targetName`, are added as their own JSDoc tags.

### JSDoc Block Format:

```javascript
/**
 * A description of your class.
 * @TrackableDTO
 * @targetName {userModelName}
 */
```

## 2. Apply the JSDoc Block to Your DTOs

Add the JSDoc block directly above any ES6 class you want the generator to process.

### Example without Placeholders:

This will generate a mobile model named `UserProfile`.

```javascript
/**
 * Represents a user's public profile.
 * @TrackableDTO
 */
class UserProfile {
    constructor() {
        this.userId = null; // Type will be inferred as 'any'
        this.fullName = ""; // Type will be inferred as 'string'
        this.isActive = false; // Type will be inferred as 'boolean'
    }
}
```

### Example with Placeholder Resolution:

This allows the CI/CD pipeline to control the name of the generated model using the `@targetName` tag.

```javascript
/**
 * Core user account information.
 * @TrackableDTO
 * @targetName {userModelName}
 */
class User {
    constructor() {
        this.id = null;
        this.email = "";
    }
}
```

## 3. Configure the Generator

In your CI/CD pipeline, you will configure the generator to find your source code and process the classes with the JSDoc directives.

### Environment Variables for JavaScript:

| Variable | Description | Example |
|----------|-------------|---------|
| `ADAPTERGEN_LANGUAGE_JAVASCRIPT` | **Required.** Tells the generator to use the JavaScript discovery engine. | `true` |
| `ADAPTERGEN_TRACK_ATTRIBUTE` | **Required.** The name of the JSDoc tag to look for. | `TrackableDTO` |
| `ADAPTERGEN_SOURCE_PATHS` | **Required.** Path inside the container to the directory containing your `.js` source files. | `/src/my-api-service/src/models` |
| `ADAPTERGEN_CUSTOM_USERMODELNAME` | **(If using placeholders).** The value for the `{userModelName}` placeholder. | `CustomerProfileV2` |

### Example `docker run` command:

This command runs the generator, targeting an iOS/Swift output. It assumes your repository is mounted at `/src`.

```bash
docker run --rm \
  -v $(pwd):/src \
  -e ADAPTERGEN_LANGUAGE_JAVASCRIPT=true \
  -e ADAPTERGEN_PLATFORM_IOS=true \
  -e ADAPTERGEN_TRACK_ATTRIBUTE="TrackableDTO" \
  -e ADAPTERGEN_SOURCE_PATHS="/src/my-api-service/src/models" \
  -e ADAPTERGEN_IOS_MODULE_NAME="MyApiModels" \
  -e ADAPTERGEN_CUSTOM_USERMODELNAME="CustomerProfileV2" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  3sc/mobile-adapter-generator:latest
```

When this command runs against the placeholder example above, it will generate a Swift struct named `CustomerProfileV2.swift`.