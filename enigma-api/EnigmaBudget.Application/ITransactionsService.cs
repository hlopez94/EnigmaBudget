using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Infrastructure.Pager;

namespace EnigmaBudget.Application
{
    public interface ITransactionsService
    {
        public Task<AppResult<DepositAccountTransaction>> DepositOnAccount(DepositOnAccountRequest request);
        public Task<AppResult<PagedResponse<DepositAccountTransaction>>> GetUserTransactions(PagedRequest request);
        public Task<AppResult<DepositAccountTransaction>> WithdrawOnAccount(WithdrawRequest request);
    }
}
