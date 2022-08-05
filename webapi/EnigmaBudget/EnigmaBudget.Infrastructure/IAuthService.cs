using EnigmaBudget.Infrastructure.Auth.Model;
using System.Security.Claims;
using System.Security.Principal;

namespace EnigmaBudget.Infrastructure.Auth
{
    public interface IAuthService
    {
        LoginResponse Login(LoginRequest request);
        
        SignUpResponse SignUp(SignUpRequest signup);

        ProfileResponse GetProfile();

        ChangePasswordResponse ChangePassword(ChangePasswordRequest request);
    }
}