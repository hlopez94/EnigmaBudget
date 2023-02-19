namespace EnigmaBudget.Application.Model
{
    public class WithdrawRequest
    {
        public Guid AccountId { get; set; }
        public decimal Ammount { get; set; }
    }
}