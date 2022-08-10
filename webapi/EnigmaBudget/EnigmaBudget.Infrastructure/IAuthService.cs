using EnigmaBudget.Infrastructure.Auth.Requests;
using EnigmaBudget.Infrastructure.Auth.Responses;

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