using EnigmaBudget.Domain.Model;

namespace EnigmaBudget.Application.Model
{
    public class CreateDepositAccountRequest
    {
        public string AccountAlias { get; set; }
        public string Description { get; set; }
        public decimal InitialFunds { get; set; }
        public Country Country { get; set; }
        public Currency Currency { get; set; }
        public DepositAccountType Type { get; set; }

    }
}