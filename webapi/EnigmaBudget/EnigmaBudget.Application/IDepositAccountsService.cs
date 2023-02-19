using EnigmaBudget.Application.Model;

namespace EnigmaBudget.Application
{
    public interface IDepositAccountsService
    {
        public DepositAccount CreateDepositAccount(CreateDepositAccountRequest request);
        public DepositAccount EditDepositAccountDetails(EditDepositAccountRequest request);
        public List<DepositAccount> ListDepositAccounts();
        public AccountMovement MakeDepositOnAccount(DepositOnAccount request);
        public AccountMovement MakeWithdrawOnAccount(WithdrawRequest request);
    }
}
