# SQL Schema Generator - Java Developer Integration Guide

## Documentation Access

This documentation is always available via HTTP endpoint on port 8080:

```bash
# Access Java documentation from running container
curl http://localhost:8080/docs/java

# Or in browser
http://localhost:8080/docs/java

# Download Java annotations reference
curl http://localhost:8080/java > SqlAnnotations.java
```

## Overview

The SQL Schema Generator analyzes Java entities marked with tracking annotations and automatically generates database schema changes with intelligent deployment planning. It uses Java reflection to discover classes, fields, and their SQL metadata annotations.

**Key Features:**
- **Reflection-based analysis** of compiled Java classes and JAR files
- **Annotation-driven** SQL schema definition
- **JPA/Hibernate compatibility** with standard annotations
- **Custom SQL annotations** for advanced schema control
- **29-phase deployment planning** with comprehensive risk assessment
- **Multi-database provider support** (SQL Server, PostgreSQL, MySQL, Oracle, SQLite)

## Quick Start

### Download Java Annotations

```bash
# Download the annotation definitions and helper classes
curl http://localhost:8080/java > SqlAnnotations.java

# Add to your project
mkdir -p src/main/java/com/company/sql/annotations
cp SqlAnnotations.java src/main/java/com/company/sql/annotations/
```

### Basic Entity Definition

```java
package com.company.models;

import com.company.sql.annotations.*;
import javax.persistence.*;
import java.time.LocalDateTime;
import java.util.List;

@ExportToSQL
@Entity
@Table(name = "users")
public class User {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @SqlConstraints({SqlConstraint.PRIMARY_KEY, SqlConstraint.IDENTITY})
    private Long id;

    @Column(nullable = false, length = 100)
    @SqlType(value = SqlDataType.NVARCHAR, length = 100)
    @SqlConstraints(SqlConstraint.NOT_NULL)
    private String name;

    @Column(length = 255)
    @SqlType(value = SqlDataType.NVARCHAR, length = 255)
    @SqlIndex(type = SqlIndexType.UNIQUE)
    private String email;

    @Column(name = "department_id", nullable = false)
    @SqlForeignKey(table = "departments", column = "id")
    private Long departmentId;

    @Column(name = "created_at")
    @SqlType(SqlDataType.DATETIME2)
    @SqlDefault("GETUTCDATE()")
    private LocalDateTime createdAt;

    // Navigation properties
    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "department_id", insertable = false, updatable = false)
    @SqlIgnore
    private Department department;

    @OneToMany(mappedBy = "user", cascade = CascadeType.ALL)
    @SqlIgnore
    private List<Order> orders;

    // Constructors, getters, setters
    public User() {}
    
    // ... getters and setters
}

@ExportToSQL
@Entity
@Table(name = "departments")
public class Department {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @SqlConstraints({SqlConstraint.PRIMARY_KEY, SqlConstraint.IDENTITY})
    private Long id;

    @Column(nullable = false, length = 50, unique = true)
    @SqlType(value = SqlDataType.NVARCHAR, length = 50)
    @SqlConstraints({SqlConstraint.NOT_NULL, SqlConstraint.UNIQUE})
    private String name;

    @Column(length = 10, unique = true)
    @SqlType(value = SqlDataType.VARCHAR, length = 10)
    @SqlConstraints({SqlConstraint.NOT_NULL, SqlConstraint.UNIQUE})
    private String code;

    // Navigation properties
    @OneToMany(mappedBy = "department")
    @SqlIgnore
    private List<User> users;

    // Constructors, getters, setters
    public Department() {}
    
    // ... getters and setters
}
```

### Container Usage

```bash
# Build your Java project first
mvn clean package -DskipTests

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
| `LANGUAGE_JAVA` | Enable Java analysis | `true` |
| `TRACK_ATTRIBUTE` | Annotation name to track for schema generation | `ExportToSQL` |
| `REPO_URL` | Repository URL for context | `https://github.com/company/project` |
| `BRANCH` | Git branch being processed | `main`, `develop` |
| `LICENSE_SERVER` | Licensing server URL | `https://license.company.com` |
| `DATABASE_SQLSERVER` | Target SQL Server (exactly one database provider required) | `true` |
| `DATABASE_SERVER` | Database server hostname | `sql.company.com` |
| `DATABASE_NAME` | Target database name | `MyApplication` |

### Optional Configuration

| Variable | Default | Description | Example |
|----------|---------|-------------|---------|
| `JAR_PATHS` | Auto-discover | Colon-separated paths to compiled JAR files | `target:build/libs` |
| `CLASS_PATHS` | Auto-discover | Colon-separated paths to compiled class directories | `target/classes:build/classes` |
| `BUILD_OUTPUT_PATH` | `target` | Primary build output directory | `build/libs` |
| `JAVA_PACKAGE_PREFIX` | Auto-detect | Java package prefix for entity scanning | `com.company.models` |
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

## Java Annotation Reference

### Class-Level Annotations

#### `@ExportToSQL`
Marks a class for database schema generation.

```java
@ExportToSQL
@Entity
public class Customer {
    // Fields...
}

@ExportToSQL(description = "Core business entity")
@Entity
public class Order {
    // Fields...
}
```

#### `@SqlTable`
Overrides the default table name.

```java
@ExportToSQL
@SqlTable("tbl_customers")
@Entity
public class Customer {
    // Fields...
}
```

#### `@SqlSchema`
Overrides the default schema name.

```java
@ExportToSQL
@SqlSchema("sales")
@Entity
public class Customer {
    // Fields...
}
```

### Field-Level Annotations

#### `@SqlType`
Specifies the SQL data type for a field.

```java
public class Product {
    @SqlType(value = SqlDataType.NVARCHAR, length = 100)
    private String name;

    @SqlType(value = SqlDataType.DECIMAL, precision = 10, scale = 2)
    private BigDecimal price;

    @SqlType(SqlDataType.DATETIME2)
    private LocalDateTime createdAt;

    @SqlType(SqlDataType.BIT)
    private Boolean isActive;
}
```

**Available SQL Data Types:**
- **String**: `NVARCHAR`, `VARCHAR`, `NVARCHAR_MAX`, `VARCHAR_MAX`, `TEXT`, `NTEXT`
- **Numeric**: `INT`, `BIGINT`, `SMALLINT`, `TINYINT`, `DECIMAL`, `FLOAT`, `REAL`, `MONEY`, `SMALLMONEY`
- **Date/Time**: `DATETIME2`, `DATETIME`, `DATE`, `TIME`, `SMALLDATETIME`, `DATETIMEOFFSET`
- **Other**: `BIT`, `UNIQUEIDENTIFIER`, `BINARY`, `VARBINARY`, `VARBINARY_MAX`, `IMAGE`, `XML`, `GEOGRAPHY`, `GEOMETRY`, `TIMESTAMP`

#### `@SqlConstraints`
Applies SQL constraints to a field.

```java
public class User {
    @SqlConstraints(SqlConstraint.NOT_NULL)
    private String name;

    @SqlConstraints({SqlConstraint.PRIMARY_KEY, SqlConstraint.IDENTITY})
    private Long id;

    @SqlConstraints(SqlConstraint.UNIQUE)
    private String email;

    @SqlConstraints(value = SqlConstraint.CHECK, expression = "age >= 0")
    private Integer age;
}
```

**Available Constraints:**
- `NOT_NULL` - NOT NULL constraint
- `UNIQUE` - UNIQUE constraint
- `PRIMARY_KEY` - PRIMARY KEY constraint
- `IDENTITY` - IDENTITY column (auto-increment)
- `CHECK` - CHECK constraint (use with expression parameter)

#### `@SqlForeignKey`
Creates a foreign key relationship.

```java
public class OrderItem {
    @SqlForeignKey(table = "orders", column = "id")
    private Long orderId;

    @SqlForeignKey(table = "products", column = "id")
    private Long productId;

    @SqlForeignKey(table = "users", column = "user_id", 
                   onDelete = CascadeAction.CASCADE)
    private Long createdByUserId;
}
```

**Cascade Actions:**
```java
public enum CascadeAction {
    NO_ACTION,
    CASCADE,
    SET_NULL,
    SET_DEFAULT,
    RESTRICT
}
```

#### `@SqlColumn`
Overrides the default column name.

```java
public class Customer {
    @SqlColumn("customer_name")
    private String name;

    @SqlColumn("email_address")
    private String email;
}
```

#### `@SqlIgnore`
Excludes a field from schema generation.

```java
public class User {
    private String name;
    
    @SqlIgnore
    private String calculatedField;

    @SqlIgnore("Cached value, not persisted")
    private BigDecimal cachedTotal;

    // Navigation properties
    @OneToMany(mappedBy = "user")
    @SqlIgnore
    private List<Order> orders;
}
```

#### `@SqlDefault`
Sets a default value for the column.

```java
public class Order {
    @SqlDefault("'Pending'")
    private String status;

    @SqlDefault("GETUTCDATE()")
    private LocalDateTime createdAt;

    @SqlDefault("1")
    private Boolean isActive;
}
```

### Index Annotations

#### `@SqlIndex`
Creates an index on a field.

```java
public class User {
    @SqlIndex
    private String email;

    @SqlIndex(type = SqlIndexType.UNIQUE)
    private String username;

    @SqlIndex(type = SqlIndexType.CLUSTERED)
    private LocalDateTime createdAt;

    @SqlIndex(name = "IX_User_Name_Email")
    private String name;
}
```

**Index Types:**
```java
public enum SqlIndexType {
    NON_CLUSTERED,
    UNIQUE,
    CLUSTERED
}
```

#### `@SqlCompositeIndex`
Creates a composite index across multiple fields.

```java
@ExportToSQL
@SqlCompositeIndex(name = "IX_User_Name_Department", 
                   columns = {"name", "departmentId"})
@SqlCompositeIndex(name = "IX_User_Email_Status", 
                   columns = {"email", "status"}, 
                   type = SqlIndexType.UNIQUE)
public class User {
    private String name;
    private Long departmentId;
    private String email;
    private String status;
}
```

## JPA/Hibernate Compatibility

The SQL Schema Generator is compatible with standard JPA and Hibernate annotations:

```java
@ExportToSQL
@Entity
@Table(name = "products", schema = "catalog")
public class Product {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, length = 100)
    private String name;

    @Column(name = "product_code", unique = true, length = 50)
    private String code;

    @Column(precision = 10, scale = 2, nullable = false)
    private BigDecimal price;

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "category_id", nullable = false)
    private Category category;

    @OneToMany(mappedBy = "product", cascade = CascadeType.ALL)
    private List<OrderItem> orderItems;

    @CreationTimestamp
    @Column(name = "created_at")
    private LocalDateTime createdAt;

    @UpdateTimestamp
    @Column(name = "updated_at")
    private LocalDateTime updatedAt;
}

@ExportToSQL
@Entity
@Table(name = "categories")
public class Category {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, length = 50, unique = true)
    private String name;

    @OneToMany(mappedBy = "category", cascade = CascadeType.ALL)
    private List<Product> products;
}
```

## Advanced Examples

### Complex Entity with Relationships

```java
package com.company.models;

import com.company.sql.annotations.*;
import javax.persistence.*;
import java.math.BigDecimal;
import java.time.LocalDateTime;
import java.util.List;
import java.util.UUID;

@ExportToSQL
@SqlTable("orders")
@SqlSchema("sales")
@Entity
@SqlCompositeIndex(name = "IX_Order_Customer_Date", 
                   columns = {"customerId", "createdAt"})
public class Order {
    @Id
    @SqlType(SqlDataType.UNIQUEIDENTIFIER)
    @SqlConstraints(SqlConstraint.PRIMARY_KEY)
    @SqlDefault("NEWID()")
    private UUID id;

    @Column(name = "order_number", nullable = false, unique = true, length = 20)
    @SqlType(value = SqlDataType.NVARCHAR, length = 20)
    @SqlConstraints({SqlConstraint.NOT_NULL, SqlConstraint.UNIQUE})
    @SqlIndex(type = SqlIndexType.UNIQUE, name = "IX_Order_OrderNumber")
    private String orderNumber;

    @Column(name = "customer_id", nullable = false)
    @SqlForeignKey(table = "customers", column = "id")
    @SqlIndex(name = "IX_Order_CustomerId")
    private Long customerId;

    @Column(name = "total_amount", precision = 10, scale = 2, nullable = false)
    @SqlType(value = SqlDataType.DECIMAL, precision = 10, scale = 2)
    @SqlConstraints(SqlConstraint.NOT_NULL)
    private BigDecimal totalAmount;

    @Column(nullable = false, length = 20)
    @SqlType(value = SqlDataType.NVARCHAR, length = 20)
    @SqlConstraints(SqlConstraint.NOT_NULL)
    @SqlDefault("'Pending'")
    private String status;

    @Column(name = "created_at", nullable = false)
    @SqlType(SqlDataType.DATETIME2)
    @SqlConstraints(SqlConstraint.NOT_NULL)
    @SqlDefault("GETUTCDATE()")
    @CreationTimestamp
    private LocalDateTime createdAt;

    @Column(name = "updated_at", nullable = false)
    @SqlType(SqlDataType.DATETIME2)
    @SqlConstraints(SqlConstraint.NOT_NULL)
    @SqlDefault("GETUTCDATE()")
    @UpdateTimestamp
    private LocalDateTime updatedAt;

    // Navigation properties (ignored in schema generation)
    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "customer_id", insertable = false, updatable = false)
    @SqlIgnore
    private Customer customer;

    @OneToMany(mappedBy = "order", cascade = CascadeType.ALL, fetch = FetchType.LAZY)
    @SqlIgnore
    private List<OrderItem> orderItems;

    // Constructors
    public Order() {
        this.id = UUID.randomUUID();
    }

    // Getters and setters...
}

@ExportToSQL
@Entity
@Table(name = "order_items")
public class OrderItem {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @SqlConstraints({SqlConstraint.PRIMARY_KEY, SqlConstraint.IDENTITY})
    private Long id;

    @Column(name = "order_id", nullable = false)
    @SqlForeignKey(table = "orders", column = "id", onDelete = CascadeAction.CASCADE)
    private UUID orderId;

    @Column(name = "product_id", nullable = false)
    @SqlForeignKey(table = "products", column = "id")
    private Long productId;

    @Column(nullable = false)
    @SqlType(SqlDataType.INT)
    @SqlConstraints({SqlConstraint.NOT_NULL, SqlConstraint.CHECK})
    @SqlCheckConstraint("quantity > 0")
    private Integer quantity;

    @Column(name = "unit_price", precision = 10, scale = 2, nullable = false)
    @SqlType(value = SqlDataType.DECIMAL, precision = 10, scale = 2)
    @SqlConstraints(SqlConstraint.NOT_NULL)
    private BigDecimal unitPrice;

    // Navigation properties
    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "order_id", insertable = false, updatable = false)
    @SqlIgnore
    private Order order;

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "product_id", insertable = false, updatable = false)
    @SqlIgnore
    private Product product;

    // Constructors, getters, setters...
}
```

### Custom Tracking Annotation

```java
// If using a custom tracking annotation name
// Set TRACK_ATTRIBUTE=DatabaseEntity

@DatabaseEntity
@Entity
public class Customer {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @SqlConstraints({SqlConstraint.PRIMARY_KEY, SqlConstraint.IDENTITY})
    private Long id;
    
    // Fields...
}
```

### Spring Boot Entity Configuration

```java
package com.company.config;

import org.springframework.boot.autoconfigure.domain.EntityScan;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.jpa.repository.config.EnableJpaRepositories;

@Configuration
@EntityScan(basePackages = "com.company.models")
@EnableJpaRepositories(basePackages = "com.company.repositories")
public class JpaConfig {
    // SQL Schema Generator will discover entities via reflection
    // No additional configuration needed
}
```

## Build Configuration

### Maven Configuration

```xml
<?xml version="1.0" encoding="UTF-8"?>
<project xmlns="http://maven.apache.org/POM/4.0.0"
         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
         xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 
         http://maven.apache.org/xsd/maven-4.0.0.xsd">
    <modelVersion>4.0.0</modelVersion>

    <groupId>com.company</groupId>
    <artifactId>myapp</artifactId>
    <version>1.0.0</version>
    <packaging>jar</packaging>

    <properties>
        <maven.compiler.source>17</maven.compiler.source>
        <maven.compiler.target>17</maven.compiler.target>
        <spring.boot.version>3.1.0</spring.boot.version>
        <hibernate.version>6.2.0.Final</hibernate.version>
    </properties>

    <dependencies>
        <dependency>
            <groupId>org.springframework.boot</groupId>
            <artifactId>spring-boot-starter-data-jpa</artifactId>
            <version>${spring.boot.version}</version>
        </dependency>
        
        <dependency>
            <groupId>org.hibernate</groupId>
            <artifactId>hibernate-core</artifactId>
            <version>${hibernate.version}</version>
        </dependency>
        
        <dependency>
            <groupId>com.microsoft.sqlserver</groupId>
            <artifactId>mssql-jdbc</artifactId>
            <version>12.2.0.jre17</version>
        </dependency>
    </dependencies>

    <build>
        <plugins>
            <plugin>
                <groupId>org.springframework.boot</groupId>
                <artifactId>spring-boot-maven-plugin</artifactId>
                <version>${spring.boot.version}</version>
            </plugin>
            
            <plugin>
                <groupId>org.apache.maven.plugins</groupId>
                <artifactId>maven-compiler-plugin</artifactId>
                <version>3.11.0</version>
                <configuration>
                    <source>17</source>
                    <target>17</target>
                </configuration>
            </plugin>
        </plugins>
    </build>
</project>
```

### Gradle Configuration

```groovy
plugins {
    id 'java'
    id 'org.springframework.boot' version '3.1.0'
    id 'io.spring.dependency-management' version '1.1.0'
}

group = 'com.company'
version = '1.0.0'
sourceCompatibility = '17'

repositories {
    mavenCentral()
}

dependencies {
    implementation 'org.springframework.boot:spring-boot-starter-data-jpa'
    implementation 'org.hibernate:hibernate-core:6.2.0.Final'
    implementation 'com.microsoft.sqlserver:mssql-jdbc:12.2.0.jre17'
    
    testImplementation 'org.springframework.boot:spring-boot-starter-test'
}

jar {
    enabled = false
}

bootJar {
    enabled = true
    archiveClassifier = ''
}
```

## Pipeline Integration

### Azure DevOps

```yaml
variables:
  LANGUAGE_JAVA: true
  TRACK_ATTRIBUTE: ExportToSQL
  LICENSE_SERVER: https://license.company.com
  DATABASE_SQLSERVER: true
  DATABASE_SERVER: $(DatabaseServer)
  DATABASE_NAME: $(DatabaseName)
  ENVIRONMENT: prod
  MODE: execute
  JAR_PATHS: "$(Build.ArtifactStagingDirectory)/target"
  JAVA_PACKAGE_PREFIX: "com.company.models"

resources:
  containers:
  - container: schema_generator
    image: myregistry.azurecr.io/sql-schema-generator:1.0.0
    options: --volume $(Build.SourcesDirectory):/src

jobs:
- job: deploy_schema
  container: schema_generator
  steps:
  - task: Maven@3
    displayName: 'Build Java Application'
    inputs:
      mavenPomFile: 'pom.xml'
      goals: 'clean package'
      options: '-DskipTests'
      publishJUnitResults: false
  
  - script: /app/sql-schema-generator
    displayName: 'Deploy Database Schema'
```

### GitHub Actions

```yaml
env:
  LANGUAGE_JAVA: true
  TRACK_ATTRIBUTE: ExportToSQL
  LICENSE_SERVER: https://license.company.com
  DATABASE_SQLSERVER: true
  DATABASE_SERVER: ${{ secrets.DATABASE_SERVER }}
  DATABASE_NAME: MyApplication
  ENVIRONMENT: prod
  MODE: execute
  JAVA_PACKAGE_PREFIX: com.company.models

jobs:
  deploy-schema:
    runs-on: ubuntu-latest
    container:
      image: myregistry.azurecr.io/sql-schema-generator:1.0.0
      options: --volume ${{ github.workspace }}:/src
    steps:
      - name: Setup JDK 17
        uses: actions/setup-java@v3
        with:
          java-version: '17'
          distribution: 'temurin'
      
      - name: Build with Maven
        run: mvn clean package -DskipTests
      
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
  "language": "java",
  "package_prefix": "com.company.models",
  "entities_discovered": 15,
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
    [id] BIGINT IDENTITY(1,1) NOT NULL,
    [name] NVARCHAR(100) NOT NULL,
    [email] NVARCHAR(255) NULL,
    [department_id] BIGINT NOT NULL,
    [created_at] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_users] PRIMARY KEY ([id])
);

CREATE INDEX [IX_users_department_id] ON [dbo].[users] ([department_id]);

ALTER TABLE [dbo].[users] 
ADD CONSTRAINT [FK_users_departments_department_id] 
FOREIGN KEY ([department_id]) REFERENCES [dbo].[departments] ([id]);
```

## Best Practices

### Entity Design

1. **Always use the tracking annotation** on entities you want in the database
2. **Explicitly define SQL types** for important fields to avoid defaults
3. **Use foreign key annotations** for referential integrity
4. **Add indexes** on frequently queried columns
5. **Combine JPA and SQL annotations** for maximum compatibility

### Build Management

1. **Use Maven or Gradle** for dependency management
2. **Package as JAR files** for production deployments
3. **Include all dependencies** in the build output
4. **Use consistent Java versions** across environments

### Schema Organization

1. **Use schemas** to organize related tables
2. **Follow Java naming conventions** that translate well to SQL
3. **Document complex relationships** in Javadoc comments
4. **Use consistent annotation patterns**

### Deployment Safety

1. **Always validate first** with `MODE=validate` before `MODE=execute`
2. **Enable backups** for production deployments
3. **Review risk assessment** output before proceeding
4. **Test in staging** environments first

## Troubleshooting

### Common Issues

**No Entities Discovered:**
```
[ERROR] No entities found with annotation: @ExportToSQL
```
*Solution: Ensure entities are marked with tracking annotation and JARs are built*

**JAR Load Failure:**
```
[ERROR] Failed to load JAR: target/myapp-1.0.0.jar
```
*Solution: Verify JAR_PATHS points to compiled JARs and build succeeded*

**Database Connection Failed:**
```
[ERROR] Failed to connect to database: sql.company.com
```
*Solution: Check DATABASE_SERVER, credentials, and network connectivity*

**Package Scanning Failed:**
```
[ERROR] Failed to scan package: com.company.models
```
*Solution: Set JAVA_PACKAGE_PREFIX or ensure package structure is correct*

**Annotation Processing Failed:**
```
[ERROR] Failed to process annotation on class: User
```
*Solution: Ensure annotations are properly imported and classes are public*

### Debug Mode

Enable comprehensive logging:
```bash
VERBOSE=true
LOG_LEVEL=DEBUG
SCHEMA_DUMP=true
```

This outputs:
- JAR loading details
- Entity discovery process
- Reflection analysis results
- SQL generation steps
- Package scanning details

## Integration Examples

### Multi-Module Maven Project

```xml
<!-- Parent POM -->
<modules>
    <module>core-entities</module>
    <module>user-service</module>
    <module>order-service</module>
</modules>

<!-- Build all modules -->
<build>
    <plugins>
        <plugin>
            <groupId>org.apache.maven.plugins</groupId>
            <artifactId>maven-assembly-plugin</artifactId>
            <configuration>
                <descriptorRefs>
                    <descriptorRef>jar-with-dependencies</descriptorRef>
                </descriptorRefs>
            </configuration>
        </plugin>
    </plugins>
</build>
```

```bash
# Build all modules
mvn clean package

# Set JAR paths to include all modules
JAR_PATHS="core-entities/target:user-service/target:order-service/target"

# Run schema generator
docker run --env JAR_PATHS="$JAR_PATHS" ...
```

### Custom Annotation Names

```java
// Use custom tracking annotation
@MyDatabaseEntity
@Entity
public class Customer {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @SqlConstraints({SqlConstraint.PRIMARY_KEY, SqlConstraint.IDENTITY})
    private Long id;
}

// Configure environment
TRACK_ATTRIBUTE=MyDatabaseEntity
```

### Spring Boot Integration

```java
package com.company;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.autoconfigure.domain.EntityScan;

@SpringBootApplication
@EntityScan(basePackages = "com.company.models")
public class Application {
    public static void main(String[] args) {
        SpringApplication.run(Application.class, args);
        
        // SQL Schema Generator discovers entities via reflection
        // No additional configuration needed
    }
}
```

### Microservices Architecture

```java
// User service entities
package com.company.user.models;

@ExportToSQL
@SqlSchema("user_service")
@Entity
public class User {
    @Id
    private Long id;
    
    @SqlType(value = SqlDataType.NVARCHAR, length = 100)
    private String name;
}

// Order service entities
package com.company.order.models;

@ExportToSQL
@SqlSchema("order_service")
@Entity
public class Order {
    @Id
    private Long id;
    
    @SqlForeignKey(table = "user_service.users", column = "id")
    private Long userId;
}
```

## Support

For issues and questions:
- Enable debug logging (`VERBOSE=true`)
- Verify all required environment variables are set
- Ensure JARs are built and accessible
- Check the generated `schema-analysis.json` for discovery details
- Access documentation at `http://container:8080/docs/java`
- Download annotations at `http://container:8080/java`