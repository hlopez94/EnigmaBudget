using EnigmaBudget.Application.Model;
using EnigmaBudget.Model.Model;

namespace EnigmaBudget.Application.Services
{
    public class DepositAccountsService : IDepositAccountsService
    {
        public AppServiceResponse<DepositAccount> CreateDepositAccount(CreateDepositAccountRequest request)
        {
            throw new NotImplementedException();
        }

        public AppServiceResponse<DepositAccount> EditDepositAccountDetails(Guid guid, EditDepositAccountRequest request)
        {
            throw new NotImplementedException();
        }

        public AppServiceResponse<DepositAccountDetails> GetDepositAccountDetails(Guid guid)
        {
            throw new NotImplementedException();
        }

        public AppServiceResponse<List<DepositAccount>> ListDepositAccounts()
        {
            throw new NotImplementedException();
        }

        public AppServiceResponse<AccountMovement> MakeDepositOnAccount(DepositOnAccountRequest request)
        {
            throw new NotImplementedException();
        }

        public AppServiceResponse<AccountMovement> MakeWithdrawOnAccount(WithdrawRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
