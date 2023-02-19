using EnigmaBudget.Application.Model;

namespace EnigmaBudget.Application.Services
{
    internal class DepositAccountsService : IDepositAccountsService
    {
        public AppServiceResponse<DepositAccount> CreateDepositAccount(CreateDepositAccountRequest request)
        {
            throw new NotImplementedException();
        }

        public AppServiceResponse<DepositAccount> EditDepositAccountDetails(EditDepositAccountRequest request)
        {
            throw new NotImplementedException();
        }

        public AppServiceResponse<List<DepositAccount>> ListDepositAccounts()
        {
            throw new NotImplementedException();
        }

        public AppServiceResponse<AccountMovement> MakeDepositOnAccount(DepositOnAccount request)
        {
            throw new NotImplementedException();
        }

        public AppServiceResponse<AccountMovement> MakeWithdrawOnAccount(WithdrawRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
