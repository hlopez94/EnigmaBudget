namespace EnigmaBudget.Model.Repositories
{
    internal interface IBaseRepository<T, KType>
    {
        T GetById(KType id);
        IEnumerable<T> ListAll();
        IEnumerable<T> Create(T entity);
        void Update(T entity);
        void Delete(KType id);
    }
}
