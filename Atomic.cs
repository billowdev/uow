using System.Transactions;
using App.Sources.Infra.Logger;
using Microsoft.Extensions.Logging;
using Serilog;
 
namespace App.Sources.Infra.Persistence.Database
{
	// Unified interface that combines both simple and hierarchical atomic operation capabilities
	public interface IAtomicService
	{
		// Original simple atomic operation method
		Task ExecuteAtomicallyAsync(Func<Task> action, CancellationToken cancellationToken = default);

		// New hierarchical atomic operation method
		Task ExecuteChildAtomicallyAsync(Func<Task> action, bool isChildProcess, CancellationToken cancellationToken = default);

		// Context information properties
		bool IsInAtomicOperation { get; }
		bool IsChildAtomicOperation { get; }
	}

	public class AtomicService : IAtomicService
	{
		private static readonly AsyncLocal<AtomicContext> _currentContext = new();

		public bool IsInAtomicOperation => _currentContext.Value?.IsActive ?? false;
		public bool IsChildAtomicOperation => _currentContext.Value?.IsChild ?? false;

		// Original simple atomic operation implementation (unchanged behavior)
		public async Task ExecuteAtomicallyAsync(Func<Task> action, CancellationToken cancellationToken = default)
		{
			using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
			try
			{
				await action(); // Execute the provided action
				scope.Complete(); // Commit the transaction
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Atomic operation failed");
				// TransactionScope will automatically rollback if Complete() is not called
				throw; // Re-throw the exception
			}
		}

		// New hierarchical atomic operation method with parent-child support
		public async Task ExecuteChildAtomicallyAsync(Func<Task> action, bool isChildProcess, CancellationToken cancellationToken = default)
		{
			// Check if we're already in an atomic operation
			var existingContext = _currentContext.Value;
			var isNested = existingContext?.IsActive ?? false;

			// If this is explicitly marked as child process or we're already in an atomic operation,
			// treat as child atomic operation
			var isChild = isChildProcess || isNested;

			if (isChild && isNested)
			{
				// We're in a nested atomic operation - execute without creating new TransactionScope
				AppLogger.Log("Executing as child atomic operation (nested)");
				await ExecuteAsChildAtomicOperation(action);
			}
			else if (isChild && !isNested)
			{
				// This is a child process but no parent atomic operation exists
				// Create a new transaction but mark it as child for tracking
				AppLogger.Log("Executing as child atomic operation (new scope)");
				await ExecuteAsParentAtomicOperation(action, isChild: true);
			}
			else
			{
				// This is a parent atomic operation
				AppLogger.Log("Executing as parent atomic operation");
				await ExecuteAsParentAtomicOperation(action, isChild: false);
			}
		}

		private static async Task ExecuteAsParentAtomicOperation(Func<Task> action, bool isChild)
		{
			using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

			// Set atomic operation context
			var previousContext = _currentContext.Value;
			_currentContext.Value = new AtomicContext(isActive: true, isChild: isChild);

			try
			{
				await action();

				if (!isChild)
				{
					scope.Complete(); // Only commit if this is truly a parent atomic operation
					AppLogger.Log("Parent atomic operation committed successfully");
				}
				else
				{
					AppLogger.Log("Child atomic operation completed (commit deferred to parent)");
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Atomic operation failed: {Message}", ex.Message);
				// TransactionScope will automatically rollback if Complete() is not called
				throw;
			}
			finally
			{
				// Restore previous context
				_currentContext.Value = previousContext!;
			}
		}

		private static async Task ExecuteAsChildAtomicOperation(Func<Task> action)
		{
			// Execute within existing atomic operation context
			// No new TransactionScope needed - will participate in ambient transaction
			try
			{
				await action();
				AppLogger.Log("Child atomic operation completed");
			}
			catch (Exception ex)
			{
				Log.Information(ex, "Child atomic operation failed: {Message}", ex.Message);
				throw; // Let parent handle the rollback
			}
		}
	}

	internal class AtomicContext
	{
		public bool IsActive { get; }
		public bool IsChild { get; }

		public AtomicContext(bool isActive, bool isChild)
		{
			IsActive = isActive;
			IsChild = isChild;
		}
	}

	// Extension methods for easier usage of hierarchical features
	public static class AtomicServiceExtensions
	{
		public static async Task ExecuteAsChildAsync(this IAtomicService atomicService,
			Func<Task> action, CancellationToken cancellationToken = default)
		{
			await atomicService.ExecuteChildAtomicallyAsync(action, isChildProcess: true, cancellationToken);
		}

		public static async Task ExecuteAsParentAsync(this IAtomicService atomicService,
			Func<Task> action, CancellationToken cancellationToken = default)
		{
			await atomicService.ExecuteChildAtomicallyAsync(action, isChildProcess: false, cancellationToken);
		}
	}

	// Example service implementations
	public class AService
	{
		private readonly IAtomicService _atomicService;
		private readonly BService _bService;
		private readonly CService _cService;

		public AService(IAtomicService atomicService, BService bService, CService cService)
		{
			_atomicService = atomicService;
			_bService = bService;
			_cService = cService;
		}

		// AService acts as parent - has permission to commit/rollback
		public async Task ExecuteParentOperationAsync(CancellationToken cancellationToken = default)
		{
			await _atomicService.ExecuteAsParentAsync(async () =>
			{
				AppLogger.Log("AService: Starting parent atomic operation");

				// Perform AService operations
				await PerformAServiceOperations(cancellationToken);

				// Call child services
				await _bService.ExecuteChildOperationAsync(cancellationToken);
				await _cService.ExecuteChildOperationAsync(cancellationToken);

				AppLogger.Log("AService: All operations completed successfully");
			}, cancellationToken);
		}

		// AService can also use simple atomic operation (original behavior)
		public async Task ExecuteSimpleOperationAsync(CancellationToken cancellationToken = default)
		{
			await _atomicService.ExecuteAtomicallyAsync(async () =>
			{
				AppLogger.Log("AService: Executing simple atomic operation");
				await PerformAServiceOperations(cancellationToken);
			}, cancellationToken);
		}

		private static async Task PerformAServiceOperations(CancellationToken cancellationToken)
		{
			// Simulate database operations
			await Task.Delay(100, cancellationToken);
			AppLogger.Log("AService: Database operations completed");
		}
	}

	public class BService
	{
		private readonly IAtomicService _atomicService;

		public BService(IAtomicService atomicService)
		{
			_atomicService = atomicService;
		}

		// BService acts as child - participates in parent atomic operation
		public async Task ExecuteChildOperationAsync(CancellationToken cancellationToken = default)
		{
			await _atomicService.ExecuteAsChildAsync(async () =>
			{
				Log.Information("BService: Executing as child atomic operation. IsChild: {IsChild}", _atomicService.IsChildAtomicOperation);

				// Perform BService operations
				await BService.PerformBServiceOperations(cancellationToken);

				// Child doesn't commit - parent will handle it
			}, cancellationToken);
		}

		// BService can also be called independently using simple atomic operation
		public async Task ExecuteSimpleOperationAsync(CancellationToken cancellationToken = default)
		{
			await _atomicService.ExecuteAtomicallyAsync(async () =>
			{
				Log.Information("BService: Executing simple independent atomic operation");
				await PerformBServiceOperations(cancellationToken);
			}, cancellationToken);
		}

		// BService can also be called independently as parent (using hierarchical method)
		public async Task ExecuteIndependentOperationAsync(CancellationToken cancellationToken = default)
		{
			await _atomicService.ExecuteAsParentAsync(async () =>
			{
				Log.Information("BService: Executing as independent parent atomic operation");
				await PerformBServiceOperations(cancellationToken);
			}, cancellationToken);
		}

		private static async Task PerformBServiceOperations(CancellationToken cancellationToken)
		{
			await Task.Delay(50, cancellationToken);
			Log.Information("BService: Database operations completed");
		}
	}

	public class CService
	{
		private readonly IAtomicService _atomicService;

		public CService(IAtomicService atomicService)
		{
			_atomicService = atomicService;
		}

		public async Task ExecuteChildOperationAsync(CancellationToken cancellationToken = default)
		{
			await _atomicService.ExecuteAsChildAsync(async () =>
			{
				Log.Information("CService: Executing as child atomic operation. IsChild: {IsChild}", _atomicService.IsChildAtomicOperation);
				await PerformCServiceOperations(cancellationToken);
			}, cancellationToken);
		}

		// CService using simple atomic operation
		public async Task ExecuteSimpleOperationAsync(CancellationToken cancellationToken = default)
		{
			await _atomicService.ExecuteAtomicallyAsync(async () =>
			{
				Log.Information("CService: Executing simple atomic operation");
				await PerformCServiceOperations(cancellationToken);
			}, cancellationToken);
		}

		private static async Task PerformCServiceOperations(CancellationToken cancellationToken)
		{
			await Task.Delay(75, cancellationToken);
			Log.Information("CService: Database operations completed");
		}
	}
}

// Usage Examples:
/*
// Dependency Injection Setup
services.AddScoped<IAtomicService, AtomicService>();
services.AddScoped<AService>();
services.AddScoped<BService>();
services.AddScoped<CService>();

// Usage in controller or other service
public class ExampleController : ControllerBase
{
    private readonly AService _aService;
    private readonly BService _bService;
    private readonly IAtomicService _atomicService;

    public ExampleController(AService aService, BService bService, IAtomicService atomicService)
    {
        _aService = aService;
        _bService = bService;
        _atomicService = atomicService;
    }

    [HttpPost("simple-atomic-operation")]
    public async Task<IActionResult> ExecuteSimpleAtomicOperation()
    {
        try
        {
            // Using original simple atomic operation method
            await _atomicService.ExecuteAtomicallyAsync(async () =>
            {
                // Your database operations here - commits automatically
                Log.Information("Simple atomic operation executed");
            });
            return Ok("Simple atomic operation completed successfully");
        }
        catch (Exception ex)
        {
            return BadRequest($"Atomic operation failed: {ex.Message}");
        }
    }

    [HttpPost("parent-child-atomic-operation")]
    public async Task<IActionResult> ExecuteParentChildAtomicOperation()
    {
        try
        {
            // Using hierarchical atomic operation method
            await _atomicService.ExecuteChildAtomicallyAsync(async () =>
            {
                Log.Information("Parent atomic operation started");
                
                // Call child services
                await _bService.ExecuteChildOperationAsync();
                
                Log.Information("Parent atomic operation completing");
            }, isChildProcess: false); // false = parent atomic operation
            
            return Ok("Parent-child atomic operation completed successfully");
        }
        catch (Exception ex)
        {
            return BadRequest($"Atomic operation failed: {ex.Message}");
        }
    }

    [HttpPost("orchestrated-atomic-operation")]
    public async Task<IActionResult> ExecuteOrchestratedAtomicOperation()
    {
        try
        {
            // AService orchestrates the entire atomic operation
            await _aService.ExecuteParentOperationAsync();
            return Ok("Orchestrated atomic operation completed successfully");
        }
        catch (Exception ex)
        {
            return BadRequest($"Atomic operation failed: {ex.Message}");
        }
    }

    [HttpPost("mixed-usage")]
    public async Task<IActionResult> ExecuteMixedUsage()
    {
        try
        {
            // Parent atomic operation using hierarchical method
            await _atomicService.ExecuteAsParentAsync(async () =>
            {
                Log.Information("Parent atomic operation");
                
                // Child can use simple method or hierarchical method
                await _bService.ExecuteSimpleOperationAsync(); // Will participate in parent atomic operation
                await _bService.ExecuteChildOperationAsync(); // Explicit child atomic operation
            });
            
            return Ok("Mixed usage atomic operation completed successfully");
        }
        catch (Exception ex)
        {
            return BadRequest($"Atomic operation failed: {ex.Message}");
        }
    }
}
*/