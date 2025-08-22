# Mobile Adapter Generator: Python Guide

This guide provides instructions for using the Mobile Adapter Generator with a Python codebase. The discovery mechanism for Python is **source code analysis**, where the tool inspects your `.py` files.

## 1. Create the Tracking Decorator

The generator identifies which classes to process by looking for a specific decorator. **You must define this decorator in your Python project.** This approach ensures the generator has zero compile-time dependencies on your code.

Create the following file in your project. The file/module name is not important, but the function name (`TrackableDTO`) is.

### `decorators.py`

```python
def TrackableDTO(target_name=None):
    """
    A class decorator that marks a class for discovery by the Mobile Adapter Generator.
    This is a no-op decorator at runtime; its only purpose is to provide a static
    marker and metadata for the generator's parsing process.
    
    :param target_name: Optional. A logical name for the target model. Can contain
                        placeholders like "{MyModelName}".
    """
    def wrapper(cls):
        # The decorator itself doesn't need to do anything to the class at runtime.
        # It's just a marker for our static analysis tool.
        return cls
    return wrapper
```

## 2. Apply the Decorator to Your DTOs

Apply the `@TrackableDTO` decorator to any class you want the generator to process. The generator will analyze properties with type hints.

### Example without Placeholders:

This will generate a mobile model named `UserProfile`.

```python
from .decorators import TrackableDTO

@TrackableDTO()
class UserProfile:
    user_id: str  # Assuming UUID is serialized as a string
    full_name: str
    is_active: bool
```

### Example with Placeholder Resolution:

This allows the CI/CD pipeline to control the name of the generated model.

```python
from .decorators import TrackableDTO

@TrackableDTO(target_name="{userModelName}")
class User:
    id: str
    email: str
```

## 3. Configure the Generator

In your CI/CD pipeline, you will configure the generator to find your source code and process the decorated classes.

### Environment Variables for Python:

| Variable | Description | Example |
|----------|-------------|---------|
| `ADAPTERGEN_LANGUAGE_PYTHON` | **Required.** Tells the generator to use the Python discovery engine. | `true` |
| `ADAPTERGEN_TRACK_ATTRIBUTE` | **Required.** The name of the decorator function to look for. | `TrackableDTO` |
| `ADAPTERGEN_PYTHON_PATHS` | **Required.** Path inside the container to the directory containing your `.py` source files. | `/src/my-api-service/src/models` |
| `ADAPTERGEN_CUSTOM_USERMODELNAME` | **(If using placeholders).** The value for the `{userModelName}` placeholder. | `CustomerProfileV2` |

### Example `docker run` command:

This command runs the generator, targeting an Android/Kotlin output. It assumes your repository is mounted at `/src`.

```bash
docker run --rm \
  -v $(pwd):/src \
  -e ADAPTERGEN_LANGUAGE_PYTHON=true \
  -e ADAPTERGEN_PLATFORM_ANDROID=true \
  -e ADAPTERGEN_TRACK_ATTRIBUTE="TrackableDTO" \
  -e ADAPTERGEN_PYTHON_PATHS="/src/my-api-service/src/models" \
  -e ADAPTERGEN_ANDROID_PACKAGE_NAME="com.mycompany.models" \
  -e ADAPTERGEN_CUSTOM_USERMODELNAME="CustomerProfileV2" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  3sc/mobile-adapter-generator:latest
```

When this command runs against the placeholder example above, it will generate a Kotlin data class named `CustomerProfileV2.kt`.