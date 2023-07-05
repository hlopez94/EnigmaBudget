namespace EnigmaBudget.Domain.Model
{
    public class DepositOnAccountRequest
    {
        public string AccountId { get; set; }
        public decimal Ammount { get; set; }
        public string Description { get; set; }
        public DateTime Date { get; set; }
    }
}