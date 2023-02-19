namespace EnigmaBudget.Model.Model
{
    public class DepositOnAccountRequest
    {
        public Guid AccountId { get; set; }
        public decimal Ammount { get; set; }
    }
}