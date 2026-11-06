using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(AppDbContext context) : IUnitOfWork
{
    public bool HasActiveTransaction => context.Database.CurrentTransaction is not null;

    public Task ExecuteWithRetryAsync(Func<Task> operation) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

    public Task<TResult> ExecuteWithRetryAsync<TResult>(Func<Task<TResult>> operation) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!context.Database.IsRelational())
        {
            return NoOpTransaction.Instance;
        }

        return new EfTransaction(await context.Database.BeginTransactionAsync(ct));
    }

    public void ResetTracking() => context.ChangeTracker.Clear();

    private sealed class EfTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct) => transaction.RollbackAsync(ct);
        public void Dispose() => transaction.Dispose();
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class NoOpTransaction : IUnitOfWorkTransaction
    {
        public static readonly NoOpTransaction Instance = new();

        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
