namespace EnigmaBudget.Model.Model
{
    public class Perfil
    {
        public string IdUnicoUsuario { get; set; }
        public string Usuario { get; set; }
        public string Correo { get; set; }

        public string? Nombre { get; set; }
        public DateTime? FechaNacimiento { get; set; }

        public short? TelefonoCodigoPais { get; set; }
        public short? TelefonoCodigoArea { get; set; }
        public int TelefonoNumero { get; set; }

    }
}