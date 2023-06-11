namespace EnigmaBudget.Infrastructure.SendInBlue.Model
{
    public class SendInBlueOptions
    {
        public string ApiKey { get; set; }
        public Uri ApiUri { get; set; }
        public SendInBlueValidationTemplateConfig ValidationTemplate { get; set; }

    }
}
