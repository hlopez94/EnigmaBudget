namespace EnigmaBudget.Infrastructure.Auth.Model
{
    public class ChangePasswordResponse
    {
        public bool IsPasswordChanged { get; set; }
        public string? Reason { get; set; }
    }
}