namespace EnigmaBudget.Model.Model
{
    public class DepositAccountDetails : DepositAccount
    {
        public IEnumerable<AccountMovement> FundMovements { get; set; }
    }
}