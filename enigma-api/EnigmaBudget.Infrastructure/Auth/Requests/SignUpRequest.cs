namespace EnigmaBudget.Infrastructure.Auth.Requests
{
    public class SignUpRequest
    {
        public string UserName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }
}
