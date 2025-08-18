# Generating Design Tokens for iOS (SwiftUI)

This document provides a comprehensive guide for generating native design token files for an iOS project using SwiftUI.

## 1. Core Concept: SwiftUI Integration

When targeting iOS, the generator produces a set of `.swift` files designed for seamless integration with modern iOS development using SwiftUI. The output consists of a main `struct` that acts as a namespace for your tokens, with extensions on native SwiftUI types like `Color` and `Font`.

**Generated Files:**
-   **`Colors.swift`**: Extends `Color` to include all your design system colors as static properties.
-   **`Typography.swift`**: Extends `Font` to include all your typography tokens as static properties.
-   **`Spacing.swift`**: A `struct` containing all spacing and sizing tokens as static `CGFloat` constants.
-   **`Theme.swift`**: A convenience `struct` that provides a single namespace (`MyTheme.Colors.primary`) to access all tokens, and includes a View Modifier for easy application.

## 2. Configuration

To generate for iOS, you must set `PLATFORM_IOS=true` and provide the necessary iOS-specific variables.

---

### Example CI/CD Configuration for iOS

```bash
# 1. Design Platform Source (Using Figma as an example)
DESIGN_FIGMA=true
FIGMA_URL="https://www.figma.com/design/YOUR_FILE_ID/My-Awesome-App"
FIGMA_TOKEN_VAULT_KEY="my-figma-api-token-secret" # Name of the secret in your Key Vault

# 2. Target Platform
PLATFORM_IOS=true

# 3. iOS-Specific Output (CRITICAL)
# The name for the main theme struct and module
IOS_MODULE_NAME="MyDesignSystem"
# The directory to place the generated files
IOS_OUTPUT_DIR=/src/MyApp/DesignSystem/Generated

# 4. Core Config
REPO_URL="https://github.com/my-org/my-ios-app"
BRANCH="main"
LICENSE_SERVER="https://license.my-company.com"
MODE="sync" # Automatically commit and tag on changes

# 5. Git Operations (Optional)
AUTO_COMMIT=true
COMMIT_MESSAGE="feat(theme): Update design tokens from Figma"
3. Example Generated Files
Based on a typical design system, the generator will produce the following files inside your specified IOS_OUTPUT_DIR.
Colors.swift
code
Swift
import SwiftUI

// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT
public extension Color {
    struct MyDesignSystem {
        /// Primary brand color for main actions
        public static let primary = Color(hex: "#6200EE")
        /// Secondary accent color
        public static let secondary = Color(hex: "#03DAC6")
        /// Color for text on primary backgrounds
        public static let onPrimary = Color(hex: "#FFFFFF")
        /// Main app background color
        public static let background = Color(hex: "#FFFFFF")
        /// Surface color for cards, sheets, etc.
        public static let surface = Color(hex: "#F2F2F7")
    }
}

// Helper for initializing Color from a hex string
extension Color {
    init(hex: String) {
        // ... implementation ...
    }
}
Typography.swift
code
Swift
import SwiftUI

// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT
public extension Font {
    struct MyDesignSystem {
        /// For large screen titles
        public static let h1 = Font.system(size: 96, weight: .light)
        /// For standard screen titles
        public static let h2 = Font.system(size: 60, weight: .regular)
        /// For body text
        public static let body1 = Font.system(size: 16, weight: .regular)
    }
}
4. Usage in SwiftUI
You can now access all your design tokens in a clean, type-safe way directly in your SwiftUI views.
code
Swift
import SwiftUI

struct MyContentView: View {
    var body: some View {
        VStack(alignment: .leading, spacing: MyDesignSystem.Spacing.medium) {
            Text("Welcome!")
                // 1. Access a typography token
                .font(.MyDesignSystem.h2)
                // 2. Access a color token
                .foregroundColor(.MyDesignSystem.primary)

            Text("This is some body text that uses the default font style for the application.")
                .font(.MyDesignSystem.body1)
                .foregroundColor(.MyDesignSystem.onSurface) // A different color
        }
        .padding(MyDesignSystem.Spacing.large)
        // 3. Access a background color
        .background(Color.MyDesignSystem.background)
    }
}
5. Custom Code Preservation
You can safely add your own custom code to the generated files. The generator will preserve any code placed within specially marked comment blocks. This is perfect for adding custom colors, UIFont extensions, or helper functions that are not part of the design system.
Example: Adding a custom color extension to Colors.swift
code
Swift
import SwiftUI

/////////////////////////////////////////
// Custom Color Extensions - Preserved
/////////////////////////////////////////

public extension Color.MyDesignSystem {
    // A custom color not from the design system
    static let specialAlert = Color(red: 255/255, green: 204/255, blue: 0/255)
}

/////////////////////////////////////////
// End Custom Section
/////////////////////////////////////////

// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT
public extension Color {
    struct MyDesignSystem {
        // ... generated colors
    }
}

// ... hex helper extension
When you re-run the generator, the specialAlert color will be preserved in the updated Colors.swift file.