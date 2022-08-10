namespace EnigmaBudget.Infrastructure.Auth.Entities
{
    internal class usuario_prefil
    {
        public byte[] usp_usu_id { get; set; }
        public string usp_nombre { get; set; }
        public DateTime? usp_fecha_nacimiento { get; set; }

        public short? usp_tel_cod_pais { get; set; }
        public short? usp_tel_cod_area{ get; set; }
        public long? usp_tel_nro{ get; set; }

        public DateTime usu_fecha_alta { get; set; }
        public DateTime usu_fecha_modif { get; set; }
        public DateTime? usu_fecha_baja { get; set; }
    }
}
