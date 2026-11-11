namespace OISM.Application.Common.Interfaces;

public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }
    Task ExecuteWithRetryAsync(Func<Task> operation);
    Task<TResult> ExecuteWithRetryAsync<TResult>(Func<Task<TResult>> operation);
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);
    void ResetTracking();
}

public interface IUnitOfWorkTransaction : IDisposable, IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
