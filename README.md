# Enhanced EF Core Unit of Work & AtomicService

This library provides advanced patterns for managing database transactions and repository access in .NET applications using Entity Framework Core. It includes:
- An enhanced Unit of Work pattern with generic and custom repository support
- Hierarchical atomic operations (parent-child transactions) via `AtomicService`
- Bulk operations, validation, and advanced transaction management

## Features

- **Unit of Work**: Centralizes DbContext and repository management, supports both generic and custom repositories.
- **Generic Repository**: Provides common CRUD, query, and paging operations for any entity.
- **Bulk Operations**: Efficiently insert, update, or delete large sets of entities with progress reporting.
- **Validation**: Async entity validation with custom error reporting.
- **Hierarchical Atomic Operations**: Parent-child transaction orchestration for complex workflows.
- **Transaction Control**: Execute operations with custom isolation levels and transaction scopes.
- **Error Handling**: Result patterns and custom exceptions for robust error management.

## Getting Started

### 1. Install & Reference
Add the source files to your project or reference as a shared library.

### 2. Register Services (Dependency Injection)
```csharp
// Program.cs or Startup.cs
services.AddScoped<IAtomicService, AtomicService>();
services.AddScoped<IEFCoreUnitOfWork, EFCoreUnitOfWork>();
services.AddScoped<AService>();
services.AddScoped<BService>();
services.AddScoped<CService>();
```

### 3. Configure DbContext
```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));
```

## Usage Examples

See `uow-usage-examples.md` and `atomic_service_usage.md` for:
- Entity and DbContext setup
- Basic CRUD and advanced repository patterns
- Parent-child atomic operations
- Bulk operations with progress
- Pagination, filtering, and validation
- Error handling and best practices

### Simple Atomic Operation
```csharp
await _atomicService.ExecuteAtomicallyAsync(async () =>
{
    // Your transactional code here
});
```

### Hierarchical Parent-Child Operation
```csharp
await _atomicService.ExecuteAsParentAsync(async () =>
{
    await _bService.ExecuteAsChildAsync();
    await _cService.ExecuteAsChildAsync();
});
```

### Unit of Work with Repository
```csharp
var repo = _unitOfWork.Repository<Product>();
var products = await repo.GetAllAsync();
await _unitOfWork.SaveChangesAsync();
```

### Bulk Insert Example
```csharp
await _unitOfWork.BulkInsertAsync(products, progress);
```

## Best Practices
- Use generic repositories for simple CRUD, custom for domain logic
- Always use `QueryAsNoTracking()` for read-only queries
- Use parent-child atomic operations for orchestrated workflows
- Keep transactions short and focused
- Handle errors and validation before committing

## Testing
- Mock `IEFCoreUnitOfWork` and `IAtomicService` for unit tests
- Use integration tests for transaction scenarios

## More
- See `atomic_service_usage.md` and `uow-usage-examples.md` for full guides, patterns, and real-world examples.
