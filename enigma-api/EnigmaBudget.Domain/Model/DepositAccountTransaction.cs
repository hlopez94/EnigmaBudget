using EnigmaBudget.Domain.Enums;

namespace EnigmaBudget.Domain.Model
{
    public class DepositAccountTransaction
    {
        public BaseType<FundsMovementTypeEnum> Type { get; set; }
        public decimal Ammount { get; set; }
        public DateTime Date { get; set; }
        public string Details { get; set; }
        public string Id { get; set; }
    }
}