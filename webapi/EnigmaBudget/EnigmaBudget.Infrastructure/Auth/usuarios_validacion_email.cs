namespace EnigmaBudget.Infrastructure.Auth
{
    internal class usuarios_validacion_email
    {
        public long uve_id { get; set; }
        public long uve_usu_id { get; set; }
        public DateTime uve_fecha_alta { get; set; }
        public DateTime uve_fecha_baja { get; set; }
        public string uve_salt { get; set; }
        public bool uve_validado { get; set; }
        public string uve_nuevo_correo { get; set; }


        public bool valida { get { return uve_fecha_alta < DateTime.Now && uve_fecha_baja > DateTime.Now && !uve_validado; } }
    }
}