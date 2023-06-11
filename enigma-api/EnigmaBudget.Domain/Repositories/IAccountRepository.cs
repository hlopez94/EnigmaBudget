using EnigmaBudget.Domain.Model;

namespace EnigmaBudget.Domain.Repositories
{
    public interface IDepositAccountRepository : IBaseRepository<DepositAccount, string>
    {
        public IAsyncEnumerable<DepositAccount> ListUserDepositAccounts();
    }
}
