
Use the standard CSS variables template
WEB_TEMPLATE="vanilla"
The directory to place the generated CSS files
WEB_OUTPUT_DIR=/src/styles/generated
An optional prefix for all generated CSS variables and utility classes
WEB_CSS_PREFIX="my-app"
4. Core Config
REPO_URL="https://github.com/my-org/my-web-app"
BRANCH="main"
LICENSE_SERVER="https://license.my-company.com"
MODE="sync"
5. Git Operations (Optional)
AUTO_COMMIT=true
COMMIT_MESSAGE="feat(styles): Update design tokens from Figma"
code
Code
---

## 4. Example Generated Files (Vanilla Template)

Based on a typical design system, the `vanilla` template will produce the following files in your `WEB_OUTPUT_DIR`.

#### `tokens.css`
This file contains the raw token values as CSS Custom Properties.

```css
/* AUTO-GENERATED STYLES - DO NOT EDIT */
:root {
  --my-app-color-primary: #6200EE;
  --my-app-color-secondary: #03DAC6;
  --my-app-color-background: #FFFFFF;
  --my-app-typography-h1-font-family: "Roboto", sans-serif;
  --my-app-typography-h1-font-size: 96px;
  --my-app-typography-h1-font-weight: 300;
  --my-app-spacing-medium: 16px;
  --my-app-spacing-large: 32px;
}
theme.css
This file contains optional utility classes that consume the variables from tokens.css.
code
Css
/* AUTO-GENERATED THEME STYLES - DO NOT EDIT */
.my-app-text-primary {
  color: var(--my-app-color-primary);
}

.my-app-bg-primary {
  background-color: var(--my-app-color-primary);
}

.my-app-text-h1 {
  font-family: var(--my-app-typography-h1-font-family);
  font-size: var(--my-app-typography-h1-font-size);
  font-weight: var(--my-app-typography-h1-font-weight);
}
5. Usage in HTML & CSS
You can now use your design tokens anywhere in your project.
Step 1: Import the CSS
Import the generated tokens.css file (and optionally theme.css) into your main stylesheet or HTML file.
code
Html
<head>
  <link rel="stylesheet" href="styles/generated/tokens.css">
  <link rel="stylesheet" href="styles/generated/theme.css">
  <link rel="stylesheet" href="styles/main.css">
</head>
Step 2: Use the Tokens
In your own CSS (main.css):
code
Css
body {
  background-color: var(--my-app-color-background);
  font-size: var(--my-app-spacing-medium);
}

.button-primary {
  background-color: var(--my-app-color-primary);
  padding: var(--my-app-spacing-medium);
}
Directly in your HTML using utility classes:
code
Html
<body>
  <h1 class="my-app-text-h1 my-app-text-primary">Page Title</h1>
  <p>This is some body text.</p>
  <button class="button-primary">Click Me</button>
</body>
6. Custom Code Preservation
For the vanilla, tailwind, and bootstrap templates, you can safely add your own custom code to the generated files. The generator will preserve any code placed within specially marked comment blocks.
Example: Adding a custom variable to tokens.css
code
Css
/**********************************/
/* Custom CSS Variables - Preserved */
/**********************************/

:root {
  --my-app-custom-border-radius: 8px;
}

/**********************************/
/* End Custom Section */
/**********************************/

/* AUTO-GENERATED STYLES - DO NOT EDIT */
:root {
  /* ... generated variables */
}