# Generating Design Tokens for Android (Jetpack Compose)

This document provides a comprehensive guide for generating native design token files for an Android project using Jetpack Compose.

## 1. Core Concept: Jetpack Compose Integration

When targeting Android, the generator produces a set of `.kt` (Kotlin) files designed for seamless integration with modern Android development using Jetpack Compose. The output consists of Kotlin `object` containers that expose your design tokens as type-safe properties.

**Generated Files:**
-   **`Colors.kt`**: Contains all color tokens as `androidx.compose.ui.graphics.Color` objects.
-   **`Typography.kt`**: Contains all typography tokens as `androidx.compose.ui.text.TextStyle` objects.
-   **`Spacing.kt`**: Contains all spacing and sizing tokens as `Dp` values.
-   **`Theme.kt`**: A complete `MaterialTheme` Composable that wires together your generated colors and typography into a reusable theme for your application.

## 2. Configuration

To generate for Android, you must set `PLATFORM_ANDROID=true` and provide the necessary Android-specific variables.

---

### Example CI/CD Configuration for Android

```bash
# 1. Design Platform Source (Using Figma as an example)
DESIGN_FIGMA=true
FIGMA_URL="https://www.figma.com/design/YOUR_FILE_ID/My-Awesome-App"
FIGMA_TOKEN_VAULT_KEY="my-figma-api-token-secret" # Name of the secret in your Key Vault

# 2. Target Platform
PLATFORM_ANDROID=true

# 3. Android-Specific Output (CRITICAL)
# The package name where the generated files will live
ANDROID_PACKAGE_NAME="com.mycompany.myapp.ui.theme"
# The directory to place the generated files
ANDROID_OUTPUT_DIR=/src/app/src/main/java/com/mycompany/myapp/ui/theme
# The name of the generated theme Composable
ANDROID_THEME_NAME="MyAppTheme"

# 4. Core Config
REPO_URL="https://github.com/my-org/my-android-app"
BRANCH="main"
LICENSE_SERVER="https://license.my-company.com"
MODE="sync" # Automatically commit and tag on changes

# 5. Git Operations (Optional)
AUTO_COMMIT=true
COMMIT_MESSAGE="feat(theme): Update design tokens from Figma"

3. Example Generated Files
Based on a typical design system, the generator will produce the following files inside your specified ANDROID_OUTPUT_DIR.
Colors.kt
code
Kotlin
package com.mycompany.myapp.ui.theme

import androidx.compose.ui.graphics.Color

// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT
object DesignTokenColors {
    /** Primary brand color for main actions */
    val Primary = Color(0xFF6200EE)
    /** Secondary accent color */
    val Secondary = Color(0xFF03DAC6)
    /** Color for text on primary backgrounds */
    val OnPrimary = Color(0xFFFFFFFF)
    /** Main app background color */
    val Background = Color(0xFFFFFFFF)
    /** Surface color for cards, sheets, etc. */
    val Surface = Color(0xFFF2F2F7)
}
Typography.kt
code
Kotlin
package com.mycompany.myapp.ui.theme

import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT
object DesignTokenTypography {
    /** For large screen titles */
    val H1 = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Light,
        fontSize = 96.sp
    )
    /** For standard screen titles */
    val H2 = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 60.sp
    )
    /** For body text */
    val Body1 = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 16.sp
    )
}
4. Usage in Jetpack Compose
You can now use the generated MyAppTheme Composable as the root of your UI and access all tokens through the MaterialTheme object.
code
Kotlin
// In your Activity or root Composable
import com.mycompany.myapp.ui.theme.MyAppTheme
import com.mycompany.myapp.ui.theme.DesignTokenColors // Optional direct access

// ...

setContent {
    // 1. Wrap your entire app in the generated theme
    MyAppTheme {
        MyScreen()
    }
}

@Composable
fun MyScreen() {
    Column(
        modifier = Modifier
            // 2. Access theme colors
            .background(MaterialTheme.colorScheme.background)
            .padding(DesignTokenSpacing.Medium) // Access spacing directly
    ) {
        Text(
            text = "Welcome!",
            // 3. Access theme typography
            style = MaterialTheme.typography.headlineMedium,
            // 4. Access a specific token color
            color = DesignTokenColors.Primary
        )
    }
}
5. Custom Code Preservation
You can safely add your own custom code to the generated files. The generator will preserve any code placed within specially marked comment blocks. This is perfect for adding custom colors, fonts, or helper functions that are not part of the design system.
Example: Adding a custom color to Colors.kt
code
Kotlin
package com.mycompany.myapp.ui.theme

import androidx.compose.ui.graphics.Color

/////////////////////////////////////////
// Custom Color Definitions - Preserved
/////////////////////////////////////////

val MyCustomGradientStart = Color(0xFF123456)

/////////////////////////////////////////
// End Custom Section
/////////////////////////////////////////

// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT
object DesignTokenColors {
    // ... generated colors
}
When you re-run the generator, the MyCustomGradientStart color will be preserved in the updated Colors.kt file.