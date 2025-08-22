# Mobile Adapter Generator: C# Guide

This guide provides instructions for using the Mobile Adapter Generator with a C# codebase. The primary discovery mechanism for C# is **assembly analysis**, where the tool inspects the compiled `.dll` files of your project.

## 1. Create the Tracking Attribute

The generator identifies which classes to process by looking for a specific attribute. **You must define this attribute in your C# project.** This approach ensures the generator has zero compile-time dependencies on your code.

Create the following file in your project. The namespace is not important, but the class name (`TrackableDTOAttribute`) and property names (`TargetName`) are.

### `TrackableDTOAttribute.cs`

```csharp
using System;

/// <summary>
/// Marks a class as a Data Transfer Object (DTO) to be discovered by the
/// Mobile Adapter Generator.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TrackableDTOAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a logical name for the target model that will be generated.
    /// This name can contain placeholders (e.g., "{MyModelName}") that will be
    /// resolved by environment variables at generation time.
    /// If not set, the original class name will be used.
    /// </summary>
    public string TargetName { get; set; }

    public TrackableDTOAttribute()
    {
    }
}
```

## 2. Apply the Attribute to Your DTOs

Apply the `[TrackableDTO]` attribute to any class you want the generator to process.

### Example without Placeholders:

This will generate a mobile model named `UserProfile`.

```csharp
[TrackableDTO]
public class UserProfile
{
    public Guid UserId { get; set; }
    public string FullName { get; set; }
    public bool IsActive { get; set; }
}
```

### Example with Placeholder Resolution:

This allows the CI/CD pipeline to control the name of the generated model.

```csharp
[TrackableDTO(TargetName = "{userModelName}")]
public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; }
}
```

## 3. Configure the Generator

In your CI/CD pipeline, you will configure the generator to find your compiled assemblies and process the attributed classes.

### Environment Variables for C#:

| Variable | Description | Example |
|----------|-------------|---------|
| `ADAPTERGEN_LANGUAGE_CSHARP` | **Required.** Tells the generator to use the C# discovery engine. | `true` |
| `ADAPTERGEN_TRACK_ATTRIBUTE` | **Required.** The name of the attribute class to look for. | `TrackableDTO` or `TrackableDTOAttribute` |
| `ADAPTERGEN_TARGET_ASSEMBLY_PATH` | **Required.** Path inside the container to the directory containing your compiled project DLLs. | `/src/MyProject.Api/bin/Release/net8.0` |
| `ADAPTERGEN_CUSTOM_USERMODELNAME` | **(If using placeholders).** The value for the `{userModelName}` placeholder. | `CustomerProfileV2` |

### Example `docker run` command:

This command runs the generator, targeting an Android/Kotlin output. It assumes your repository is mounted at `/src`.

```bash
docker run --rm \
  -v $(pwd):/src \
  -e ADAPTERGEN_LANGUAGE_CSHARP=true \
  -e ADAPTERGEN_PLATFORM_ANDROID=true \
  -e ADAPTERGEN_TRACK_ATTRIBUTE="TrackableDTO" \
  -e ADAPTERGEN_TARGET_ASSEMBLY_PATH="/src/MyProject.Api/bin/Release/net8.0" \
  -e ADAPTERGEN_ANDROID_PACKAGE_NAME="com.mycompany.models" \
  -e ADAPTERGEN_CUSTOM_USERMODELNAME="CustomerProfileV2" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  3sc/mobile-adapter-generator:latest
```

When this command runs against the placeholder example above, it will generate a Kotlin data class named `CustomerProfileV2.kt`.