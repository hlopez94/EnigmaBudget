using EnigmaBudget.Domain.Enums;

namespace EnigmaBudget.Domain.Model
{
    public class AccountMovement
    {
        public BaseType<FundsMovementTypeEnum> Type { get; set; }
        public decimal Ammount { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public Guid Guid { get; set; }
    }
}