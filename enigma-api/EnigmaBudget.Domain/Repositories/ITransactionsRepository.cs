using EnigmaBudget.Domain.Model;

namespace EnigmaBudget.Domain.Repositories
{
    public interface ITransactionsRepository : IBaseRepository<DepositAccountTransaction, string>
    {
    }
}
