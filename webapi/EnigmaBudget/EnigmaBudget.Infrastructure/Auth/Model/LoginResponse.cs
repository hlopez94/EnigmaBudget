using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnigmaBudget.Infrastructure.Auth.Model
{
    public class LoginResponse
    {
        public bool LoggedIn { get; set; }
        public string? JWT { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public string? Reason { get; set; }

        public LoginResponse()
        {
            this.LoggedIn = false;
        }
    }
}
