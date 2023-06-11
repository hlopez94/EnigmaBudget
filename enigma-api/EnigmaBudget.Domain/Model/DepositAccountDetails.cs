namespace EnigmaBudget.Domain.Model
{
    public class DepositAccountDetails : DepositAccount
    {
        public IEnumerable<AccountMovement> FundMovements { get; set; }
    }
}