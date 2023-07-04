namespace EnigmaBudget.Domain.Model
{
    public class DepositAccountDetails : DepositAccount
    {
        public IEnumerable<DepositAccountTransaction> Transactions { get; set; }
    }
}