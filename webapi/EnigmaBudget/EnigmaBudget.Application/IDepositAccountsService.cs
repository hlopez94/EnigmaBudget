using EnigmaBudget.Application.Model;
using EnigmaBudget.Model.Model;

namespace EnigmaBudget.Application
{
    public interface IDepositAccountsService
    {
        public AppServiceResponse<DepositAccount> CreateDepositAccount(CreateDepositAccountRequest request);
        public AppServiceResponse<DepositAccount> EditDepositAccountDetails(EditDepositAccountRequest request);
        public AppServiceResponse<AccountMovement> MakeDepositOnAccount(DepositOnAccount request);
        public AppServiceResponse<AccountMovement> MakeWithdrawOnAccount(WithdrawRequest request);
        public AppServiceResponse<List<DepositAccount>> ListDepositAccounts();
    }
}
