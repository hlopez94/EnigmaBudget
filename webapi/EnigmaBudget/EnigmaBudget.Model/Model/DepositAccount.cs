using EnigmaBudget.Model.Enums;

namespace EnigmaBudget.Model.Model
{
    public class DepositAccount
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public BaseType<DepositAccountTypesEnum> Type { get; set; }
    }
}