# EF Core Unit of Work - Usage Examples

## Table of Contents
- [Setup and Configuration](#setup-and-configuration)
- [Basic CRUD Operations](#basic-crud-operations)
- [Advanced Repository Patterns](#advanced-repository-patterns)
- [Transaction Management](#transaction-management)
- [Bulk Operations](#bulk-operations)
- [Pagination and Filtering](#pagination-and-filtering)
- [Validation](#validation)
- [Performance Optimization](#performance-optimization)
- [Error Handling](#error-handling)
- [Real-World Service Examples](#real-world-service-examples)

## Setup and Configuration

### 1. Entity Models

```csharp
// Domain Entities
public class Customer
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    public List<Order> Orders { get; set; } = new();
}

public class Order
{
    public int Id { get; set; }
    
    [Required]
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    
    [Required]
    public DateTime OrderDate { get; set; }
    
    [Range(0.01, double.MaxValue)]
    public decimal Total { get; set; }
    
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    
    [Required]
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
    
    [Range(0.01, double.MaxValue)]
    public decimal UnitPrice { get; set; }
}

public class Product
{
    public int Id { get; set; }
    
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }
    
    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }
    
    public bool IsActive { get; set; } = true;
}

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}
```

### 2. DbContext Configuration

```csharp
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Product> Products { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure relationships
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId);
            
        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId);
            
        // Configure decimal precision
        modelBuilder.Entity<Order>()
            .Property(o => o.Total)
            .HasPrecision(18, 2);
            
        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.UnitPrice)
            .HasPrecision(18, 2);
            
        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);
    }
}
```

### 3. Dependency Injection Setup

```csharp
// Program.cs or Startup.cs
public void ConfigureServices(IServiceCollection services)
{
    // Database
    services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString));
    
    // Unit of Work
    services.AddScoped<IEFCoreUnitOfWork, EFCoreUnitOfWork>();
    services.AddScoped<DbContext>(provider => 
        provider.GetRequiredService<ApplicationDbContext>());
    
    // Custom Repositories (optional)
    services.AddScoped<ICustomerRepository, CustomerRepository>();
    services.AddScoped<IOrderRepository, OrderRepository>();
    
    // Services
    services.AddScoped<CustomerService>();
    services.AddScoped<OrderService>();
    services.AddScoped<ProductService>();
}
```

## Basic CRUD Operations

### Customer Service - Simple CRUD

```csharp
public class CustomerService
{
    private readonly IEFCoreUnitOfWork _unitOfWork;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(IEFCoreUnitOfWork unitOfWork, ILogger<CustomerService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    // Create a new customer
    public async Task<Customer> CreateCustomerAsync(string name, string email)
    {
        return await _unitOfWork.ExecuteAsync(async uow =>
        {
            var customerRepo = uow.Repository<Customer>();
            
            // Check for duplicate email
            var existingCustomer = await customerRepo.FirstOrDefaultAsync(c => c.Email == email);
            if (existingCustomer != null)
            {
                throw new InvalidOperationException($"Customer with email {email} already exists");
            }
            
            var customer = new Customer
            {
                Name = name,
                Email = email,
                CreatedAt = DateTime.UtcNow
            };
            
            customerRepo.Add(customer);
            
            _logger.LogInformation("Created customer {Name} with email {Email}", name, email);
            return customer;
        });
    }

    // Get customer by ID
    public async Task<Customer?> GetCustomerAsync(int id)
    {
        var customerRepo = _unitOfWork.Repository<Customer>();
        return await customerRepo.GetByIdAsync(id);
    }

    // Get all customers with pagination
    public async Task<PagedResult<Customer>> GetCustomersAsync(int pageNumber = 1, int pageSize = 20)
    {
        var customerRepo = _unitOfWork.Repository<Customer>();
        return await customerRepo.GetPagedAsync(
            pageNumber, 
            pageSize, 
            orderBy: c => c.Name);
    }

    // Update customer
    public async Task<Customer> UpdateCustomerAsync(int id, string name, string email)
    {
        return await _unitOfWork.ExecuteAsync(async uow =>
        {
            var customerRepo = uow.Repository<Customer>();
            
            var customer = await customerRepo.GetByIdAsync(id);
            if (customer == null)
            {
                throw new NotFoundException($"Customer with ID {id} not found");
            }
            
            customer.Name = name;
            customer.Email = email;
            customer.UpdatedAt = DateTime.UtcNow;
            
            customerRepo.Update(customer);
            
            return customer;
        });
    }

    // Delete customer
    public async Task DeleteCustomerAsync(int id)
    {
        await _unitOfWork.ExecuteAsync(async uow =>
        {
            var customerRepo = uow.Repository<Customer>();
            await customerRepo.RemoveByIdAsync(id);
        });
    }
}
```

## Advanced Repository Patterns

### Custom Repository with Specific Business Logic

```csharp
public interface IOrderRepository
{
    Task<Order?> GetOrderWithDetailsAsync(int orderId);
    Task<List<Order>> GetCustomerOrdersAsync(int customerId, int months = 12);
    Task<List<Order>> GetOrdersByStatusAsync(OrderStatus status);
    Task<decimal> GetCustomerTotalSpentAsync(int customerId);
    Task<bool> ValidateOrderAsync(Order order);
}

public class OrderRepository : GenericRepository<Order>, IOrderRepository
{
    public OrderRepository(DbContext context) : base(context) { }

    public async Task<Order?> GetOrderWithDetailsAsync(int orderId)
    {
        return await _dbSet
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);
    }

    public async Task<List<Order>> GetCustomerOrdersAsync(int customerId, int months = 12)
    {
        var cutoffDate = DateTime.UtcNow.AddMonths(-months);
        
        return await _dbSet
            .Where(o => o.CustomerId == customerId && o.OrderDate >= cutoffDate)
            .Include(o => o.Items)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();
    }

    public async Task<List<Order>> GetOrdersByStatusAsync(OrderStatus status)
    {
        return await _dbSet
            .Where(o => o.Status == status)
            .Include(o => o.Customer)
            .ToListAsync();
    }

    public async Task<decimal> GetCustomerTotalSpentAsync(int customerId)
    {
        return await _dbSet
            .Where(o => o.CustomerId == customerId)
            .SumAsync(o => o.Total);
    }

    public async Task<bool> ValidateOrderAsync(Order order)
    {
        // Custom business validation
        if (!order.Items.Any())
            return false;
            
        // Check if all products are available
        foreach (var item in order.Items)
        {
            var product = await _context.Set<Product>()
                .FirstOrDefaultAsync(p => p.Id == item.ProductId);
                
            if (product == null || !product.IsActive || product.StockQuantity < item.Quantity)
                return false;
        }
        
        return true;
    }
}
```

## Transaction Management

### Complex Multi-Entity Operations

```csharp
public class OrderService
{
    private readonly IEFCoreUnitOfWork _unitOfWork;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IEFCoreUnitOfWork unitOfWork, ILogger<OrderService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    // Complex order creation with inventory management
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        return await _unitOfWork.ExecuteInTransactionAsync(async uow =>
        {
            var orderRepo = uow.GetRepository<IOrderRepository>();
            var productRepo = uow.Repository<Product>();
            var customerRepo = uow.Repository<Customer>();

            // Validate customer exists
            var customer = await customerRepo.GetByIdAsync(request.CustomerId);
            if (customer == null)
            {
                throw new NotFoundException($"Customer {request.CustomerId} not found");
            }

            // Create order
            var order = new Order
            {
                CustomerId = request.CustomerId,
                OrderDate = DateTime.UtcNow,
                Status = OrderStatus.Pending
            };

            decimal total = 0;

            // Process each item
            foreach (var itemRequest in request.Items)
            {
                var product = await productRepo.GetByIdAsync(itemRequest.ProductId);
                if (product == null)
                {
                    throw new NotFoundException($"Product {itemRequest.ProductId} not found");
                }

                if (product.StockQuantity < itemRequest.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Insufficient stock for product {product.Name}. Available: {product.StockQuantity}, Requested: {itemRequest.Quantity}");
                }

                // Create order item
                var orderItem = new OrderItem
                {
                    Order = order,
                    ProductId = itemRequest.ProductId,
                    Quantity = itemRequest.Quantity,
                    UnitPrice = product.Price
                };

                order.Items.Add(orderItem);
                total += orderItem.Quantity * orderItem.UnitPrice;

                // Update inventory
                product.StockQuantity -= itemRequest.Quantity;
                productRepo.Update(product);
            }

            order.Total = total;

            // Validate the complete order
            if (!await orderRepo.ValidateOrderAsync(order))
            {
                throw new InvalidOperationException("Order validation failed");
            }

            orderRepo.Add(order);

            _logger.LogInformation("Created order {OrderId} for customer {CustomerId} with total {Total:C}",
                order.Id, order.CustomerId, order.Total);

            return order;
        }, IsolationLevel.Serializable); // Use higher isolation for inventory management
    }

    // Process multiple orders atomically
    public async Task ProcessOrderBatchAsync(List<int> orderIds, OrderStatus newStatus)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async uow =>
        {
            var orderRepo = uow.GetRepository<IOrderRepository>();

            foreach (var orderId in orderIds)
            {
                var order = await orderRepo.GetByIdAsync(orderId);
                if (order == null)
                {
                    throw new NotFoundException($"Order {orderId} not found");
                }

                order.Status = newStatus;
                orderRepo.Update(order);
            }

            _logger.LogInformation("Processed {Count} orders to status {Status}", 
                orderIds.Count, newStatus);

            return Task.CompletedTask;
        });
    }
}
```

## Bulk Operations

### Mass Data Operations with Progress Tracking

```csharp
public class ProductService
{
    private readonly IEFCoreUnitOfWork _unitOfWork;
    private readonly ILogger<ProductService> _logger;

    public ProductService(IEFCoreUnitOfWork unitOfWork, ILogger<ProductService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    // Bulk import products with progress reporting
    public async Task<ImportResult> BulkImportProductsAsync(
        List<ImportProductRequest> productRequests, 
        IProgress<BulkProgress>? progress = null)
    {
        var result = new ImportResult();

        try
        {
            await _unitOfWork.ExecuteInTransactionAsync(async uow =>
            {
                var productRepo = uow.Repository<Product>();
                var validProducts = new List<Product>();

                // Validate all products first
                progress?.Report(new BulkProgress(0, productRequests.Count, "Validating products"));

                for (int i = 0; i < productRequests.Count; i++)
                {
                    var request = productRequests[i];
                    
                    if (string.IsNullOrWhiteSpace(request.Name) || request.Price <= 0)
                    {
                        result.Errors.Add($"Row {i + 1}: Invalid product data");
                        continue;
                    }

                    // Check for duplicates
                    var existing = await productRepo.FirstOrDefaultAsync(p => p.Name == request.Name);
                    if (existing != null)
                    {
                        result.Errors.Add($"Row {i + 1}: Product '{request.Name}' already exists");
                        continue;
                    }

                    validProducts.Add(new Product
                    {
                        Name = request.Name,
                        Price = request.Price,
                        StockQuantity = request.StockQuantity,
                        IsActive = true
                    });

                    if (i % 100 == 0)
                    {
                        progress?.Report(new BulkProgress(i, productRequests.Count, "Validating products"));
                    }
                }

                // Bulk insert valid products
                if (validProducts.Any())
                {
                    await uow.BulkInsertAsync(validProducts, progress);
                    result.SuccessCount = validProducts.Count;
                }

                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk product import");
            result.Errors.Add($"Import failed: {ex.Message}");
        }

        return result;
    }

    // Bulk update product prices
    public async Task BulkUpdatePricesAsync(Dictionary<int, decimal> priceUpdates)
    {
        await _unitOfWork.ExecuteAsync(async uow =>
        {
            var productRepo = uow.Repository<Product>();
            var productsToUpdate = new List<Product>();

            foreach (var update in priceUpdates)
            {
                var product = await productRepo.GetByIdAsync(update.Key);
                if (product != null)
                {
                    product.Price = update.Value;
                    productsToUpdate.Add(product);
                }
            }

            if (productsToUpdate.Any())
            {
                await uow.BulkUpdateAsync(productsToUpdate);
                _logger.LogInformation("Updated prices for {Count} products", productsToUpdate.Count);
            }
        });
    }

    // Bulk deactivate discontinued products
    public async Task BulkDeactivateProductsAsync(List<int> productIds)
    {
        await _unitOfWork.ExecuteAsync(async uow =>
        {
            var productRepo = uow.Repository<Product>();
            var products = await productRepo.FindAsync(p => productIds.Contains(p.Id));

            foreach (var product in products)
            {
                product.IsActive = false;
            }

            await uow.BulkUpdateAsync(products);
            _logger.LogInformation("Deactivated {Count} products", products.Count);
        });
    }
}

public class ImportResult
{
    public int SuccessCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool HasErrors => Errors.Any();
}

public class ImportProductRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}
```

## Pagination and Filtering

### Advanced Query Operations

```csharp
public class OrderQueryService
{
    private readonly IEFCoreUnitOfWork _unitOfWork;

    public OrderQueryService(IEFCoreUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Advanced order search with multiple filters
    public async Task<PagedResult<OrderDto>> SearchOrdersAsync(OrderSearchRequest request)
    {
        var orderRepo = _unitOfWork.Repository<Order>();

        // Build dynamic filter
        Expression<Func<Order, bool>> filter = o => true;

        if (request.CustomerId.HasValue)
        {
            filter = filter.And(o => o.CustomerId == request.CustomerId.Value);
        }

        if (request.Status.HasValue)
        {
            filter = filter.And(o => o.Status == request.Status.Value);
        }

        if (request.StartDate.HasValue)
        {
            filter = filter.And(o => o.OrderDate >= request.StartDate.Value);
        }

        if (request.EndDate.HasValue)
        {
            filter = filter.And(o => o.OrderDate <= request.EndDate.Value);
        }

        if (request.MinTotal.HasValue)
        {
            filter = filter.And(o => o.Total >= request.MinTotal.Value);
        }

        if (request.MaxTotal.HasValue)
        {
            filter = filter.And(o => o.Total <= request.MaxTotal.Value);
        }

        // Determine sorting
        Expression<Func<Order, object>> orderBy = request.SortBy?.ToLower() switch
        {
            "date" => o => o.OrderDate,
            "total" => o => o.Total,
            "status" => o => o.Status,
            "customer" => o => o.Customer.Name,
            _ => o => o.Id
        };

        var result = await orderRepo.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            filter,
            orderBy,
            request.SortDescending);

        // Convert to DTOs
        var orderDtos = result.Items.Select(o => new OrderDto
        {
            Id = o.Id,
            CustomerName = o.Customer?.Name ?? "Unknown",
            OrderDate = o.OrderDate,
            Total = o.Total,
            Status = o.Status.ToString(),
            ItemCount = o.Items?.Count ?? 0
        }).ToList();

        return new PagedResult<OrderDto>(orderDtos, result.TotalCount, result.PageNumber, result.PageSize);
    }

    // Get customer analytics with pagination
    public async Task<PagedResult<CustomerAnalyticsDto>> GetCustomerAnalyticsAsync(int pageNumber = 1, int pageSize = 20)
    {
        var customerRepo = _unitOfWork.Repository<Customer>();

        // Use raw query for complex analytics
        var query = customerRepo.Query()
            .Select(c => new CustomerAnalyticsDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                TotalOrders = c.Orders.Count(),
                TotalSpent = c.Orders.Sum(o => o.Total),
                LastOrderDate = c.Orders.Max(o => (DateTime?)o.OrderDate),
                AverageOrderValue = c.Orders.Any() ? c.Orders.Average(o => o.Total) : 0
            });

        // Get total count
        var totalCount = await query.CountAsync();

        // Get paged results
        var items = await query
            .OrderByDescending(c => c.TotalSpent)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<CustomerAnalyticsDto>(items, totalCount, pageNumber, pageSize);
    }
}

// Helper extension for combining expressions
public static class ExpressionExtensions
{
    public static Expression<Func<T, bool>> And<T>(
        this Expression<Func<T, bool>> first,
        Expression<Func<T, bool>> second)
    {
        var parameter = Expression.Parameter(typeof(T));
        var leftVisitor = new ReplaceExpressionVisitor(first.Parameters[0], parameter);
        var left = leftVisitor.Visit(first.Body);
        var rightVisitor = new ReplaceExpressionVisitor(second.Parameters[0], parameter);
        var right = rightVisitor.Visit(second.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left, right), parameter);
    }
}

public class ReplaceExpressionVisitor : ExpressionVisitor
{
    private readonly Expression _oldValue;
    private readonly Expression _newValue;

    public ReplaceExpressionVisitor(Expression oldValue, Expression newValue)
    {
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public override Expression Visit(Expression node)
    {
        return node == _oldValue ? _newValue : base.Visit(node);
    }
}

// DTOs and Request Models
public class OrderSearchRequest
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? CustomerId { get; set; }
    public OrderStatus? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? MinTotal { get; set; }
    public decimal? MaxTotal { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
}

public class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}

public class CustomerAnalyticsDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime? LastOrderDate { get; set; }
    public decimal AverageOrderValue { get; set; }
}
```

## Validation

### Entity Validation with Custom Rules

```csharp
public class OrderValidationService
{
    private readonly IEFCoreUnitOfWork _unitOfWork;

    public OrderValidationService(IEFCoreUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Validate order before processing
    public async Task<ValidationResult> ValidateOrderAsync(Order order)
    {
        var errors = new List<ValidationError>();

        // Basic validation using Data Annotations
        var validationResult = await _unitOfWork.ValidateAsync();
        if (!validationResult.IsValid)
        {
            errors.AddRange(validationResult.Errors);
        }

        // Custom business rules
        await ValidateCustomerStatusAsync(order.CustomerId, errors);
        await ValidateProductAvailabilityAsync(order.Items, errors);
        ValidateOrderTotals(order, errors);

        return errors.Any() 
            ? ValidationResult.Failure(errors) 
            : ValidationResult.Success();
    }

    private async Task ValidateCustomerStatusAsync(int customerId, List<ValidationError> errors)
    {
        var customerRepo = _unitOfWork.Repository<Customer>();
        var customer = await customerRepo.GetByIdAsync(customerId);

        if (customer == null)
        {
            errors.Add(new ValidationError("CustomerId", "Customer not found", customerId));
            return;
        }

        // Check if customer has outstanding balance
        var orderRepo = _unitOfWork.GetRepository<IOrderRepository>();
        var totalSpent = await orderRepo.GetCustomerTotalSpentAsync(customerId);
        
        if (totalSpent > 10000) // Credit limit check
        {
            errors.Add(new ValidationError("CustomerId", 
                "Customer has exceeded credit limit", customerId));
        }
    }

    private async Task ValidateProductAvailabilityAsync(
        IEnumerable<OrderItem> items, 
        List<ValidationError> errors)
    {
        var productRepo = _unitOfWork.Repository<Product>();

        foreach (var item in items)
        {
            var product = await productRepo.GetByIdAsync(item.ProductId);
            
            if (product == null)
            {
                errors.Add(new ValidationError($"Items.ProductId", 
                    "Product not found", item.ProductId));
                continue;
            }

            if (!product.IsActive)
            {
                errors.Add(new ValidationError($"Items.ProductId", 
                    "Product is not active", item.ProductId));
            }

            if (product.StockQuantity < item.Quantity)
            {
                errors.Add(new ValidationError($"Items.Quantity", 
                    $"Insufficient stock. Available: {product.StockQuantity}", 
                    item.Quantity));
            }
        }
    }

    private static void ValidateOrderTotals(Order order, List<ValidationError> errors)
    {
        var calculatedTotal = order.Items.Sum(i => i.Quantity * i.UnitPrice);
        
        if (Math.Abs(order.Total - calculatedTotal) > 0.01m)
        {
            errors.Add(new ValidationError("Total", 
                "Order total does not match item totals", order.Total));
        }
    }
}
```

## Performance Optimization

### Optimized Queries and Caching Strategies

```csharp
public class OptimizedOrderService
{
    private readonly IEFCoreUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OptimizedOrderService> _logger;

    public OptimizedOrderService(
        IEFCoreUnitOfWork unitOfWork, 
        IMemoryCache cache, 
        ILogger<OptimizedOrderService> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    // Optimized order retrieval with selective loading
    public async Task<OrderDetailDto> GetOrderDetailsAsync(int orderId)
    {
        var cacheKey = $"order_details_{orderId}";
        
        if (_cache.TryGetValue(cacheKey, out OrderDetailDto? cachedOrder))
        {
            return cachedOrder!;
        }

        var orderRepo = _unitOfWork.Repository<Order>();
        
        // Use projection to load only needed data
        var orderDetail = await orderRepo.Query()
            .Where(o => o.Id == orderId)
            .Select(o => new OrderDetailDto
            {
                Id = o.Id,
                OrderDate = o.OrderDate,
                Status = o.Status.ToString(),
                Total = o.Total,
                Customer = new CustomerSummaryDto
                {
                    Id = o.Customer.Id,
                    Name = o.Customer.Name,
                    Email = o.Customer.Email
                },
                Items = o.Items.Select(i => new OrderItemDto
                {
                    Id = i.Id,
                    ProductName = i.Product.Name,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Subtotal = i.Quantity * i.UnitPrice
                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (orderDetail != null)
        {
            // Cache for 5 minutes
            _cache.Set(cacheKey, orderDetail, TimeSpan.FromMinutes(5));
        }

        return orderDetail ?? throw new NotFoundException($"Order {orderId} not found");
    }

    // Batch processing with optimal chunk sizes
    public async Task ProcessOrdersInBatchesAsync(
        List<int> orderIds, 
        Func<List<Order>, Task> processor)
    {
        const int batchSize = 100; // Optimal batch size for your scenario
        
        var orderRepo = _unitOfWork.Repository<Order>();

        for (int i = 0; i < orderIds.Count; i += batchSize)
        {
            var batchIds = orderIds.Skip(i).Take(batchSize).ToList();
            
            // Load batch with minimal data
            var orders = await orderRepo.Query()
                .Where(o => batchIds.Contains(o.Id))
                .AsNoTracking() // Use for read-only operations
                .ToListAsync();

            await processor(orders);
            
            _logger.LogDebug("Processed batch {BatchNumber} with {Count} orders", 
                (i / batchSize) + 1, orders.Count);
        }
    }

    // Optimized reporting query with raw SQL for complex aggregations
    public async Task<SalesReportDto> GenerateSalesReportAsync(DateTime startDate, DateTime endDate)
    {
        var cacheKey = $"sales_report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}";
        
        if (_cache.TryGetValue(cacheKey, out SalesReportDto? cachedReport))
        {
            return cachedReport!;
        }

        // For complex reporting, sometimes raw SQL is more efficient
        var sql = @"
            SELECT 
                COUNT(*) as TotalOrders,
                SUM(Total) as TotalRevenue,
                AVG(Total) as AverageOrderValue,
                COUNT(DISTINCT CustomerId) as UniqueCustomers
            FROM Orders 
            WHERE OrderDate >= @startDate AND OrderDate <= @endDate";

        var parameters = new[]
        {
            new SqlParameter("@startDate", startDate),
            new SqlParameter("@endDate", endDate)
        };

        // Use raw SQL for complex aggregations
        using var command = _unitOfWork.Context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);

        await _unitOfWork.Context.Database.OpenConnectionAsync();
        using var reader = await command.ExecuteReaderAsync();
        
        var report = new SalesReportDto();
        if (await reader.ReadAsync())
        {
            report.TotalOrders = reader.GetInt32("TotalOrders");
            report.TotalRevenue = reader.GetDecimal("TotalRevenue");
            report.AverageOrderValue = reader.GetDecimal("AverageOrderValue");
            report.UniqueCustomers = reader.GetInt32("UniqueCustomers");
        }

        // Cache for 1 hour
        _cache.Set(cacheKey, report, TimeSpan.FromHours(1));
        
        return report;
    }
}

// DTOs for optimized queries
public class OrderDetailDto
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public CustomerSummaryDto Customer { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
}

public class CustomerSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

public class SalesReportDto
{
    public int TotalOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AverageOrderValue { get; set; }
    public int UniqueCustomers { get; set; }
}
```

## Error Handling

### Comprehensive Error Handling Strategy

```csharp
public class RobustOrderService
{
    private readonly IEFCoreUnitOfWork _unitOfWork;
    private readonly ILogger<RobustOrderService> _logger;

    public RobustOrderService(IEFCoreUnitOfWork unitOfWork, ILogger<RobustOrderService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<Order>> CreateOrderWithErrorHandlingAsync(CreateOrderRequest request)
    {
        try
        {
            // Pre-validation
            if (request == null || !request.Items.Any())
            {
                return Result<Order>.Failure("Invalid order request");
            }

            return await _unitOfWork.ExecuteInTransactionAsync(async uow =>
            {
                var order = await CreateOrderInternalAsync(uow, request);
                return Result<Order>.Success(order);
            });
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation error creating order for customer {CustomerId}", 
                request.CustomerId);
            return Result<Order>.Failure($"Validation failed: {ex.Message}");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict creating order for customer {CustomerId}", 
                request.CustomerId);
            return Result<Order>.Failure("Order creation failed due to concurrent modification. Please retry.");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE constraint") == true)
        {
            _logger.LogWarning(ex, "Duplicate order attempt for customer {CustomerId}", 
                request.CustomerId);
            return Result<Order>.Failure("Duplicate order detected");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Business rule violation creating order for customer {CustomerId}", 
                request.CustomerId);
            return Result<Order>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating order for customer {CustomerId}", 
                request.CustomerId);
            return Result<Order>.Failure("An unexpected error occurred. Please try again later.");
        }
    }

    private async Task<Order> CreateOrderInternalAsync(IEFCoreUnitOfWork uow, CreateOrderRequest request)
    {
        // Implementation details here...
        throw new NotImplementedException();
    }

    // Retry mechanism for transient failures
    public async Task<Result<T>> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation, 
        int maxRetries = 3, 
        TimeSpan delay = default)
    {
        if (delay == default)
            delay = TimeSpan.FromSeconds(1);

        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await operation();
                return Result<T>.Success(result);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                lastException = ex;
                _logger.LogWarning("Attempt {Attempt} failed with concurrency exception", attempt);
                
                if (attempt < maxRetries)
                {
                    await Task.Delay(delay * attempt); // Exponential backoff
                }
            }
            catch (Exception ex) when (IsTransientError(ex))
            {
                lastException = ex;
                _logger.LogWarning("Attempt {Attempt} failed with transient error: {Error}", 
                    attempt, ex.Message);
                
                if (attempt < maxRetries)
                {
                    await Task.Delay(delay * attempt);
                }
            }
            catch (Exception ex)
            {
                // Non-transient error, don't retry
                _logger.LogError(ex, "Non-transient error occurred");
                return Result<T>.Failure(ex.Message);
            }
        }

        _logger.LogError(lastException, "Operation failed after {MaxRetries} attempts", maxRetries);
        return Result<T>.Failure($"Operation failed after {maxRetries} attempts: {lastException?.Message}");
    }

    private static bool IsTransientError(Exception ex)
    {
        // Define what constitutes a transient error
        return ex is TimeoutException ||
               ex is TaskCanceledException ||
               (ex is SqlException sqlEx && IsTransientSqlError(sqlEx.Number));
    }

    private static bool IsTransientSqlError(int errorNumber)
    {
        // Common transient SQL error numbers
        var transientErrorNumbers = new[] { 2, 53, 121, 233, 997, 1205, 1222 };
        return transientErrorNumbers.Contains(errorNumber);
    }
}

// Result pattern for better error handling
public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; private set; }
    public string Error { get; private set; } = string.Empty;

    private Result(bool isSuccess, T? value, string error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, string.Empty);
    public static Result<T> Failure(string error) => new(false, default, error);

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(Value!) : onFailure(Error);
    }
}

// Custom exceptions
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
    public BusinessRuleException(string message, Exception innerException) : base(message, innerException) { }
}
```

## Real-World Service Examples

### E-commerce Order Management System

```csharp
public class ECommerceOrderService
{
    private readonly IEFCoreUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<ECommerceOrderService> _logger;

    public ECommerceOrderService(
        IEFCoreUnitOfWork unitOfWork,
        IEmailService emailService,
        IInventoryService inventoryService,
        IPaymentService paymentService,
        ILogger<ECommerceOrderService> logger)
    {
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _inventoryService = inventoryService;
        _paymentService = paymentService;
        _logger = logger;
    }

    // Complete order processing workflow
    public async Task<Result<OrderProcessingResult>> ProcessOrderAsync(CompleteOrderRequest request)
    {
        var orderResult = await CreateOrderWithValidationAsync(request);
        if (orderResult.IsFailure)
        {
            return Result<OrderProcessingResult>.Failure(orderResult.Error);
        }

        var order = orderResult.Value!;

        try
        {
            // Process payment
            var paymentResult = await _paymentService.ProcessPaymentAsync(
                order.Id, order.Total, request.PaymentDetails);

            if (!paymentResult.IsSuccess)
            {
                await CancelOrderAsync(order.Id);
                return Result<OrderProcessingResult>.Failure($"Payment failed: {paymentResult.ErrorMessage}");
            }

            // Update order status
            await UpdateOrderStatusAsync(order.Id, OrderStatus.Processing);

            // Reserve inventory
            await _inventoryService.ReserveInventoryAsync(order.Items.Select(i => 
                new InventoryReservation(i.ProductId, i.Quantity)).ToList());

            // Send confirmation email
            await _emailService.SendOrderConfirmationAsync(order.CustomerId, order.Id);

            _logger.LogInformation("Successfully processed order {OrderId} for customer {CustomerId}", 
                order.Id, order.CustomerId);

            return Result<OrderProcessingResult>.Success(new OrderProcessingResult
            {
                OrderId = order.Id,
                PaymentTransactionId = paymentResult.TransactionId,
                TotalAmount = order.Total,
                EstimatedDeliveryDate = CalculateDeliveryDate(request.ShippingMethod)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order {OrderId}", order.Id);
            
            // Compensating actions
            await CancelOrderAsync(order.Id);
            
            return Result<OrderProcessingResult>.Failure("Order processing failed");
        }
    }

    private async Task<Result<Order>> CreateOrderWithValidationAsync(CompleteOrderRequest request)
    {
        return await _unitOfWork.ExecuteInTransactionAsync(async uow =>
        {
            var orderRepo = uow.GetRepository<IOrderRepository>();
            var productRepo = uow.Repository<Product>();
            var customerRepo = uow.Repository<Customer>();

            // Comprehensive validation
            var validationErrors = new List<string>();

            // Validate customer
            var customer = await customerRepo.GetByIdAsync(request.CustomerId);
            if (customer == null)
            {
                validationErrors.Add("Customer not found");
            }

            // Validate products and calculate total
            decimal calculatedTotal = 0;
            var orderItems = new List<OrderItem>();

            foreach (var item in request.Items)
            {
                var product = await productRepo.GetByIdAsync(item.ProductId);
                if (product == null)
                {
                    validationErrors.Add($"Product {item.ProductId} not found");
                    continue;
                }

                if (!product.IsActive)
                {
                    validationErrors.Add($"Product {product.Name} is not available");
                    continue;
                }

                if (product.StockQuantity < item.Quantity)
                {
                    validationErrors.Add($"Insufficient stock for {product.Name}. Available: {product.StockQuantity}");
                    continue;
                }

                var orderItem = new OrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = product.Price
                };

                orderItems.Add(orderItem);
                calculatedTotal += orderItem.Quantity * orderItem.UnitPrice;

                // Reserve stock temporarily
                product.StockQuantity -= item.Quantity;
                productRepo.Update(product);
            }

            if (validationErrors.Any())
            {
                return Result<Order>.Failure(string.Join("; ", validationErrors));
            }

            // Create order
            var order = new Order
            {
                CustomerId = request.CustomerId,
                OrderDate = DateTime.UtcNow,
                Total = calculatedTotal,
                Status = OrderStatus.Pending,
                Items = orderItems
            };

            orderRepo.Add(order);

            return Result<Order>.Success(order);
        });
    }

    // Cancel order with proper cleanup
    private async Task CancelOrderAsync(int orderId)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async uow =>
        {
            var orderRepo = uow.GetRepository<IOrderRepository>();
            var productRepo = uow.Repository<Product>();

            var order = await orderRepo.GetOrderWithDetailsAsync(orderId);
            if (order == null) return;

            // Restore inventory
            foreach (var item in order.Items)
            {
                var product = await productRepo.GetByIdAsync(item.ProductId);
                if (product != null)
                {
                    product.StockQuantity += item.Quantity;
                    productRepo.Update(product);
                }
            }

            // Update order status
            order.Status = OrderStatus.Cancelled;
            orderRepo.Update(order);

            _logger.LogInformation("Cancelled order {OrderId} and restored inventory", orderId);
        });
    }

    // Update order status with audit trail
    public async Task UpdateOrderStatusAsync(int orderId, OrderStatus newStatus)
    {
        await _unitOfWork.ExecuteAsync(async uow =>
        {
            var orderRepo = uow.GetRepository<IOrderRepository>();
            var order = await orderRepo.GetByIdAsync(orderId);

            if (order == null)
                throw new NotFoundException($"Order {orderId} not found");

            var oldStatus = order.Status;
            order.Status = newStatus;
            
            orderRepo.Update(order);

            // Log status change
            _logger.LogInformation("Order {OrderId} status changed from {OldStatus} to {NewStatus}", 
                orderId, oldStatus, newStatus);

            // Trigger notifications based on status
            await HandleStatusChangeNotifications(order, oldStatus, newStatus);
        });
    }

    private async Task HandleStatusChangeNotifications(Order order, OrderStatus oldStatus, OrderStatus newStatus)
    {
        switch (newStatus)
        {
            case OrderStatus.Processing:
                await _emailService.SendOrderProcessingNotificationAsync(order.CustomerId, order.Id);
                break;
            case OrderStatus.Shipped:
                await _emailService.SendShippingNotificationAsync(order.CustomerId, order.Id);
                break;
            case OrderStatus.Delivered:
                await _emailService.SendDeliveryConfirmationAsync(order.CustomerId, order.Id);
                break;
            case OrderStatus.Cancelled:
                await _emailService.SendCancellationNotificationAsync(order.CustomerId, order.Id);
                break;
        }
    }

    private DateTime CalculateDeliveryDate(ShippingMethod method)
    {
        var businessDays = method switch
        {
            ShippingMethod.Standard => 5,
            ShippingMethod.Express => 2,
            ShippingMethod.Overnight => 1,
            _ => 5
        };

        var date = DateTime.UtcNow.AddDays(1); // Start from tomorrow
        var addedDays = 0;

        while (addedDays < businessDays)
        {
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                addedDays++;
            }
            date = date.AddDays(1);
        }

        return date;
    }
}

// Supporting models and DTOs
public class CompleteOrderRequest
{
    public int CustomerId { get; set; }
    public List<OrderItemRequest> Items { get; set; } = new();
    public PaymentDetails PaymentDetails { get; set; } = new();
    public ShippingMethod ShippingMethod { get; set; } = ShippingMethod.Standard;
}

public class OrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class PaymentDetails
{
    public string CardNumber { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public string CVV { get; set; } = string.Empty;
    public string CardHolderName { get; set; } = string.Empty;
}

public class OrderProcessingResult
{
    public int OrderId { get; set; }
    public string PaymentTransactionId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime EstimatedDeliveryDate { get; set; }
}

public enum ShippingMethod
{
    Standard,
    Express,
    Overnight
}

// External service interfaces
public interface IEmailService
{
    Task SendOrderConfirmationAsync(int customerId, int orderId);
    Task SendOrderProcessingNotificationAsync(int customerId, int orderId);
    Task SendShippingNotificationAsync(int customerId, int orderId);
    Task SendDeliveryConfirmationAsync(int customerId, int orderId);
    Task SendCancellationNotificationAsync(int customerId, int orderId);
}

public interface IInventoryService
{
    Task ReserveInventoryAsync(List<InventoryReservation> reservations);
    Task ReleaseInventoryAsync(List<InventoryReservation> reservations);
}

public record InventoryReservation(int ProductId, int Quantity);

public interface IPaymentService
{
    Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, PaymentDetails details);
}

public class PaymentResult
{
    public bool IsSuccess { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
```

## Testing Examples

### Unit Testing with Unit of Work

```csharp
public class OrderServiceTests
{
    private readonly Mock<IEFCoreUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IGenericRepository<Order>> _mockOrderRepo;
    private readonly Mock<IGenericRepository<Product>> _mockProductRepo;
    private readonly Mock<IGenericRepository<Customer>> _mockCustomerRepo;
    private readonly Mock<ILogger<OrderService>> _mockLogger;
    private readonly OrderService _orderService;

    public OrderServiceTests()
    {
        _mockUnitOfWork = new Mock<IEFCoreUnitOfWork>();
        _mockOrderRepo = new Mock<IGenericRepository<Order>>();
        _mockProductRepo = new Mock<IGenericRepository<Product>>();
        _mockCustomerRepo = new Mock<IGenericRepository<Customer>>();
        _mockLogger = new Mock<ILogger<OrderService>>();

        _mockUnitOfWork.Setup(x => x.Repository<Order>()).Returns(_mockOrderRepo.Object);
        _mockUnitOfWork.Setup(x => x.Repository<Product>()).Returns(_mockProductRepo.Object);
        _mockUnitOfWork.Setup(x => x.Repository<Customer>()).Returns(_mockCustomerRepo.Object);

        _orderService = new OrderService(_mockUnitOfWork.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CreateOrderAsync_WithValidData_ShouldCreateOrder()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            CustomerId = 1,
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = 1, Quantity = 2 }
            }
        };

        var customer = new Customer { Id = 1, Name = "Test Customer", Email = "test@test.com" };
        var product = new Product { Id = 1, Name = "Test Product", Price = 10.00m, StockQuantity = 10 };

        _mockCustomerRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(customer);
        _mockProductRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(product);

        var capturedOrder = default(Order);
        _mockOrderRepo.Setup(x => x.Add(It.IsAny<Order>()))
            .Callback<Order>(order => capturedOrder = order)
            .Returns((Order o) => o);

        _mockUnitOfWork.Setup(x => x.ExecuteAsync(It.IsAny<Func<IEFCoreUnitOfWork, Task<Order>>>(), true))
            .Returns<Func<IEFCoreUnitOfWork, Task<Order>>, bool>((func, autoSave) => func(_mockUnitOfWork.Object));

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.CustomerId);
        Assert.Equal(20.00m, result.Total);
        Assert.Single(result.Items);
        
        _mockOrderRepo.Verify(x => x.Add(It.IsAny<Order>()), Times.Once);
        _mockProductRepo.Verify(x => x.Update(It.IsAny<Product>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrderAsync_WithInsufficientStock_ShouldThrowException()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            CustomerId = 1,
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = 1, Quantity = 15 } // More than available
            }
        };

        var customer = new Customer { Id = 1, Name = "Test Customer", Email = "test@test.com" };
        var product = new Product { Id = 1, Name = "Test Product", Price = 10.00m, StockQuantity = 10 };

        _mockCustomerRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(customer);
        _mockProductRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(product);

        _mockUnitOfWork.Setup(x => x.ExecuteAsync(It.IsAny<Func<IEFCoreUnitOfWork, Task<Order>>>(), true))
            .Returns<Func<IEFCoreUnitOfWork, Task<Order>>, bool>((func, autoSave) => func(_mockUnitOfWork.Object));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _orderService.CreateOrderAsync(request));
        
        Assert.Contains("Insufficient stock", exception.Message);
    }
}
```

## Best Practices Summary

### 1. **Repository Usage Patterns**
- Use generic repositories for simple CRUD operations
- Create custom repositories for complex domain-specific queries
- Always use `QueryAsNoTracking()` for read-only operations
- Implement projection queries for performance-critical scenarios

### 2. **Transaction Management**
- Use `ExecuteInTransactionAsync` for operations requiring ACID properties
- Choose appropriate isolation levels based on business requirements
- Keep transactions as short as possible
- Always handle transaction rollback in error scenarios

### 3. **Performance Optimization**
- Implement caching for frequently accessed data
- Use pagination for large result sets
- Batch operations when processing multiple entities
- Consider raw SQL for complex reporting queries

### 4. **Error Handling**
- Implement comprehensive validation before saving
- Use Result patterns for better error propagation
- Implement retry mechanisms for transient failures
- Log errors appropriately with correlation IDs

### 5. **Testing Strategy**
- Mock the Unit of Work interface for unit testing
- Test business logic separately from data access
- Use integration tests for complex transaction scenarios
- Verify that proper cleanup occurs in error cases

This comprehensive guide demonstrates how to effectively use the Enhanced EF Core Unit of Work pattern in real-world applications, covering everything from basic CRUD operations to complex business workflows with proper error handling and performance optimization.