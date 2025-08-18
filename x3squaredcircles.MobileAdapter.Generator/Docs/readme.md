# Mobile Adapter Generator - Documentation

Welcome to the documentation for the **3SC Mobile Adapter Generator**. This tool is designed to automate the creation of type-safe data models for your native mobile applications, ensuring they stay perfectly synchronized with your backend or shared data model library.

## Core Concept

The generator operates on the principle of a **"single source of truth."** You define your data models once in your primary language (e.g., C#, TypeScript, Go), and the generator analyzes this source to produce the equivalent, idiomatic models for your mobile targets:

-   **Android:** Generates Kotlin `data class` files.
-   **iOS:** Generates Swift `struct` files that conform to `Codable`.

This eliminates manual model creation, prevents common serialization bugs, and accelerates development by removing a tedious and error-prone step.

---

## 🚨 Important: One Execution Per Platform

To ensure full forensic traceability, **you must run the generator separately for each mobile platform you target.** Each run creates a distinct, auditable report for a single platform.

### Example CI/CD Pipeline Structure

Your pipeline should have separate, sequential jobs or steps:

```yaml
# Stage 1: Generate Android Adapters
- script: |
    docker run \
      --env PLATFORM_ANDROID=true \
      # ... other common variables

# Stage 2: Generate iOS Adapters
- script: |
    docker run \
      --env PLATFORM_IOS=true \
      # ... other common variables
📚 Language-Specific Documentation
Please select your project's source language for detailed instructions, configuration examples, and code samples.
C# Documentation
Java Documentation
Go Documentation
JavaScript Documentation
Python Documentation
TypeScript Documentation
헬 Helper Files
The generator server provides helper files to simplify integration. You can download these directly from the running container:
Kotlin (for Android): /kotlin - Helper functions and annotations.
Swift (for iOS): /swift - Helper protocols and extensions.
Java: /java - The @TrackableDTO annotation interface.
TypeScript: /typescript - The @TrackableDTO decorator implementation.
JavaScript: /javascript - Helper for JSDoc-based tracking.
Go: /go - Comment snippet for tracking Go structs.