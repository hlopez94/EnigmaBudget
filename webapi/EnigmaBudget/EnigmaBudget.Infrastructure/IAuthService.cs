using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.Auth.Requests;
using EnigmaBudget.Infrastructure.Auth.Responses;
using EnigmaBudget.Model.Model;

namespace EnigmaBudget.Infrastructure.Auth
{
    public interface IAuthService
    {
        AppServiceResponse<LoginInfo> Login(LoginRequest request);
        
        AppServiceResponse<SignUpInfo> SignUp(SignUpRequest signup);

        AppServiceResponse<Perfil> GetProfile();

        AppServiceResponse<bool> ChangePassword(ChangePasswordRequest request);
    }
}