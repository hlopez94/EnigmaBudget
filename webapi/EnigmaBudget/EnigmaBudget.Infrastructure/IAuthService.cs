using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.Auth.Requests;
using EnigmaBudget.Model.Model;

namespace EnigmaBudget.Infrastructure.Auth
{
    public interface IAuthService
    {
        AppServiceResponse<LoginInfo> Login(LoginRequest request);
        
        AppServiceResponse<SignUpInfo> SignUp(SignUpRequest signup);

        AppServiceResponse<Perfil> GetProfile();
        AppServiceResponse<bool> UpdateProfile(Perfil perfil);


        AppServiceResponse<IEnumerable<Pais>> GetCountries();
        AppServiceResponse<bool> ChangePassword(ChangePasswordRequest request);
        AppServiceResponse<bool> ValidateEmail(string token);
        AppServiceResponse<bool> ResendValidationEmail();
    }
}