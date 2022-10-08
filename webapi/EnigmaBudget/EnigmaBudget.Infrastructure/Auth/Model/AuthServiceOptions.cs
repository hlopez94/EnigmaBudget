namespace EnigmaBudget.Infrastructure.Auth.Model
{
    public class AuthServiceOptions
    {
        public string Issuer { get; set; }
        public string Audience { get; set; }
        public string Subject { get; set; }
        public string Key { get; set; }
        public string UiUrl { get; set; }

        public AuthServiceOptions(string issuer, string audience, string subject, string key, string uiUrl)
        {
            Issuer = issuer;
            Audience = audience;
            Subject = subject;
            Key = key;
            UiUrl = uiUrl;
        }
    }
}
