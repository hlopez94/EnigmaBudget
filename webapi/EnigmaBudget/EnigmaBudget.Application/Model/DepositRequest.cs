namespace EnigmaBudget.Domain.Model
{
    public class DepositOnAccountRequest
    {
        public string AccountId { get; set; }
        public decimal Ammount { get; set; }
    }
}