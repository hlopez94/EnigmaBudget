namespace EnigmaBudget.Infrastructure.SendInBlue.Model
{
    public class SendInBlueOptions
    {
        public string api_key { get; set; }
        public Uri apiUri { get; set; }
        public string templateIdValidacionCorreo { get; set; }

        public SendInBlueOptions(string api_key, string apiUri, string templateIdValidacionCorreo)
        {
            this.api_key = api_key;
            this.apiUri = new Uri(apiUri);
            this.templateIdValidacionCorreo = templateIdValidacionCorreo;
        }
    }
}
