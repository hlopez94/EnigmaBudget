using EnigmaBudget.Domain.Enums;

namespace EnigmaBudget.Domain.Model
{
    public class DepositAccountType : BaseType<DepositAccountTypesEnum>
    {
        public string IconString { get; set; }
    }
}