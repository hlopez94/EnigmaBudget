using System.Text.Json.Serialization;

namespace EnigmaBudget.Infrastructure.SendInBlue.Model
{
    internal class EmailData
    {
        public EmailToData[] to { get; set; }
        public int templateId { get; set; }

        [JsonPropertyName("params")]
        public Dictionary<string, string> Params { get; set; }
    }
}
