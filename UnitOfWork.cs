using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using System.Transactions;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.ComponentModel.DataAnnotations;
 
namespace App.Sources.Infra.Persistence.Database
{
	// Enhanced EF Core specific Unit of Work interface
	public interface IEFCoreUnitOfWork : IAtomicService
	{
		// DbContext access
		DbContext Context { get; }

		// Repository management
		TRepository GetRepository<TRepository>() where TRepository : class;
		IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class;

		// EF Core change tracking integration
		EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
		EntityEntry Entry(object entity);

		// Advanced operations with validation
		Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
		Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
		Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default);

		// Bulk operations with progress reporting
		Task BulkInsertAsync<TEntity>(IEnumerable<TEntity> entities, IProgress<BulkProgress>? progress = null) where TEntity : class;
		Task BulkUpdateAsync<TEntity>(IEnumerable<TEntity> entities, IProgress<BulkProgress>? progress = null) where TEntity : class;
		Task BulkDeleteAsync<TEntity>(IEnumerable<TEntity> entities, IProgress<BulkProgress>? progress = null) where TEntity : class;

		// Change tracking
		bool HasChanges();
		void DetectChanges();
		void AcceptAllChanges();
		void RejectAllChanges();
		IEnumerable<EntityEntry> GetChangedEntries();

		// Transaction control with better isolation
		Task<T> ExecuteInTransactionAsync<T>(Func<IEFCoreUnitOfWork, Task<T>> operation,
			IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

		// Database operations
		Task<bool> CanConnectAsync();
		Task<int> ExecuteSqlRawAsync(string sql, params object[] parameters);
	}

	// Enhanced Generic repository interface
	public interface IGenericRepository<TEntity> where TEntity : class
	{
		// Query operations with better performance
		IQueryable<TEntity> Query();
		IQueryable<TEntity> QueryAsNoTracking();
		Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);
		Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
		Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
		Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

		// Paged queries
		Task<PagedResult<TEntity>> GetPagedAsync(int pageNumber, int pageSize,
			Expression<Func<TEntity, bool>>? predicate = null,
			Expression<Func<TEntity, object>>? orderBy = null,
			bool orderDescending = false,
			CancellationToken cancellationToken = default);

		// Modification operations
		TEntity Add(TEntity entity);
		void AddRange(IEnumerable<TEntity> entities);
		TEntity Update(TEntity entity);
		void UpdateRange(IEnumerable<TEntity> entities);
		void Remove(TEntity entity);
		void RemoveRange(IEnumerable<TEntity> entities);
		Task RemoveByIdAsync(object id, CancellationToken cancellationToken = default);

		// Utility operations
		Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
		Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
		Task<bool> ExistsAsync(object id, CancellationToken cancellationToken = default);
	}

	// Progress reporting for bulk operations
	public record BulkProgress(int ProcessedCount, int TotalCount, string? Message = null);

	// Paged result wrapper
	public record PagedResult<T>(IList<T> Items, int TotalCount, int PageNumber, int PageSize)
	{
		public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
		public bool HasPreviousPage => PageNumber > 1;
		public bool HasNextPage => PageNumber < TotalPages;
	}

	// Validation result
	public record ValidationResult(bool IsValid, IList<ValidationError> Errors)
	{
		public static ValidationResult Success() => new(true, Array.Empty<ValidationError>());
		public static ValidationResult Failure(IList<ValidationError> errors) => new(false, errors);
	}

	public record ValidationError(string PropertyName, string ErrorMessage, object? AttemptedValue = null);

	// Enhanced EF Core Unit of Work implementation
	public class EFCoreUnitOfWork : AtomicService, IEFCoreUnitOfWork, IAsyncDisposable
	{
		private readonly DbContext _context;
		private readonly IServiceProvider _serviceProvider;
		private readonly ILogger<EFCoreUnitOfWork> _logger;
		private readonly Dictionary<Type, object> _repositories = new();
		private bool _disposed = false;

		public EFCoreUnitOfWork(DbContext context, IServiceProvider serviceProvider,
			ILogger<EFCoreUnitOfWork> logger)
		{
			_context = context ?? throw new ArgumentNullException(nameof(context));
			_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public DbContext Context => _context;

		// Repository Management
		public TRepository GetRepository<TRepository>() where TRepository : class
		{
			var type = typeof(TRepository);

			if (!_repositories.TryGetValue(type, out var repository))
			{
				repository = _serviceProvider.GetService(type)
					?? throw new InvalidOperationException($"Repository {type.Name} not registered in DI container");
				_repositories[type] = repository;
			}

			return (TRepository)repository;
		}

		public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class
		{
			var type = typeof(TEntity);

			if (!_repositories.TryGetValue(type, out var repository))
			{
				repository = new GenericRepository<TEntity>(_context);
				_repositories[type] = repository;
			}

			return (IGenericRepository<TEntity>)repository;
		}

		// EF Core Change Tracking Integration
		public EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class
		{
			return _context.Entry(entity);
		}

		public EntityEntry Entry(object entity)
		{
			return _context.Entry(entity);
		}

		public bool HasChanges()
		{
			return _context.ChangeTracker.HasChanges();
		}

		public void DetectChanges()
		{
			_context.ChangeTracker.DetectChanges();
		}

		public void AcceptAllChanges()
		{
			_context.ChangeTracker.AcceptAllChanges();
		}

		public void RejectAllChanges()
		{
			foreach (var entry in _context.ChangeTracker.Entries())
			{
				switch (entry.State)
				{
					case EntityState.Added:
						entry.State = EntityState.Detached;
						break;
					case EntityState.Modified:
					case EntityState.Deleted:
						entry.Reload();
						break;
				}
			}
		}

		public IEnumerable<EntityEntry> GetChangedEntries()
		{
			return _context.ChangeTracker.Entries()
				.Where(e => e.State == EntityState.Added ||
						   e.State == EntityState.Modified ||
						   e.State == EntityState.Deleted);
		}

		// Validation
		public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
		{
			var errors = new List<ValidationError>();

			foreach (var entry in GetChangedEntries())
			{
				var validationContext = new ValidationContext(entry.Entity);
				var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

				if (!Validator.TryValidateObject(entry.Entity, validationContext, validationResults, true))
				{
					errors.AddRange(validationResults.Select(vr =>
						new ValidationError(
							vr.MemberNames.FirstOrDefault() ?? "Unknown",
							vr.ErrorMessage ?? "Validation failed",
							entry.Entity)));
				}
			}

			return errors.Any() ? ValidationResult.Failure(errors) : ValidationResult.Success();
		}

		// Enhanced Save Operations
		public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			return await SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
		}

		public async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
			CancellationToken cancellationToken = default)
		{
			try
			{
				// Validate before saving
				var validationResult = await ValidateAsync(cancellationToken);
				if (!validationResult.IsValid)
				{
					var errorMessage = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
					throw new ValidationException($"Validation failed: {errorMessage}");
				}

				DetectChanges();

				var changedEntities = GetChangedEntries().ToList();
				_logger.LogDebug("Saving {Count} changed entities", changedEntities.Count);

				var result = await _context.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

				_logger.LogInformation("Successfully saved {Count} entities", result);
				return result;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error saving changes");
				throw;
			}
		}

		// Enhanced Bulk Operations with Progress
		public async Task BulkInsertAsync<TEntity>(IEnumerable<TEntity> entities, IProgress<BulkProgress>? progress = null)
			where TEntity : class
		{
			var entityList = entities.ToList();
			var total = entityList.Count;

			progress?.Report(new BulkProgress(0, total, "Starting bulk insert"));

			const int batchSize = 1000;
			var processed = 0;

			for (int i = 0; i < entityList.Count; i += batchSize)
			{
				var batch = entityList.Skip(i).Take(batchSize);
				await _context.Set<TEntity>().AddRangeAsync(batch);

				processed += batch.Count();
				progress?.Report(new BulkProgress(processed, total, $"Processed {processed}/{total} entities"));
			}

			_logger.LogDebug("Bulk insert prepared for {Count} {EntityType} entities", total, typeof(TEntity).Name);
		}

		public async Task BulkUpdateAsync<TEntity>(IEnumerable<TEntity> entities, IProgress<BulkProgress>? progress = null)
			where TEntity : class
		{
			var entityList = entities.ToList();
			var total = entityList.Count;

			progress?.Report(new BulkProgress(0, total, "Starting bulk update"));

			_context.Set<TEntity>().UpdateRange(entityList);
			progress?.Report(new BulkProgress(total, total, "Bulk update completed"));

			_logger.LogDebug("Bulk update prepared for {Count} {EntityType} entities", total, typeof(TEntity).Name);
			await Task.CompletedTask;
		}

		public async Task BulkDeleteAsync<TEntity>(IEnumerable<TEntity> entities, IProgress<BulkProgress>? progress = null)
			where TEntity : class
		{
			var entityList = entities.ToList();
			var total = entityList.Count;

			progress?.Report(new BulkProgress(0, total, "Starting bulk delete"));

			_context.Set<TEntity>().RemoveRange(entityList);
			progress?.Report(new BulkProgress(total, total, "Bulk delete completed"));

			_logger.LogDebug("Bulk delete prepared for {Count} {EntityType} entities", total, typeof(TEntity).Name);
			await Task.CompletedTask;
		}

		// Enhanced Transaction Control
		public async Task<T> ExecuteInTransactionAsync<T>(
			Func<IEFCoreUnitOfWork, Task<T>> operation,
			IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
		{
			if (Transaction.Current != null)
			{
				// Use existing ambient transaction
				return await operation(this);
			}

			// Create EF Core transaction with proper isolation level
			await using var transaction = await _context.Database.BeginTransactionAsync(
				ConvertToEFIsolationLevel(isolationLevel));

			try
			{
				var result = await operation(this);
				await transaction.CommitAsync();
				return result;
			}
			catch
			{
				await transaction.RollbackAsync();
				throw;
			}
		}

		// Database operations
		public async Task<bool> CanConnectAsync()
		{
			try
			{
				return await _context.Database.CanConnectAsync();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Database connection check failed");
				return false;
			}
		}

		public async Task<int> ExecuteSqlRawAsync(string sql, params object[] parameters)
		{
			return await _context.Database.ExecuteSqlRawAsync(sql, parameters);
		}

		// Helper method to convert isolation levels
		private static System.Data.IsolationLevel ConvertToEFIsolationLevel(IsolationLevel isolationLevel)
		{
			return isolationLevel switch
			{
				IsolationLevel.ReadUncommitted => System.Data.IsolationLevel.ReadUncommitted,
				IsolationLevel.ReadCommitted => System.Data.IsolationLevel.ReadCommitted,
				IsolationLevel.RepeatableRead => System.Data.IsolationLevel.RepeatableRead,
				IsolationLevel.Serializable => System.Data.IsolationLevel.Serializable,
				IsolationLevel.Snapshot => System.Data.IsolationLevel.Snapshot,
				_ => System.Data.IsolationLevel.ReadCommitted
			};
		}

		// Enhanced Dispose pattern with async support
		public async ValueTask DisposeAsync()
		{
			if (!_disposed)
			{
				if (_context != null)
				{
					await _context.DisposeAsync();
				}
				_repositories.Clear();
				_disposed = true;
			}
			GC.SuppressFinalize(this);
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				_context?.Dispose();
				_repositories.Clear();
				_disposed = true;
			}
			GC.SuppressFinalize(this);
		}
	}

	// Enhanced Generic Repository Implementation
	public class GenericRepository<TEntity> : IGenericRepository<TEntity> where TEntity : class
	{
		protected readonly DbContext _context;
		protected readonly DbSet<TEntity> _dbSet;

		public GenericRepository(DbContext context)
		{
			_context = context ?? throw new ArgumentNullException(nameof(context));
			_dbSet = context.Set<TEntity>();
		}

		// Query operations
		public virtual IQueryable<TEntity> Query()
		{
			return _dbSet.AsQueryable();
		}

		public virtual IQueryable<TEntity> QueryAsNoTracking()
		{
			return _dbSet.AsNoTracking();
		}

		public virtual async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
		{
			return await _dbSet.FindAsync(new object[] { id }, cancellationToken);
		}

		public virtual async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate,
			CancellationToken cancellationToken = default)
		{
			return await _dbSet.FirstOrDefaultAsync(predicate, cancellationToken);
		}

		public virtual async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
		{
			return await _dbSet.ToListAsync(cancellationToken);
		}

		public virtual async Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate,
			CancellationToken cancellationToken = default)
		{
			return await _dbSet.Where(predicate).ToListAsync(cancellationToken);
		}

		// Paged queries
		public virtual async Task<PagedResult<TEntity>> GetPagedAsync(int pageNumber, int pageSize,
			Expression<Func<TEntity, bool>>? predicate = null,
			Expression<Func<TEntity, object>>? orderBy = null,
			bool orderDescending = false,
			CancellationToken cancellationToken = default)
		{
			var query = _dbSet.AsQueryable();

			if (predicate != null)
				query = query.Where(predicate);

			var totalCount = await query.CountAsync(cancellationToken);

			if (orderBy != null)
			{
				query = orderDescending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
			}

			var items = await query
				.Skip((pageNumber - 1) * pageSize)
				.Take(pageSize)
				.ToListAsync(cancellationToken);

			return new PagedResult<TEntity>(items, totalCount, pageNumber, pageSize);
		}

		// Modification operations
		public virtual TEntity Add(TEntity entity)
		{
			return _dbSet.Add(entity).Entity;
		}

		public virtual void AddRange(IEnumerable<TEntity> entities)
		{
			_dbSet.AddRange(entities);
		}

		public virtual TEntity Update(TEntity entity)
		{
			return _dbSet.Update(entity).Entity;
		}

		public virtual void UpdateRange(IEnumerable<TEntity> entities)
		{
			_dbSet.UpdateRange(entities);
		}

		public virtual void Remove(TEntity entity)
		{
			_dbSet.Remove(entity);
		}

		public virtual void RemoveRange(IEnumerable<TEntity> entities)
		{
			_dbSet.RemoveRange(entities);
		}

		public virtual async Task RemoveByIdAsync(object id, CancellationToken cancellationToken = default)
		{
			var entity = await GetByIdAsync(id, cancellationToken);
			if (entity != null)
			{
				Remove(entity);
			}
		}

		// Utility operations
		public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null,
			CancellationToken cancellationToken = default)
		{
			return predicate == null
				? await _dbSet.CountAsync(cancellationToken)
				: await _dbSet.CountAsync(predicate, cancellationToken);
		}

		public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate,
			CancellationToken cancellationToken = default)
		{
			return await _dbSet.AnyAsync(predicate, cancellationToken);
		}

		public virtual async Task<bool> ExistsAsync(object id, CancellationToken cancellationToken = default)
		{
			var entity = await GetByIdAsync(id, cancellationToken);
			return entity != null;
		}
	}
}