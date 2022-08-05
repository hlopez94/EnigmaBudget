namespace EnigmaBudget.Infrastructure.Auth.Model
{
    public class ProfileResponse
    {

        public string UUID { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public DateTime FechaAlta { get; set; }
        public DateTime FechaModificacion { get; set; }

    }
}