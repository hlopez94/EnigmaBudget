namespace EnigmaBudget.Infrastructure.Auth.Responses
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
            LoggedIn = false;
        }
    }
}
