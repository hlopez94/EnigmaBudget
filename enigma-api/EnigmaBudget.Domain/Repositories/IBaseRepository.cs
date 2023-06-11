namespace EnigmaBudget.Domain.Repositories
{
    public interface IBaseRepository<TModel, KType>
    {
        Task<TModel> GetById(KType id);
        Task<TModel> Create(TModel model);
        IAsyncEnumerable<TModel> ListAll();
        Task<bool> Update(TModel model);
        Task<bool> Delete(KType id);
    }
}
