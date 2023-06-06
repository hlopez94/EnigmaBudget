using EnigmaBudget.Infrastructure.SendInBlue.Model;

namespace EnigmaBudget.Infrastructure
{
    public interface IEmailApiService
    {
        void EnviarCorreoValidacionCuenta(EmailValidacionInfo infoTemplate);
    }
}
