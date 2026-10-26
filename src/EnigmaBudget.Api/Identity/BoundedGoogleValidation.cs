namespace EnigmaBudget.Api.Identity;

public sealed class BoundedGoogleValidation<T>(int maximumInFlight, TimeSpan deadline) where T : class
{
    private readonly SemaphoreSlim _permits = new(maximumInFlight, maximumInFlight);

    public async Task<T?> ValidateAsync(Func<Task<T>> validation, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _permits.WaitAsync(0, cancellationToken)) return null;
        }
        catch (OperationCanceledException) { return null; }
        Task<T> operation;
        try { operation = validation(); }
        catch
        {
            _permits.Release();
            return null;
        }

        _ = operation.ContinueWith(task =>
        {
            if (task.IsFaulted) _ = task.Exception;
            _permits.Release();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        try { return await operation.WaitAsync(deadline, cancellationToken); }
        catch (OperationCanceledException) { return null; }
        catch (Exception) { return null; }
    }
}
