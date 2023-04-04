namespace EnigmaBudget.Domain.Repositories
{
    public interface IBaseRepository<T, KType>
    {
        Task<T> GetById(KType id);
        Task<T> Create(T entity);
        IAsyncEnumerable<T> ListAll();
        Task<bool> Update(T entity);
        Task<bool> Delete(KType id);        
    }
}
