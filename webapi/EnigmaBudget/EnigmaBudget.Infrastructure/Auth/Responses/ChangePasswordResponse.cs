namespace EnigmaBudget.Infrastructure.Auth.Responses
{
    public class ChangePasswordResponse
    {
        public bool IsPasswordChanged { get; set; }
        public string? Reason { get; set; }
    }
}