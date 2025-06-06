# AtomicService Usage Guide

The `AtomicService` provides both simple atomic operations and hierarchical parent-child atomic operations for managing database transactions in .NET applications.
 
## Table of Contents

1. [Setup and Dependency Injection](#setup-and-dependency-injection)
2. [Simple Atomic Operations](#simple-atomic-operations)
3. [Hierarchical Atomic Operations](#hierarchical-atomic-operations)
4. [Service Examples](#service-examples)
5. [Controller Usage Examples](#controller-usage-examples)
6. [Best Practices](#best-practices)

## Setup and Dependency Injection

### Register Services in Program.cs or Startup.cs

```csharp
// Program.cs (NET 6+)
builder.Services.AddScoped<IAtomicService, AtomicService>();
builder.Services.AddScoped<AService>();
builder.Services.AddScoped<BService>();
builder.Services.AddScoped<CService>();

// Or in Startup.cs (older versions)
services.AddScoped<IAtomicService, AtomicService>();
services.AddScoped<AService>();
services.AddScoped<BService>();
services.AddScoped<CService>();
```

## Simple Atomic Operations

### Basic Usage

```csharp
public class OrderService
{
    private readonly IAtomicService _atomicService;
    
    public OrderService(IAtomicService atomicService)
    {
        _atomicService = atomicService;
    }
    
    public async Task CreateOrderAsync(Order order)
    {
        await _atomicService.ExecuteAtomicallyAsync(async () =>
        {
            // All operations within this block are atomic
            await SaveOrderToDatabase(order);
            await UpdateInventory(order.Items);
            await SendConfirmationEmail(order.CustomerEmail);
            
            // Transaction commits automatically if no exception is thrown
        });
    }
}
```

### With Cancellation Token

```csharp
public async Task ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken)
{
    await _atomicService.ExecuteAtomicallyAsync(async () =>
    {
        await ChargeCustomer(payment, cancellationToken);
        await UpdateAccountBalance(payment.AccountId, payment.Amount, cancellationToken);
        await LogTransaction(payment, cancellationToken);
    }, cancellationToken);
}
```

## Hierarchical Atomic Operations

### Parent-Child Pattern

```csharp
// Parent service orchestrates the transaction
public class OrderOrchestrator
{
    private readonly IAtomicService _atomicService;
    private readonly OrderService _orderService;
    private readonly InventoryService _inventoryService;
    private readonly PaymentService _paymentService;
    
    public async Task ProcessCompleteOrderAsync(OrderRequest request)
    {
        // Parent atomic operation - controls commit/rollback
        await _atomicService.ExecuteAsParentAsync(async () =>
        {
            Log.Information("Starting complete order processing");
            
            // All child operations participate in the same transaction
            await _orderService.CreateOrderAsChildAsync(request.Order);
            await _inventoryService.ReserveItemsAsChildAsync(request.Items);
            await _paymentService.ProcessPaymentAsChildAsync(request.Payment);
            
            Log.Information("All operations completed successfully");
            // Transaction commits here
        });
    }
}

// Child services participate in parent transaction
public class OrderService
{
    private readonly IAtomicService _atomicService;
    
    public async Task CreateOrderAsChildAsync(Order order)
    {
        await _atomicService.ExecuteAsChildAsync(async () =>
        {
            Log.Information($"Creating order as child operation. IsChild: {_atomicService.IsChildAtomicOperation}");
            
            await SaveOrderToDatabase(order);
            await GenerateOrderNumber(order);
            
            // No commit here - parent controls transaction
        });
    }
}
```

### Explicit Child Process

```csharp
public async Task ExecuteAsExplicitChild()
{
    // Explicitly mark as child process
    await _atomicService.ExecuteChildAtomicallyAsync(async () =>
    {
        await PerformChildOperations();
    }, isChildProcess: true);
}

public async Task ExecuteAsParent()
{
    // Explicitly mark as parent process
    await _atomicService.ExecuteChildAtomicallyAsync(async () =>
    {
        await PerformParentOperations();
        await CallChildServices();
    }, isChildProcess: false);
}
```

## Service Examples

### AService (Parent Orchestrator)

```csharp
public class AService
{
    private readonly IAtomicService _atomicService;
    private readonly BService _bService;
    private readonly CService _cService;

    public async Task ExecuteParentOperationAsync(CancellationToken cancellationToken = default)
    {
        await _atomicService.ExecuteAsParentAsync(async () =>
        {
            AppLogger.Log("AService: Starting parent atomic operation");

            // Perform AService operations
            await PerformAServiceOperations(cancellationToken);

            // Call child services - they participate in this transaction
            await _bService.ExecuteChildOperationAsync(cancellationToken);
            await _cService.ExecuteChildOperationAsync(cancellationToken);

            AppLogger.Log("AService: All operations completed successfully");
            // Transaction commits automatically
        }, cancellationToken);
    }

    public async Task ExecuteSimpleOperationAsync(CancellationToken cancellationToken = default)
    {
        await _atomicService.ExecuteAtomicallyAsync(async () =>
        {
            AppLogger.Log("AService: Executing simple atomic operation");
            await PerformAServiceOperations(cancellationToken);
        }, cancellationToken);
    }
}
```

### BService (Flexible Child/Parent)

```csharp
public class BService
{
    private readonly IAtomicService _atomicService;

    // Called as child by parent service
    public async Task ExecuteChildOperationAsync(CancellationToken cancellationToken = default)
    {
        await _atomicService.ExecuteAsChildAsync(async () =>
        {
            Log.Information($"BService: Executing as child. IsChild: {_atomicService.IsChildAtomicOperation}");
            await PerformBServiceOperations(cancellationToken);
        }, cancellationToken);
    }

    // Called independently with its own transaction
    public async Task ExecuteIndependentOperationAsync(CancellationToken cancellationToken = default)
    {
        await _atomicService.ExecuteAsParentAsync(async () =>
        {
            Log.Information("BService: Executing as independent parent");
            await PerformBServiceOperations(cancellationToken);
        }, cancellationToken);
    }

    // Using simple atomic operation (original behavior)
    public async Task ExecuteSimpleOperationAsync(CancellationToken cancellationToken = default)
    {
        await _atomicService.ExecuteAtomicallyAsync(async () =>
        {
            Log.Information("BService: Executing simple independent operation");
            await PerformBServiceOperations(cancellationToken);
        }, cancellationToken);
    }
}
```

## Controller Usage Examples

### Simple Atomic Operation

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly IAtomicService _atomicService;
    private readonly OrderService _orderService;

    [HttpPost("simple")]
    public async Task<IActionResult> CreateSimpleOrder([FromBody] Order order)
    {
        try
        {
            await _atomicService.ExecuteAtomicallyAsync(async () =>
            {
                await _orderService.SaveOrder(order);
                await _orderService.UpdateInventory(order);
                Log.Information("Simple atomic operation executed");
            });
            
            return Ok("Order created successfully");
        }
        catch (Exception ex)
        {
            return BadRequest($"Order creation failed: {ex.Message}");
        }
    }
}
```

### Parent-Child Atomic Operation

```csharp
[HttpPost("parent-child")]
public async Task<IActionResult> CreateComplexOrder([FromBody] ComplexOrderRequest request)
{
    try
    {
        await _atomicService.ExecuteAsParentAsync(async () =>
        {
            Log.Information("Parent atomic operation started");
            
            // All these operations participate in the same transaction
            await _orderService.ExecuteChildOperationAsync(request.Order);
            await _inventoryService.ExecuteChildOperationAsync(request.Items);
            await _paymentService.ExecuteChildOperationAsync(request.Payment);
            
            Log.Information("Parent atomic operation completing");
        });
        
        return Ok("Complex order processed successfully");
    }
    catch (Exception ex)
    {
        return BadRequest($"Order processing failed: {ex.Message}");
    }
}
```

### Orchestrated Operations

```csharp
[HttpPost("orchestrated")]
public async Task<IActionResult> ExecuteOrchestratedOperation()
{
    try
    {
        // AService orchestrates the entire atomic operation
        await _aService.ExecuteParentOperationAsync();
        return Ok("Orchestrated operation completed successfully");
    }
    catch (Exception ex)
    {
        return BadRequest($"Operation failed: {ex.Message}");
    }
}
```

### Mixed Usage Pattern

```csharp
[HttpPost("mixed")]
public async Task<IActionResult> ExecuteMixedUsage()
{
    try
    {
        await _atomicService.ExecuteAsParentAsync(async () =>
        {
            Log.Information("Parent atomic operation");
            
            // Child can use simple method (will participate in parent transaction)
            await _bService.ExecuteSimpleOperationAsync();
            
            // Or explicit child method
            await _bService.ExecuteChildOperationAsync();
            
            // Both participate in the same parent transaction
        });
        
        return Ok("Mixed usage operation completed successfully");
    }
    catch (Exception ex)
    {
        return BadRequest($"Operation failed: {ex.Message}");
    }
}
```

## Best Practices

### 1. Use Parent-Child for Complex Operations

```csharp
// Good: Parent orchestrates, children participate
await _atomicService.ExecuteAsParentAsync(async () =>
{
    await _serviceA.ExecuteAsChildAsync();
    await _serviceB.ExecuteAsChildAsync();
    await _serviceC.ExecuteAsChildAsync();
});
```

### 2. Check Atomic Context

```csharp
public async Task FlexibleOperation()
{
    if (_atomicService.IsInAtomicOperation)
    {
        // Already in atomic context, just execute
        await PerformOperations();
    }
    else
    {
        // Create new atomic operation
        await _atomicService.ExecuteAtomicallyAsync(async () =>
        {
            await PerformOperations();
        });
    }
}
```

### 3. Service Design Patterns

```csharp
// Good: Provide both independent and child operation methods
public class UserService
{
    // For independent use
    public async Task CreateUserAsync(User user)
    {
        await _atomicService.ExecuteAtomicallyAsync(async () =>
        {
            await SaveUser(user);
            await SendWelcomeEmail(user);
        });
    }
    
    // For use within parent atomic operations
    public async Task CreateUserAsChildAsync(User user)
    {
        await _atomicService.ExecuteAsChildAsync(async () =>
        {
            await SaveUser(user);
            await SendWelcomeEmail(user);
        });
    }
}
```

### 4. Error Handling

```csharp
public async Task<Result> ProcessWithErrorHandling()
{
    try
    {
        await _atomicService.ExecuteAsParentAsync(async () =>
        {
            await _operationA.ExecuteAsChildAsync();
            await _operationB.ExecuteAsChildAsync();
            // If any operation fails, entire transaction rolls back
        });
        
        return Result.Success();
    }
    catch (ValidationException ex)
    {
        Log.Warning("Validation failed: {Message}", ex.Message);
        return Result.Failure(ex.Message);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Atomic operation failed");
        return Result.Failure("Operation failed");
    }
}
```

### 5. Performance Considerations

```csharp
// Keep atomic operations focused and short-lived
await _atomicService.ExecuteAsParentAsync(async () =>
{
    // Fast database operations
    await SaveCriticalData();
    await UpdateRelatedRecords();
    
    // Don't include slow operations like external API calls
    // unless they're part of the transaction requirement
});

// Separate non-transactional operations
await SendNotifications(); // Execute after atomic operation
```