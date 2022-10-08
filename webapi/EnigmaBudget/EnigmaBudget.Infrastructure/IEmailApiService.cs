using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnigmaBudget.Infrastructure.SendInBlue.Model;

namespace EnigmaBudget.Infrastructure
{
    public interface IEmailApiService
    {
        void EnviarCorreoValidacionCuenta(EmailValidacionInfo infoTemplate);        
    }
}
