namespace EnigmaBudget.Infrastructure.Auth.Responses
{
    public class ProfileResponse
    {

        public string UserName { get; set; }
        public string Email { get; set; }
        public string Nombre { get; set; }
        public DateTime? FechaNacimiento { get; set; }

        public short TelefonoCodigoPais { get; set; }
        public short TelefonoCodigoArea { get; set; }
        public int TelefonoNumero { get; set; }

        public DateTime FechaAlta { get; set; }
        public DateTime FechaModificacion { get; set; }
    }
}