Design Token Generator - Documentation
Welcome to the documentation for the 3SC Design Token Generator. This tool automates the bridge between your design system and your application codebases, ensuring brand consistency and dramatically accelerating UI development.
Core Concept
The generator connects to a single source of truth for your design system (like Figma, Sketch, or Penpot) and extracts foundational design decisions—colors, typography, spacing, etc.—as structured data. It then transforms this data into ready-to-use, native code for your target platforms.
Input: A design file from a supported platform (e.g., a Figma URL).
Output: Native, idiomatic style files (e.g., Kotlin objects for Android, Swift structs for iOS, CSS custom properties for Web).
This process ensures that a change in the design system (e.g., updating the primary brand color) can be automatically and accurately propagated to all platforms with a single pipeline run.
📚 Platform-Specific Documentation
Please select your target development platform for detailed instructions, configuration examples, and code samples.
Android (Jetpack Compose) Documentation - PLATFORM_ANDROID=true
Generates Kotlin objects with Color, TextStyle, and Dp values for seamless integration with Jetpack Compose.
iOS (SwiftUI) Documentation - PLATFORM_IOS=true
Generates Swift structs with extensions on Color, Font, and CGFloat for use in SwiftUI.
Web Documentation - PLATFORM_WEB=true
Generates CSS custom properties (:root variables) and supports advanced templates for Tailwind, Bootstrap, and Material Design.
Key Features Overview
Multi-Platform Design Source: Connects to Figma, Sketch, Adobe XD, Zeplin, Abstract, and Penpot.
Preserves Custom Code: Intelligently merges new token generations with hand-written code in your style files.
Automated Git Workflow: Can automatically commit, create branches, tag releases, and open pull requests.
Forensic Traceability: Every run produces a comprehensive set of JSON reports, creating an auditable trail from design change to code implementation.
CI/CD Native: Fully configurable via environment variables for seamless integration into any build pipeline.
Health Check
You can verify that the generator's documentation server is running by accessing the /health endpoint (e.g., http://localhost:8080/health).