# 3SC Design Token Generator: Android Quick Start

This guide provides a practical overview of the files generated for the Android platform and how to integrate them into a Jetpack Compose project.

## 1. Generated Files

When you run the Design Token Generator with `TOKENS_TARGET_PLATFORM=android`, it will produce a set of Kotlin (`.kt`) files in the specified output directory. These files are designed to be dropped directly into your Android project's source tree.

- `Colors.kt`: Contains all your extracted color tokens as `Color` objects.
- `Typography.kt`: Contains your typography styles as `TextStyle` objects.
- `Spacing.kt`: Contains your spacing and sizing tokens as `Dp` values.
- `Theme.kt`: A Jetpack Compose `Theme` composable that wires everything together.

## 2. Example Output: `Colors.kt`

This file provides a central object, `DesignTokenColors`, for accessing all your brand colors in a type-safe way.

### Generated Code:

```kotlin
package com.mycompany.app.ui.theme.generated

import androidx.compose.ui.graphics.Color

// AUTO-GENERATED CONTENT - DO NOT EDIT
object DesignTokenColors {
    /** The primary brand color for interactive elements. */
    val brandPrimary = Color(0xFF0052CC)

    /** The primary background color for screens. */
    val backgroundPrimary = Color(0xFFFFFFFF)

    /** The color for primary text content. */
    val textPrimary = Color(0xFF1D2329)
    
    /** A neutral gray for borders and dividers. */
    val borderNeutral = Color(0xFFD1D5DA)
}
```

### Usage in a Composable:

```kotlin
import com.mycompany.app.ui.theme.generated.DesignTokenColors

@Composable
fun PrimaryButton(text: String) {
    Button(
        onClick = { /* ... */ },
        colors = ButtonDefaults.buttonColors(
            containerColor = DesignTokenColors.brandPrimary
        )
    ) {
        Text(text, color = DesignTokenColors.backgroundPrimary)
    }
}
```

## 3. Example Output: `Typography.kt`

This file provides `TextStyle` objects for your typography system, ready to be used in Text composables.

### Generated Code:

```kotlin
package com.mycompany.app.ui.theme.generated

import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

// AUTO-GENERATED CONTENT - DO NOT EDIT
object DesignTokenTypography {
    /** For large screen titles. */
    val heading1 = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Bold,
        fontSize = 32.sp
    )

    /** For standard body copy. */
    val bodyRegular = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 16.sp
    )
}
```

### Usage in a Composable:

```kotlin
import com.mycompany.app.ui.theme.generated.DesignTokenTypography

@Composable
fun ArticleScreen(title: String, body: String) {
    Column {
        Text(text = title, style = DesignTokenTypography.heading1)
        Text(text = body, style = DesignTokenTypography.bodyRegular)
    }
}
```

## 4. Example Output: `Theme.kt`

This file provides a top-level Theme composable that configures the MaterialTheme with your design tokens, making them available throughout your application.

### Generated Code:

```kotlin
package com.mycompany.app.ui.theme.generated

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable

// AUTO-GENERATED CONTENT - DO NOT EDIT
private val LightColorScheme = lightColorScheme(
    primary = DesignTokenColors.brandPrimary,
    background = DesignTokenColors.backgroundPrimary,
    /* ... other colors mapped automatically ... */
)

@Composable
fun MyBrandTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = LightColorScheme,
        // Typography and Shapes can also be configured here
        content = content
    )
}
```

### Usage in your App/Activity:

```kotlin
import com.mycompany.app.ui.theme.generated.MyBrandTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MyBrandTheme { // Wrap your entire app in the generated theme
                MyAppNavHost()
            }
        }
    }
}
```

## 5. Integrating Generated Files

To use the generated files, simply copy them into the appropriate package within your Android project, typically within a `ui/theme/generated` sub-package, and ensure your Gradle build includes the directory.