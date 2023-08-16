namespace EnigmaBudget.Infrastructure.SendInBlue.Model
{
    public class SendInBlueOptions
    {
        public bool Enabled { get; set; }
        public string ApiKey { get; set; }
        public Uri ApiUri { get; set; }
        public SendInBlueValidationTemplateConfig ValidationTemplate { get; set; }

    }
}
