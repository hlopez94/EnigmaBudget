using EnigmaBudget.Infrastructure.Auth;
using EnigmaBudget.Infrastructure.Auth.Model;
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
        public LoginResponse Login(LoginRequest request)
        {
            return _authService.Login(request);
        }

        [HttpPost("signup")]
        [AllowAnonymous]
        public SignUpResponse SignUp(SignUpRequest request)
        {
            return _authService.SignUp(request);
        }

        [HttpGet("profile")]
        [Authorize]
        public ProfileResponse Profile()
        {
            return _authService.GetProfile();
        }

        [HttpPost("change-password")]
        [Authorize]
        public ChangePasswordResponse ChangePassword(ChangePasswordRequest request)
        {
            return _authService.ChangePassword(request);

        }
    }
}
