using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.Auth.Requests;

namespace EnigmaBudget.Infrastructure.Auth
{
    public interface IAuthService
    {
        AuthResult<LoginInfo> Login(LoginRequest request);

        AuthResult<SignUpInfo> SignUp(SignUpRequest signup);

        AuthResult<UserProfile> GetProfile();
        AuthResult UpdateProfile(UserProfile perfil);
        AuthResult ChangePassword(ChangePasswordRequest request);
        AuthResult ValidateEmail(string token);
        AuthResult ResendValidationEmail();
    }
}