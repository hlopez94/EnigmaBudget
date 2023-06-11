using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace EnigmaBudget.Domain.Model
{
    public class DepositAccount
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public decimal Funds { get; set; }
        public Country Country { get; set; }
        public Currency Currency { get; set; }

        [JsonIgnore]
        [XmlIgnore]
        public string OwnerId { get; set; }
        public DepositAccountType Type { get; set; }
    }
}