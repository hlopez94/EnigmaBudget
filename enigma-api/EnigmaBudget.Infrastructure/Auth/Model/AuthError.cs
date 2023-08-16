namespace EnigmaBudget.Infrastructure.Auth.Model
{
    public class AuthError
    {
        public string Message { get; set; }

        public AuthErrorTypeEnum Type { get; set; }
    }
}
