# DbSqlExpose

Provides extension methods for a web application to execute SQL database queries.

## Features

- **ExposeDbContext**: Method to expose the database context for handling SQL queries via HTTP methods.
  - Allows execution of SQL queries through HTTP endpoints.
  - Supports different HTTP methods (GET, PUT, POST, DELETE) by default.
  - Provides options for JSON serialization.

## Usage

### ExposeDbContext Method

The `ExposeDbContext` method enables exposing database context for executing SQL queries through HTTP endpoints.

#### Parameters

- `app`: The instance of the WebApplication.
- `pattern` (Optional): Route pattern for the SQL query endpoint. Default is "/Query".
- `methods` (Optional): HTTP methods to be supported. Default includes GET, PUT, POST, DELETE.
- `jsonOptions` (Optional): Options for JSON serialization.

#### Caution

Exposing databases via HTTP can pose security risks, such as SQL injection attacks.
Therefore, consider strong and extensive access restrictions
(e.g., network host binding, IP address filtering, or authentication mechanisms).

### QueryResultMode Enum

The `QueryResultMode` enum represents different modes for SQL query results.

- `None`: No query result expected.
- `Scalar`: Single value result.
- `Enumeration`: Multiple rows result.
- `MultiSet`: Multiple result sets.

## Usage Example

```csharp
// Pre-configured case:
app.ExposeDbContext<MyDbContext>();

// Custom configuration:
app.ExposeDbContext<MyDbContext>("/CustomQueryEndpoint", [HttpMethod.Get, HttpMethod.Post]);
```