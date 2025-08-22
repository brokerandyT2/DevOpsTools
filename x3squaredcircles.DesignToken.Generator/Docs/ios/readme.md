# 3SC Design Token Generator: iOS Quick Start

This guide provides a practical overview of the files generated for the iOS platform and how to integrate them into a SwiftUI project.

## 1. Generated Files

When you run the Design Token Generator with `TOKENS_TARGET_PLATFORM=ios`, it will produce a set of Swift (`.swift`) files in the specified output directory. These files are designed to be added directly to your Xcode project.

- `Colors.swift`: Contains all your extracted color tokens as SwiftUI `Color` extensions.
- `Typography.swift`: Contains your typography styles as `Font` extensions.
- `Spacing.swift`: Contains your spacing and sizing tokens as `CGFloat` constants.
- `Theme.swift`: A struct that provides a unified namespace for all your tokens.

## 2. Example Output: `Colors.swift`

This file provides a central struct, `DesignTokenColors`, for accessing all your brand colors in a type-safe way within SwiftUI.

### Generated Code:

```swift
import SwiftUI

// AUTO-GENERATED CONTENT - DO NOT EDIT
public struct DesignTokenColors {
    /// The primary brand color for interactive elements.
    public static let brandPrimary = Color(hex: "#0052CC")

    /// The primary background color for screens.
    public static let backgroundPrimary = Color(hex: "#FFFFFF")

    /// The color for primary text content.
    public static let textPrimary = Color(hex: "#1D2329")
    
    /// A neutral gray for borders and dividers.
    public static let borderNeutral = Color(hex: "#D1D5DA")
}

// Private extension to support hex colors
private extension Color {
    // ... hex initializer implementation ...
}
```

### Usage in a SwiftUI View:

```swift
struct PrimaryButton: View {
    var title: String
    
    var body: some View {
        Text(title)
            .padding()
            .background(DesignTokenColors.brandPrimary)
            .foregroundColor(DesignTokenColors.backgroundPrimary)
            .cornerRadius(8)
    }
}
```

## 3. Example Output: `Typography.swift`

This file provides `Font` styles for your typography system, ready to be used in Text views.

### Generated Code:

```swift
import SwiftUI

// AUTO-GENERATED CONTENT - DO NOT EDIT
public struct DesignTokenTypography {
    /// For large screen titles.
    public static let heading1 = Font.system(size: 32, weight: .bold)

    /// For standard body copy.
    public static let bodyRegular = Font.system(size: 16, weight: .regular)
}
```

### Usage in a SwiftUI View:

```swift
struct ArticleView: View {
    var title: String
    var bodyText: String
    
    var body: some View {
        VStack(alignment: .leading) {
            Text(title)
                .font(DesignTokenTypography.heading1)
            Text(bodyText)
                .font(DesignTokenTypography.bodyRegular)
        }
    }
}
```

## 4. Example Output: `Theme.swift`

This file provides a top-level `DesignTokenTheme` struct that acts as a single, convenient namespace for all your design tokens.

### Generated Code:

```swift
import SwiftUI

// AUTO-GENERATED CONTENT - DO NOT EDIT
public struct DesignTokenTheme {
    public static let colors = DesignTokenColors.self
    public static let typography = DesignTokenTypography.self
    public static let spacing = DesignTokenSpacing.self
}
```

### Usage in a SwiftUI View:

```swift
struct ThemedCard: View {
    var body: some View {
        VStack {
            // ... content ...
        }
        .padding(DesignTokenTheme.spacing.medium) // Access spacing
        .background(DesignTokenTheme.colors.backgroundPrimary) // Access colors
        .cornerRadius(DesignTokenTheme.spacing.small)
    }
}
```

## 5. Preserving Custom Code

The generator is designed to be non-destructive. You can safely add your own custom code to the generated files inside special comment blocks. This is useful for adding custom gradients, ShapeStyle protocols, or complex Font objects that are not directly represented in your design system.

Your custom code will be preserved and moved to the top of the file every time the generator is re-run.

### Example: Adding a custom shadow style to Theme.swift

1. Open the generated `Theme.swift` file.
2. Add your custom code inside the designated comment blocks.

```swift
import SwiftUI

///////////////////////////////////////////
// Custom Theme Components - Preserved
///////////////////////////////////////////

struct CardShadow: ViewModifier {
    func body(content: Content) -> some View {
        content
            .shadow(
                color: DesignTokenColors.borderNeutral.opacity(0.5),
                radius: DesignTokenTheme.spacing.xSmall,
                x: 0,
                y: 4
            )
    }
}

///////////////////////////////////////////
// End Custom Section
///////////////////////////////////////////

// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT
public struct DesignTokenTheme {
    // ... generated properties ...
}
```

Now, no matter how many times you re-run the token generator, the `CardShadow` view modifier will be safely preserved.

## 6. Integrating Generated Files

To use the generated files, simply add them to your Xcode project by dragging them into your project navigator or using "Add Files to Project" from the File menu. Ensure they're included in your app target's build phases.