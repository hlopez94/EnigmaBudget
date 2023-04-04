using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;

namespace EnigmaBudget.Application
{
    public interface IDepositAccountsService
    {
        public Task<AppResult<DepositAccount>> CreateDepositAccount(CreateDepositAccountRequest request);
        public Task<AppResult<DepositAccount>> EditDepositAccountDetails(string uuid, EditDepositAccountRequest request);
        public Task<AppResult<AccountMovement>> MakeDepositOnAccount(DepositOnAccountRequest request);
        public Task<AppResult<AccountMovement>> MakeWithdrawOnAccount(WithdrawRequest request);
        public Task<AppResult<List<DepositAccount>>> ListUserDepositAccounts();
        public Task<AppResult<DepositAccountDetails>> GetDepositAccountDetails(string uuid);
    }
}
