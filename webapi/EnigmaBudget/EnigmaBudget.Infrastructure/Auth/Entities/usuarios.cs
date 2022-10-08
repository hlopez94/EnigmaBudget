using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnigmaBudget.Infrastructure.Auth.Entities
{
    internal class usuarios
    {
        public long usu_id { get; set; }
        public string usu_usuario { get; set; }
        public string usu_correo { get; set; }
        public string usu_password { get; set; }
        public string usu_seed { get; set; }
        public DateTime usu_fecha_alta { get; set; }
        public DateTime usu_fecha_modif { get; set; }
        public DateTime? usu_fecha_baja { get; set; }
        public bool usu_correo_verificado { get; set; }
    }
}
