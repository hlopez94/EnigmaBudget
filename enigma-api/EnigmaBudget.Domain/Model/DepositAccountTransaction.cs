namespace EnigmaBudget.Domain.Model
{
    public class DepositAccountTransaction
    {
        public TransactionType Type { get; set; }
        public decimal Ammount { get; set; }
        public DateTime Date { get; set; }
        public string Details { get; set; }
        public string Id { get; set; }
        public string DepositAccountId { get; set; }
    }
}