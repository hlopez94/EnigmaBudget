namespace EnigmaBudget.Infrastructure.Auth.Model
{
    public class ChangePasswordRequest
    {
        public string OldPassword { get; set; }
        public string NewPassword { get; set; }
    }
}