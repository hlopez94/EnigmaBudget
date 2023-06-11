using EnigmaBudget.Infrastructure.Auth;
using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.Auth.Requests;
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

        public UserController(IAuthService authSvc)
        {
            _authService = authSvc;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public AuthResult<LoginInfo> Login(LoginRequest request)
        {
            var response = _authService.Login(request);

            return response;
        }

        [HttpPost("signup")]
        [AllowAnonymous]
        public AuthResult<SignUpInfo> SignUp(SignUpRequest request)
        {
            var res = _authService.SignUp(request);
            return res;
        }

        [HttpGet("profile")]
        [Authorize]
        public AuthResult<UserProfile> GetProfile()
        {
            return _authService.GetProfile();
        }

        [HttpPost("profile")]
        [Authorize]
        public AuthResult UpdateProfile([FromBody] UserProfile perfil)
        {
            return _authService.UpdateProfile(perfil);
        }
        [HttpPost("change-password")]
        [Authorize]
        public AuthResult ChangePassword(ChangePasswordRequest request)
        {
            return _authService.ChangePassword(request);

        }

        [HttpGet("resend-verification")]
        public AuthResult<IEnumerable<Pais>> ReenviarVerificacion()
        {
            throw new NotImplementedException();
        }


        [HttpPost("verify-email-account")]
        [AllowAnonymous]
        public AuthResult VerifyEmail([FromBody] string token)
        {
            return _authService.ValidateEmail(token);
        }
    }
}
