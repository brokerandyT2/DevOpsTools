# SQL Schema Generator - JavaScript Developer Integration Guide

## Documentation Access

This documentation is always available via HTTP endpoint on port 8080:

```bash
# Access JavaScript documentation from running container
curl http://localhost:8080/docs/javascript

# Or in browser
http://localhost:8080/docs/javascript

# Download JavaScript decorators reference
curl http://localhost:8080/javascript > sql-decorators.js
```

## Overview

The SQL Schema Generator analyzes JavaScript classes marked with tracking decorators and automatically generates database schema changes with intelligent deployment planning. It uses static analysis to discover classes, properties, and their SQL metadata decorators.

**Key Features:**
- **Static analysis** of JavaScript files and bundled code
- **Decorator-based** SQL schema definition
- **TypeORM/Sequelize compatibility** with standard decorators
- **Custom SQL decorators** for advanced schema control
- **29-phase deployment planning** with comprehensive risk assessment
- **Multi-database provider support** (SQL Server, PostgreSQL, MySQL, Oracle, SQLite)

## Quick Start

### Download JavaScript Decorators

```bash
# Download the decorator definitions and helper functions
curl http://localhost:8080/javascript > sql-decorators.js

# Add to your project
cp sql-decorators.js src/decorators/
```

### Basic Class Definition

```javascript
import { 
    ExportToSQL, 
    SqlType, 
    SqlConstraints, 
    SqlForeignKey, 
    SqlIndex 
} from './decorators/sql-decorators.js';

@ExportToSQL()
class User {
    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;

    @SqlType('NVARCHAR', { length: 100 })
    @SqlConstraints(['NOT_NULL'])
    name;

    @SqlType('NVARCHAR', { length: 255 })
    @SqlIndex({ type: 'UNIQUE' })
    email;

    @SqlForeignKey({ table: 'departments', column: 'id' })
    @SqlConstraints(['NOT_NULL'])
    departmentId;

    @SqlType('DATETIME2')
    @SqlDefault('GETUTCDATE()')
    createdAt;

    constructor(data = {}) {
        Object.assign(this, data);
    }
}

@ExportToSQL()
class Department {
    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;

    @SqlType('NVARCHAR', { length: 50 })
    @SqlConstraints(['NOT_NULL', 'UNIQUE'])
    name;

    @SqlType('VARCHAR', { length: 10 })
    @SqlConstraints(['NOT_NULL', 'UNIQUE'])
    code;

    constructor(data = {}) {
        Object.assign(this, data);
    }
}

@ExportToSQL()
@SqlTable('orders')
@SqlSchema('sales')
class Order {
    @SqlType('UNIQUEIDENTIFIER')
    @SqlConstraints(['PRIMARY_KEY'])
    @SqlDefault('NEWID()')
    id;

    @SqlType('NVARCHAR', { length: 20 })
    @SqlConstraints(['NOT_NULL', 'UNIQUE'])
    @SqlIndex({ name: 'IX_Order_OrderNumber' })
    orderNumber;

    @SqlForeignKey({ table: 'customers', column: 'id' })
    @SqlIndex()
    customerId;

    @SqlType('DECIMAL', { precision: 10, scale: 2 })
    @SqlConstraints(['NOT_NULL'])
    totalAmount;

    @SqlType('NVARCHAR', { length: 20 })
    @SqlDefault("'Pending'")
    status;

    @SqlType('DATETIME2')
    @SqlDefault('GETUTCDATE()')
    createdAt;

    constructor(data = {}) {
        Object.assign(this, data);
        this.id = this.id || require('uuid').v4();
    }
}
```

### Container Usage

```bash
# Build your JavaScript project first
npm run build

# Run SQL Schema Generator
docker run --rm \
  -p 8080:8080 \
  --volume $(pwd):/src \
  --env-file .env \
  myregistry.azurecr.io/sql-schema-generator:latest
```

## Complete Configuration Reference

All configuration is provided via environment variables:

### Required Configuration

| Variable | Description | Example |
|----------|-------------|---------|
| `LANGUAGE_JAVASCRIPT` | Enable JavaScript analysis | `true` |
| `TRACK_ATTRIBUTE` | Decorator name to track for schema generation | `ExportToSQL` |
| `REPO_URL` | Repository URL for context | `https://github.com/company/project` |
| `BRANCH` | Git branch being processed | `main`, `develop` |
| `LICENSE_SERVER` | Licensing server URL | `https://license.company.com` |
| `DATABASE_SQLSERVER` | Target SQL Server (exactly one database provider required) | `true` |
| `DATABASE_SERVER` | Database server hostname | `sql.company.com` |
| `DATABASE_NAME` | Target database name | `MyApplication` |

### Optional Configuration

| Variable | Default | Description | Example |
|----------|---------|-------------|---------|
| `JS_SOURCE_PATHS` | Auto-discover | Colon-separated paths to JavaScript source files | `src:lib:dist` |
| `JS_BUILD_PATH` | `dist` | Primary build output directory | `build:public/js` |
| `JS_ENTRY_POINTS` | Auto-detect | Entry point files for analysis | `src/index.js:src/models/index.js` |
| `JS_MODULE_TYPE` | `auto` | Module system type | `commonjs`, `esm`, `auto` |
| `DATABASE_SCHEMA` | Provider default | Target database schema | `dbo`, `public` |
| `MODE` | `validate` | Operation mode | `validate`, `execute` |
| `ENVIRONMENT` | `dev` | Deployment environment | `dev`, `beta`, `prod` |
| `VERTICAL` | (empty) | Business vertical for beta environments | `Photography`, `Navigation` |
| `VALIDATE_ONLY` | `false` | Validation-only mode | `true`, `false` |
| `GENERATE_INDEXES` | `true` | Auto-generate performance indexes | `false` |
| `GENERATE_FK_INDEXES` | `true` | Auto-generate foreign key indexes | `false` |
| `BACKUP_BEFORE_DEPLOYMENT` | `false` | Create backup before changes | `true` |

### Authentication Configuration

| Variable | Description | Example |
|----------|-------------|---------|
| `DATABASE_USERNAME` | Database username (if not using integrated auth) | `sa`, `app_user` |
| `DATABASE_PASSWORD` | Database password | `SecurePassword123` |
| `DATABASE_USE_INTEGRATED_AUTH` | Use Windows integrated authentication | `true`, `false` |
| `PAT_TOKEN` | Personal Access Token for git operations | `ghp_xxxxxxxxxxxx` |

## JavaScript Decorator Reference

### Class-Level Decorators

#### `@ExportToSQL()`
Marks a class for database schema generation.

```javascript
@ExportToSQL()
class Customer {
    // Properties...
}

@ExportToSQL({ description: 'Core business entity' })
class Order {
    // Properties...
}
```

#### `@SqlTable(tableName)`
Overrides the default table name.

```javascript
@ExportToSQL()
@SqlTable('tbl_customers')
class Customer {
    // Properties...
}
```

#### `@SqlSchema(schemaName)`
Overrides the default schema name.

```javascript
@ExportToSQL()
@SqlSchema('sales')
class Customer {
    // Properties...
}
```

### Property-Level Decorators

#### `@SqlType(dataType, options)`
Specifies the SQL data type for a property.

```javascript
class Product {
    @SqlType('NVARCHAR', { length: 100 })
    name;

    @SqlType('DECIMAL', { precision: 10, scale: 2 })
    price;

    @SqlType('DATETIME2')
    createdAt;

    @SqlType('BIT')
    isActive;
}
```

**Available SQL Data Types:**
- **String**: `NVARCHAR`, `VARCHAR`, `NVARCHAR_MAX`, `VARCHAR_MAX`, `TEXT`, `NTEXT`
- **Numeric**: `INT`, `BIGINT`, `SMALLINT`, `TINYINT`, `DECIMAL`, `FLOAT`, `REAL`, `MONEY`, `SMALLMONEY`
- **Date/Time**: `DATETIME2`, `DATETIME`, `DATE`, `TIME`, `SMALLDATETIME`, `DATETIMEOFFSET`
- **Other**: `BIT`, `UNIQUEIDENTIFIER`, `BINARY`, `VARBINARY`, `VARBINARY_MAX`, `IMAGE`, `XML`, `GEOGRAPHY`, `GEOMETRY`, `TIMESTAMP`

#### `@SqlConstraints(constraints)`
Applies SQL constraints to a property.

```javascript
class User {
    @SqlConstraints(['NOT_NULL'])
    name;

    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;

    @SqlConstraints(['UNIQUE'])
    email;

    @SqlConstraints(['CHECK'], { expression: 'age >= 0' })
    age;
}
```

**Available Constraints:**
- `NOT_NULL` - NOT NULL constraint
- `UNIQUE` - UNIQUE constraint
- `PRIMARY_KEY` - PRIMARY KEY constraint
- `IDENTITY` - IDENTITY column (auto-increment)
- `CHECK` - CHECK constraint (use with expression option)

#### `@SqlForeignKey(options)`
Creates a foreign key relationship.

```javascript
class OrderItem {
    @SqlForeignKey({ table: 'orders', column: 'id' })
    orderId;

    @SqlForeignKey({ table: 'products', column: 'id' })
    productId;

    @SqlForeignKey({ 
        table: 'users', 
        column: 'id', 
        onDelete: 'CASCADE' 
    })
    createdByUserId;
}
```

**Cascade Actions:**
- `NO_ACTION` - No action (default)
- `CASCADE` - Cascade changes
- `SET_NULL` - Set to NULL
- `SET_DEFAULT` - Set to default value
- `RESTRICT` - Restrict changes

#### `@SqlColumn(columnName)`
Overrides the default column name.

```javascript
class Customer {
    @SqlColumn('customer_name')
    name;

    @SqlColumn('email_address')
    email;
}
```

#### `@SqlIgnore(reason)`
Excludes a property from schema generation.

```javascript
class User {
    name;
    
    @SqlIgnore()
    calculatedField;

    @SqlIgnore('Cached value, not persisted')
    cachedTotal;

    @SqlIgnore('Navigation property')
    orders;
}
```

#### `@SqlDefault(value)`
Sets a default value for the column.

```javascript
class Order {
    @SqlDefault("'Pending'")
    status;

    @SqlDefault('GETUTCDATE()')
    createdAt;

    @SqlDefault('1')
    isActive;
}
```

### Index Decorators

#### `@SqlIndex(options)`
Creates an index on a property.

```javascript
class User {
    @SqlIndex()
    email;

    @SqlIndex({ type: 'UNIQUE' })
    username;

    @SqlIndex({ type: 'CLUSTERED' })
    createdAt;

    @SqlIndex({ name: 'IX_User_Name' })
    name;
}
```

**Index Types:**
- `NON_CLUSTERED` - Non-clustered index (default)
- `UNIQUE` - Unique index
- `CLUSTERED` - Clustered index

#### `@SqlCompositeIndex(options)`
Creates a composite index across multiple properties.

```javascript
@ExportToSQL()
@SqlCompositeIndex({ 
    name: 'IX_User_Name_Department', 
    columns: ['name', 'departmentId'] 
})
@SqlCompositeIndex({ 
    name: 'IX_User_Email_Status', 
    columns: ['email', 'status'], 
    type: 'UNIQUE' 
})
class User {
    name;
    departmentId;
    email;
    status;
}
```

## TypeORM Compatibility

The SQL Schema Generator is compatible with TypeORM decorators:

```javascript
import { Entity, PrimaryGeneratedColumn, Column, ManyToOne, OneToMany, CreateDateColumn, UpdateDateColumn } from 'typeorm';

@ExportToSQL()
@Entity('products')
export class Product {
    @PrimaryGeneratedColumn()
    id;

    @Column({ type: 'varchar', length: 100, nullable: false })
    name;

    @Column({ type: 'varchar', length: 50, unique: true })
    code;

    @Column({ type: 'decimal', precision: 10, scale: 2, nullable: false })
    price;

    @ManyToOne('Category', 'products')
    category;

    @Column({ name: 'category_id' })
    categoryId;

    @OneToMany('OrderItem', 'product')
    orderItems;

    @CreateDateColumn({ type: 'datetime2' })
    createdAt;

    @UpdateDateColumn({ type: 'datetime2' })
    updatedAt;
}

@ExportToSQL()
@Entity('categories')
export class Category {
    @PrimaryGeneratedColumn()
    id;

    @Column({ type: 'varchar', length: 50, nullable: false, unique: true })
    name;

    @OneToMany('Product', 'category')
    products;
}
```

## Sequelize Compatibility

The SQL Schema Generator is compatible with Sequelize model definitions:

```javascript
import { DataTypes } from 'sequelize';
import sequelize from '../config/database.js';

@ExportToSQL()
const User = sequelize.define('User', {
    id: {
        type: DataTypes.INTEGER,
        primaryKey: true,
        autoIncrement: true
    },
    name: {
        type: DataTypes.STRING(100),
        allowNull: false
    },
    email: {
        type: DataTypes.STRING(255),
        unique: true,
        allowNull: false
    },
    departmentId: {
        type: DataTypes.INTEGER,
        allowNull: false,
        references: {
            model: 'departments',
            key: 'id'
        }
    }
}, {
    tableName: 'users',
    timestamps: true
});

@ExportToSQL()
const Department = sequelize.define('Department', {
    id: {
        type: DataTypes.INTEGER,
        primaryKey: true,
        autoIncrement: true
    },
    name: {
        type: DataTypes.STRING(50),
        allowNull: false,
        unique: true
    },
    code: {
        type: DataTypes.STRING(10),
        allowNull: false,
        unique: true
    }
}, {
    tableName: 'departments'
});
```

## Advanced Examples

### Complex Entity with Relationships

```javascript
import { v4 as uuidv4 } from 'uuid';
import { ExportToSQL, SqlTable, SqlSchema, SqlType, SqlConstraints, SqlForeignKey, SqlIndex, SqlDefault, SqlCompositeIndex } from '../decorators/sql-decorators.js';

@ExportToSQL()
@SqlTable('orders')
@SqlSchema('sales')
@SqlCompositeIndex({ 
    name: 'IX_Order_Customer_Date', 
    columns: ['customerId', 'createdAt'] 
})
class Order {
    @SqlType('UNIQUEIDENTIFIER')
    @SqlConstraints(['PRIMARY_KEY'])
    @SqlDefault('NEWID()')
    id;

    @SqlType('NVARCHAR', { length: 20 })
    @SqlConstraints(['NOT_NULL', 'UNIQUE'])
    @SqlIndex({ type: 'UNIQUE', name: 'IX_Order_OrderNumber' })
    orderNumber;

    @SqlForeignKey({ table: 'customers', column: 'id' })
    @SqlIndex({ name: 'IX_Order_CustomerId' })
    customerId;

    @SqlType('DECIMAL', { precision: 10, scale: 2 })
    @SqlConstraints(['NOT_NULL'])
    totalAmount;

    @SqlType('NVARCHAR', { length: 20 })
    @SqlConstraints(['NOT_NULL'])
    @SqlDefault("'Pending'")
    status;

    @SqlType('DATETIME2')
    @SqlConstraints(['NOT_NULL'])
    @SqlDefault('GETUTCDATE()')
    createdAt;

    @SqlType('DATETIME2')
    @SqlConstraints(['NOT_NULL'])
    @SqlDefault('GETUTCDATE()')
    updatedAt;

    // Navigation properties (ignored in schema generation)
    @SqlIgnore('Navigation property')
    customer;

    @SqlIgnore('Navigation property')
    orderItems = [];

    constructor(data = {}) {
        Object.assign(this, data);
        this.id = this.id || uuidv4();
        this.createdAt = this.createdAt || new Date();
        this.updatedAt = this.updatedAt || new Date();
    }

    addItem(item) {
        this.orderItems.push(item);
        this.calculateTotal();
    }

    calculateTotal() {
        this.totalAmount = this.orderItems.reduce(
            (sum, item) => sum + (item.quantity * item.unitPrice), 0
        );
    }
}

@ExportToSQL()
@SqlTable('order_items')
class OrderItem {
    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;

    @SqlForeignKey({ 
        table: 'orders', 
        column: 'id', 
        onDelete: 'CASCADE' 
    })
    orderId;

    @SqlForeignKey({ table: 'products', column: 'id' })
    productId;

    @SqlType('INT')
    @SqlConstraints(['NOT_NULL', 'CHECK'], { expression: 'quantity > 0' })
    quantity;

    @SqlType('DECIMAL', { precision: 10, scale: 2 })
    @SqlConstraints(['NOT_NULL'])
    unitPrice;

    // Navigation properties
    @SqlIgnore('Navigation property')
    order;

    @SqlIgnore('Navigation property')
    product;

    constructor(data = {}) {
        Object.assign(this, data);
    }

    getLineTotal() {
        return this.quantity * this.unitPrice;
    }
}

@ExportToSQL()
@SqlTable('customers')
class Customer {
    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;

    @SqlType('NVARCHAR', { length: 50 })
    @SqlConstraints(['NOT_NULL'])
    firstName;

    @SqlType('NVARCHAR', { length: 50 })
    @SqlConstraints(['NOT_NULL'])
    lastName;

    @SqlType('NVARCHAR', { length: 255 })
    @SqlConstraints(['UNIQUE', 'NOT_NULL'])
    @SqlIndex({ name: 'IX_Customer_Email' })
    email;

    @SqlType('NVARCHAR', { length: 20 })
    phone;

    @SqlType('NVARCHAR', { length: 500 })
    address;

    @SqlType('DATETIME2')
    @SqlConstraints(['NOT_NULL'])
    @SqlDefault('GETUTCDATE()')
    createdAt;

    // Navigation properties
    @SqlIgnore('Navigation property')
    orders = [];

    constructor(data = {}) {
        Object.assign(this, data);
        this.createdAt = this.createdAt || new Date();
    }

    get fullName() {
        return `${this.firstName} ${this.lastName}`;
    }
}
```

### ES6 Module Pattern

```javascript
// models/base.js
export class BaseModel {
    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;

    @SqlType('DATETIME2')
    @SqlDefault('GETUTCDATE()')
    createdAt;

    @SqlType('DATETIME2')
    @SqlDefault('GETUTCDATE()')
    updatedAt;

    constructor(data = {}) {
        Object.assign(this, data);
        this.createdAt = this.createdAt || new Date();
        this.updatedAt = this.updatedAt || new Date();
    }
}

// models/user.js
import { BaseModel } from './base.js';
import { ExportToSQL, SqlType, SqlConstraints } from '../decorators/sql-decorators.js';

@ExportToSQL()
export class User extends BaseModel {
    @SqlType('NVARCHAR', { length: 100 })
    @SqlConstraints(['NOT_NULL'])
    name;

    @SqlType('NVARCHAR', { length: 255 })
    @SqlConstraints(['UNIQUE', 'NOT_NULL'])
    email;

    constructor(data = {}) {
        super(data);
    }
}
```

### Custom Tracking Decorator

```javascript
// If using a custom tracking decorator name
// Set TRACK_ATTRIBUTE=DatabaseEntity

@DatabaseEntity()
class Customer {
    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;
    
    // Properties...
}
```

## Build Configuration

### Package.json

```json
{
  "name": "myapp",
  "version": "1.0.0",
  "type": "module",
  "main": "dist/index.js",
  "scripts": {
    "build": "webpack --mode=production",
    "build:dev": "webpack --mode=development",
    "start": "node dist/index.js",
    "test": "jest"
  },
  "dependencies": {
    "uuid": "^9.0.0",
    "typeorm": "^0.3.0",
    "sequelize": "^6.32.0"
  },
  "devDependencies": {
    "@babel/core": "^7.22.0",
    "@babel/preset-env": "^7.22.0",
    "@babel/plugin-proposal-decorators": "^7.22.0",
    "webpack": "^5.88.0",
    "webpack-cli": "^5.1.0",
    "babel-loader": "^9.1.0"
  }
}
```

### Babel Configuration

```javascript
// babel.config.js
module.exports = {
  presets: [
    ['@babel/preset-env', {
      targets: { node: '18' }
    }]
  ],
  plugins: [
    ['@babel/plugin-proposal-decorators', { legacy: true }]
  ]
};
```

### Webpack Configuration

```javascript
// webpack.config.js
const path = require('path');

module.exports = {
  entry: './src/index.js',
  output: {
    path: path.resolve(__dirname, 'dist'),
    filename: 'bundle.js'
  },
  module: {
    rules: [
      {
        test: /\.js$/,
        exclude: /node_modules/,
        use: {
          loader: 'babel-loader'
        }
      }
    ]
  },
  target: 'node',
  mode: 'production'
};
```

## Pipeline Integration

### Azure DevOps

```yaml
variables:
  LANGUAGE_JAVASCRIPT: true
  TRACK_ATTRIBUTE: ExportToSQL
  LICENSE_SERVER: https://license.company.com
  DATABASE_SQLSERVER: true
  DATABASE_SERVER: $(DatabaseServer)
  DATABASE_NAME: $(DatabaseName)
  ENVIRONMENT: prod
  MODE: execute
  JS_SOURCE_PATHS: "$(Build.ArtifactStagingDirectory)/dist"
  JS_MODULE_TYPE: "esm"

resources:
  containers:
  - container: schema_generator
    image: myregistry.azurecr.io/sql-schema-generator:1.0.0
    options: --volume $(Build.SourcesDirectory):/src

jobs:
- job: deploy_schema
  container: schema_generator
  steps:
  - task: NodeTool@0
    displayName: 'Setup Node.js'
    inputs:
      versionSpec: '18.x'
  
  - script: npm ci
    displayName: 'Install Dependencies'
  
  - script: npm run build
    displayName: 'Build JavaScript Application'
  
  - script: /app/sql-schema-generator
    displayName: 'Deploy Database Schema'
```

### GitHub Actions

```yaml
env:
  LANGUAGE_JAVASCRIPT: true
  TRACK_ATTRIBUTE: ExportToSQL
  LICENSE_SERVER: https://license.company.com
  DATABASE_SQLSERVER: true
  DATABASE_SERVER: ${{ secrets.DATABASE_SERVER }}
  DATABASE_NAME: MyApplication
  ENVIRONMENT: prod
  MODE: execute
  JS_MODULE_TYPE: esm

jobs:
  deploy-schema:
    runs-on: ubuntu-latest
    container:
      image: myregistry.azurecr.io/sql-schema-generator:1.0.0
      options: --volume ${{ github.workspace }}:/src
    steps:
      - name: Setup Node.js
        uses: actions/setup-node@v3
        with:
          node-version: '18'
          cache: 'npm'
      
      - name: Install Dependencies
        run: npm ci
      
      - name: Build Application
        run: npm run build
      
      - name: Deploy Database Schema
        run: /app/sql-schema-generator
```

## Output Files

The tool generates several output files in your mounted volume:

### pipeline-tools.log
```
sql-schema-generator=1.0.0
```

### schema-analysis.json
```json
{
  "tool_name": "sql-schema-generator",
  "language": "javascript",
  "module_type": "esm",
  "classes_discovered": 15,
  "tables_generated": 15,
  "constraints_generated": 23,
  "indexes_generated": 8,
  "deployment_risk_level": "Safe"
}
```

### compiled-deployment.sql
```sql
-- Generated SQL deployment script
CREATE TABLE [dbo].[users] (
    [id] INT IDENTITY(1,1) NOT NULL,
    [name] NVARCHAR(100) NOT NULL,
    [email] NVARCHAR(255) NULL,
    [department_id] INT NOT NULL,
    [created_at] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_users] PRIMARY KEY ([id])
);

CREATE INDEX [IX_users_department_id] ON [dbo].[users] ([department_id]);

ALTER TABLE [dbo].[users] 
ADD CONSTRAINT [FK_users_departments_department_id] 
FOREIGN KEY ([department_id]) REFERENCES [dbo].[departments] ([id]);
```

## Best Practices

### Class Design

1. **Always use the tracking decorator** on classes you want in the database
2. **Explicitly define SQL types** for important properties to avoid defaults
3. **Use foreign key decorators** for referential integrity
4. **Add indexes** on frequently queried columns
5. **Separate navigation properties** with `@SqlIgnore`

### Build Management

1. **Use modern build tools** (Webpack, Rollup, Vite)
2. **Configure Babel** for decorator support
3. **Generate source maps** for debugging
4. **Organize modules** for better analysis

### Schema Organization

1. **Use schemas** to organize related tables
2. **Follow JavaScript naming conventions** that translate well to SQL
3. **Document complex relationships** in JSDoc comments
4. **Use consistent decorator patterns**

### Deployment Safety

1. **Always validate first** with `MODE=validate` before `MODE=execute`
2. **Enable backups** for production deployments
3. **Review risk assessment** output before proceeding
4. **Test in staging** environments first

## Troubleshooting

### Common Issues

**No Classes Discovered:**
```
[ERROR] No classes found with decorator: @ExportToSQL
```
*Solution: Ensure classes are marked with tracking decorator and files are built*

**Static Analysis Failed:**
```
[ERROR] Failed to analyze JavaScript file: src/models/user.js
```
*Solution: Verify JavaScript syntax and decorator configuration*

**Database Connection Failed:**
```
[ERROR] Failed to connect to database: sql.company.com
```
*Solution: Check DATABASE_SERVER, credentials, and network connectivity*

**Module Type Detection Failed:**
```
[ERROR] Failed to detect module type
```
*Solution: Set JS_MODULE_TYPE environment variable or check package.json*

**Decorator Processing Failed:**
```
[ERROR] Failed to process decorator on class: User
```
*Solution: Ensure decorators are properly imported and Babel is configured*

### Debug Mode

Enable comprehensive logging:
```bash
VERBOSE=true
LOG_LEVEL=DEBUG
SCHEMA_DUMP=true
```

This outputs:
- File loading details
- Class discovery process
- Static analysis results
- SQL generation steps
- Module system analysis

## Integration Examples

### Multi-Package Project

```bash
# Build all packages
npm run build:models
npm run build:services
npm run build:api

# Set source paths to include all packages
JS_SOURCE_PATHS="packages/models/dist:packages/services/dist:packages/api/dist"

# Run schema generator
docker run --env JS_SOURCE_PATHS="$JS_SOURCE_PATHS" ...
```

### Custom Decorator Names

```javascript
// Use custom tracking decorator
@MyDatabaseEntity()
class Customer {
    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;
}

// Configure environment
TRACK_ATTRIBUTE=MyDatabaseEntity
```

### Express.js Integration

```javascript
// app.js
import express from 'express';
import { User, Order, Customer } from './models/index.js';

const app = express();

// SQL Schema Generator discovers classes via static analysis
// No additional configuration needed

app.get('/users', async (req, res) => {
    // Application logic using decorated classes
});

export default app;
```

### Microservices Architecture

```javascript
// user-service/models/user.js
@ExportToSQL()
@SqlSchema('user_service')
class User {
    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;
    
    @SqlType('NVARCHAR', { length: 100 })
    name;
}

// order-service/models/order.js
@ExportToSQL()
@SqlSchema('order_service')
class Order {
    @SqlConstraints(['PRIMARY_KEY', 'IDENTITY'])
    id;
    
    @SqlForeignKey({ table: 'user_service.users', column: 'id' })
    userId;
}
```

## Support

For issues and questions:
- Enable debug logging (`VERBOSE=true`)
- Verify all required environment variables are set
- Ensure files are built and accessible
- Check the generated `schema-analysis.json` for discovery details
- Access documentation at `http://container:8080/docs/javascript`
- Download decorators at `http://container:8080/javascript`