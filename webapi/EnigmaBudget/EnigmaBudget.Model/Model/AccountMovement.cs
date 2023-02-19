using EnigmaBudget.Model.Enums;

namespace EnigmaBudget.Model.Model
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