using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnigmaBudget.Persistence.MariaDB.Entities
{
    internal class user_profile
    {
        internal int usp_usu_id { get; set; }
        internal string usp_nombre { get; set; }
        internal DateTime usp_fecha_nacimiento { get; set; }
        internal short usp_tel_cod_pais { get; set; }
        internal short usp_tel_cod_area { get; set; }
        internal int usp_tel_nro { get; set; }
        internal DateTime usp_fecha_alta { get; set; }
        internal DateTime usp_fecha_modif { get; set; }
        internal DateTime? usp_fecha_baja { get; set; }

    }
}
