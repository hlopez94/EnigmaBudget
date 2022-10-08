using EnigmaBudget.Infrastructure.Auth;
using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.Auth.Requests;
using EnigmaBudget.Model.Model;
using EnigmaBudget.WebApi.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<UserController> _logger;

        public UserController(ILogger<UserController> logger, IAuthService authSvc)
        {
            _logger = logger;
            _authService = authSvc;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public AppServiceResponse<LoginInfo> Login(LoginRequest request)
        {
            var response = _authService.Login(request);

            return response;
        }

        [HttpPost("signup")]
        [AllowAnonymous]
        public AppServiceResponse<SignUpInfo> SignUp(SignUpRequest request)
        {
            var res = _authService.SignUp(request);
            return res;
        }

        [HttpGet("profile")]
        [Authorize]
        public AppServiceResponse<Perfil> GetProfile()
        {
            return _authService.GetProfile();
        }

        [HttpPost("profile")]
        [Authorize]
        public AppServiceResponse<bool> UpdateProfile([FromBody]Perfil perfil)
        {
            return _authService.UpdateProfile(perfil);
        }
        [HttpPost("change-password")]
        [Authorize]
        public AppServiceResponse<bool> ChangePassword(ChangePasswordRequest request)
        {
            return _authService.ChangePassword(request);

        }

        [HttpGet("countries")]
        public AppServiceResponse<IEnumerable<Pais>> GetCountries()
        {
            return _authService.GetCountries();
        }

        [HttpGet("resend-verification")]
        public AppServiceResponse<IEnumerable<Pais>> ReenviarVerificacion()
        {
            return _authService.GetCountries();
        }


        [HttpPost("verify-email-account")]
        [AllowAnonymous]
        public AppServiceResponse<bool> VerifyEmail([FromBody] string token) {
            return _authService.ValidateEmail(token);
        }
    }
}
