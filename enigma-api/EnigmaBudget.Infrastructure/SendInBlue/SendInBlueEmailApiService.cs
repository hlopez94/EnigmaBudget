using EnigmaBudget.Infrastructure.SendInBlue.Model;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EnigmaBudget.Infrastructure.SendInBlue
{
    public class SendInBlueEmailApiService : IEmailApiService
    {
        SendInBlueOptions _options;
        HttpClient _httpClient;

        public SendInBlueEmailApiService(SendInBlueOptions options)
        {
            _options = options;
            _httpClient = new HttpClient();
        }

        public void EnviarCorreoValidacionCuenta(EmailValidacionInfo infoTemplate)
        {
            Dictionary<string, string> pars = new Dictionary<string, string>
            {
                { "user_name", infoTemplate.UsuarioNombre },
                { "activation_url", string.Concat(infoTemplate.UrlApp, "/#/validate-mail?token=", infoTemplate.Token) },
                { "app_url", infoTemplate.UrlApp }
            };

            List<EmailToData> emailTo = new List<EmailToData>
            {
                new EmailToData() { email = infoTemplate.Correo, name = infoTemplate.UsuarioNombre }
            };

            EmailData emailData = new EmailData()
            {
                Params = pars,
                templateId = _options.ValidationTemplate.Id,
                to = emailTo.ToArray()
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiUri))
            {
                request.Headers.Add("accept", "application/json");
                request.Headers.Add("api-key", _options.ApiKey);

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(emailData, serializerOptions);

                HttpContent httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                request.Content = httpContent;

                var response = _httpClient.Send(request);

                response.EnsureSuccessStatusCode();
            }
        }
    }
}
