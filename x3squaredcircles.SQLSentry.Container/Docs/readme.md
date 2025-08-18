# 3SC Guardian: Universal Data Governance Engine

---

## 1. Overview

**3SC Guardian** is a high-speed, containerized data governance engine designed for modern CI/CD pipelines. It acts as a critical quality gate that validates the *reality* of your data against the *intent* of your architecture by detecting sensitive data in your database.

Guardian's unique power comes from its ability to find not only **direct identifiers** (like an email address) but also **contextual PII**—combinations of seemingly innocent data (like a first name, last name, and zip code) that, when linked together across your database, represent a critical security risk.

It is the "Developer Honesty Enforcement Engine" that ensures your data is as clean and compliant as your schema.

## 2. The Three Tiers of Governance

Guardian is built on a tiered philosophy that provides immediate value, deep customization, and emergency flexibility.

#### Tier 1: "No Config" (Native Intelligence)
Guardian works out-of-the-box. It ships with a comprehensive library of built-in **direct detectors** and pre-configured **contextual combination rules**. Run it with zero configuration, and it will immediately start finding complex, multi-table PII violations like "Full Name + Location".

#### Tier 2: "Easy Custom" (The CISO's Paved Road)
This is the standard path for enterprise use. It allows you to steer the native intelligence and define your organization's specific risk posture by enabling/disabling built-in rules or defining new high-level combination rules in the `guardian.context.json` file.

#### Tier 3: "Full Blown Custom" (The Expert Path)
For truly unique or proprietary data, experts can define entirely new, low-level data detectors using regular expressions in the `guardian.patterns.json` file. These new detectors can then be used by the CISO in their combination rules.

## 3. How It Works: The CI/CD Workflow

Guardian runs as a post-deployment quality gate, creating a tight feedback loop.

1.  **Forge Deploys:** `3SC Forge` deploys a schema change (`compiled-deployment.047.sql`).
2.  **Tests Populate Data:** Integration tests populate the new schema with realistic data.
3.  **Guardian Scans:** The pipeline runs the `3SC Guardian` container, providing it with the path to the SQL file and database connection details.
4.  **Guardian Analyzes:**
    *   It parses the SQL file to identify the "delta" of changed columns.
    *   It builds an in-memory graph of your database's foreign key relationships.
    *   It performs a high-speed scan for all known data patterns ("Hits").
    *   It traverses the schema graph from each "Hit," looking for other related hits to satisfy a combination rule (e.g., finding a `LastName` and `ZipCode` in tables related to a `FirstName`).
5.  **Build Halts:** If a violation is found, Guardian generates its `guardian-report.json` and exits with a non-zero code, failing the pipeline and forcing a fix.

## 4. Configuration

Guardian uses a hybrid model of version-controlled files and optional environment variables.

#### Configuration Files

| File | Purpose | Audience |
|---|---|---|
| **`guardian.context.json`** | **The CISO's Control Panel.** Enables/disables rules and defines new, high-level combination rules. | CISO, Data Stewards |
| **`guardian.patterns.json`** | **The Expert's Toolbox.** Defines new, low-level data detectors using regex. | Security Engineers, Data Scientists |
| **`guardian.exceptions.json`** | **The Operational Ledger.** A simple instruction set to suppress specific, approved violations. The Git history is the audit trail. | Developers, DevOps |

#### Environment Variables

| Variable | Description |
|---|---|
| `DB_CONNECTION_STRING` | **Required.** The full connection string to the target database (or use Vault). |
| `DB_VAULT_KEY` | **Required.** The name of the secret in your vault containing the connection string (or use direct string). |
| `THREE_SC_VAULT_...` | Variables to configure your vault provider (`PROVIDER`, `URL`, `TOKEN`). |
| `GUARDIAN_CONTEXT_FILE_PATH`| Optional path to the context file. Defaults to `./guardian.context.json`. |
| `GUARDIAN_PATTERNS_FILE_PATH`| Optional path to the custom patterns file. Defaults to `./guardian.patterns.json`. |
| `GUARDIAN_EXCEPTIONS_FILE_PATH`| Optional path to the exception file. Defaults to `./guardian.exceptions.json`. |
| `GUARDIAN_CONTINUE_ON_FAILURE`| The "Break Glass" override. If `true`, always exits with code `0`. |

---

## 5. Built-in Violation Codes & Detectors

Guardian ships with a rich library of detectors. **Direct Identifiers** (`PII_*`, `PCI_*`, `SEC_*`) are sensitive on their own. **Quasi-Identifiers** (`QI_*`) are low-sensitivity building blocks used to create high-risk **Contextual Violations** (`CTX_*`).

#### Direct Identifiers (PII & National IDs)
| Code | Default Severity | Description | Tags |
|---|---|---|---|
| `PII_001` | critical | Email Address | `contact`, `direct_identifier` |
| `PII_003` | critical | Social Security Number (US) | `national_id`, `direct_identifier` |
| `PII_007` | critical | National Insurance Number (UK) | `national_id`, `direct_identifier` |
| `PII_008` | critical | Social Insurance Number (Canada) | `national_id`, `direct_identifier` |

#### Quasi-Identifiers (Building Blocks for Contextual PII)
| Code | Default Severity | Description | Tags |
|---|---|---|---|
| `QI_001` | info | First Name (Common English) | `name_part` |
| `QI_002` | info | Last Name (Common English) | `name_part` |
| `QI_003` | info | Full Name (First Last) | `name_full` |
| `QI_010` | info | Date of Birth | `dob` |
| `QI_020` | info | Street Address | `address_part` |
| `QI_021` | info | Zip Code (US) | `location`, `address_part` |
| `QI_022` | info | Postal Code (Canada) | `location`, `address_part` |
| `QI_023` | info | Postcode (UK) | `location`, `address_part` |

#### Financial Data (PCI/PIFI)
| Code | Default Severity | Description | Tags |
|---|---|---|---|
| `PCI_001` | critical | Credit Card Number | `financial`, `pci` |
| `PIFI_001`| critical | IBAN | `financial`, `pifi` |
| `PIFI_003`| error | ABA Routing Number (US) | `financial`, `pifi` |

#### Security Credentials (SEC)
| Code | Default Severity | Description | Tags |
|---|---|---|---|
| `SEC_001` | critical | AWS Access Key ID | `secret`, `credential` |
| `SEC_002` | critical | AWS Secret Access Key | `secret`, `credential` |
| `SEC_003` | critical | Azure Client Secret | `secret`, `credential` |
| `SEC_004` | critical | Google Cloud API Key | `secret`, `credential` |
| `SEC_005` | critical | Private Key Block | `secret`, `credential`, `crypto` |
| `SEC_010` | critical | Database Connection String | `secret`, `credential` |
| `SEC_999` | error | Generic Secret Pattern | `secret`, `credential` |

#### Protected Health Information (PHI)
| Code | Default Severity | Description | Tags |
|---|---|---|---|
| `PHI_001` | error | ICD-10 Code | `health`, `phi` |
| `PHI_002` | error | National Drug Code (US) | `health`, `phi` |
| `PHI_003` | error | DEA Number (US) | `health`, `phi` |

#### Network & Device Identifiers
| Code | Default Severity | Description | Tags |
|---|---|---|---|
| `NET_001` | warning | IP Address (v4) | `location`, `network` |
| `NET_002` | warning | MAC Address | `device`, `network` |

---

## 6. Native Combination Rules

Guardian ships with pre-configured combination rules. You can disable them or change their traversal depth in `guardian.context.json`.

| Code | Default Severity | Description | Default Combination |
|---|---|---|---|
| **`CTX_PII_001`** | critical | Full Name + Location | `QI_003` (Full Name) + `QI_021` (Zip Code) |
| **`CTX_PII_002`** | critical | Name + Full DOB | `QI_003` (Full Name) + `QI_010` (Date of Birth) |
| **`CTX_PHI_001`** | critical | Name + Health Info | `QI_003` (Full Name) + a tag of `health` |