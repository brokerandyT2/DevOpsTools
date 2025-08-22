# 3SC Design Token Generator: Web Quick Start

This guide provides a practical overview of the files generated for the Web platform and how to integrate them into your CSS, SCSS, or Tailwind projects.

## 1. Generated Files

When you run the Design Token Generator with `TOKENS_TARGET_PLATFORM=web`, the output will vary based on the `TOKENS_WEB_TEMPLATE` you select.

- **`vanilla` (default):** Generates a `tokens.css` file with all your design tokens defined as CSS Custom Properties on the `:root` element.
- **`tailwind`:** Generates a `tailwind.tokens.js` file designed to be imported and merged into your project's `tailwind.config.js`.
- **`bootstrap`:** Generates a `_variables.scss` file with all your tokens defined as SCSS variables, ready to override Bootstrap's defaults.
- **`material`:** Generates a `material-theme.js` file for creating a Material-UI theme.

## 2. Example: `vanilla` (CSS Custom Properties)

This is the most common and framework-agnostic output.

### Generated `tokens.css`:

```css
/* AUTO-GENERATED STYLES - DO NOT EDIT */
:root {
  --brand-primary: #0052cc;
  --background-primary: #ffffff;
  --text-primary: #1d2329;
  --spacing-medium: 16px;
  --font-family-body: "Inter", sans-serif;
  --font-weight-bold: 700;
  --font-size-heading-1: 32px;
}
```

### Usage in your own CSS:

```css
.my-button {
  background-color: var(--brand-primary);
  color: var(--background-primary);
  padding: var(--spacing-medium);
}

h1 {
  font-family: var(--font-family-body);
  font-size: var(--font-size-heading-1);
  font-weight: var(--font-weight-bold);
}
```

## 3. Example: `tailwind`

This output is designed to extend your existing Tailwind CSS configuration.

### Generated `tailwind.tokens.js`:

```javascript
// AUTO-GENERATED TAILWIND TOKENS - DO NOT EDIT
module.exports = {
  theme: {
    extend: {
      colors: {
        'brand-primary': '#0052cc',
        'background-primary': '#ffffff',
        'text-primary': '#1d2329',
      },
      spacing: {
        'medium': '16px',
        'large': '24px',
      },
      // ... other token types ...
    }
  }
};
```

### Usage in `tailwind.config.js`:

```javascript
const designTokens = require('./styles/generated/tailwind.tokens.js');
const { merge } = require('lodash'); // A utility like lodash is great for merging

/** @type {import('tailwindcss').Config} */
module.exports = merge(
  {
    // Your standard Tailwind config here
    content: ["./src/**/*.{html,js,jsx,ts,tsx}"],
    theme: {
      extend: {
        // Your custom extensions
      },
    },
    plugins: [],
  },
  designTokens // Merge the generated tokens
);
```

You can now use Tailwind utility classes like `bg-brand-primary`, `text-text-primary`, and `p-medium`.

## 4. Preserving Custom Code

For the `vanilla` template, you can safely add your own custom CSS variables to the `tokens.css` file inside special comment blocks. Your code will be preserved across runs.

### Example: Adding custom variables to `tokens.css`

```css
/**********************************/
/* Custom CSS Variables - Preserved */
/**********************************/

:root {
  --header-height: 60px;
  --sidebar-width: 240px;
}

/**********************************/
/* End Custom Section             */
/**********************************/

/* AUTO-GENERATED STYLES - DO NOT EDIT */
:root {
  --brand-primary: #0052cc;
  /* ... etc ... */
}
```

Now, no matter how many times you re-run the generator, your `--header-height` and `--sidebar-width` variables will be safely preserved at the top of the file.

## 5. Integrating Generated Files

To use the generated files:

- **For vanilla CSS:** Import or link the `tokens.css` file in your HTML or main CSS file.
- **For Tailwind:** Import and merge the tokens file in your `tailwind.config.js` as shown above.
- **For Bootstrap:** Import the `_variables.scss` file before importing Bootstrap's main SCSS files.
- **For Material-UI:** Import and use the theme object in your Material-UI ThemeProvider.