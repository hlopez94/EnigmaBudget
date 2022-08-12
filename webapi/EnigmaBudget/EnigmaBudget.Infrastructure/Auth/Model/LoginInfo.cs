namespace EnigmaBudget.Infrastructure.Auth.Model
{
    public class LoginInfo
    {
        public string? JWT { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public string? Reason { get; set; }
    }
}
