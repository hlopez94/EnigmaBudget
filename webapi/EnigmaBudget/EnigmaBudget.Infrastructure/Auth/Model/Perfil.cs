using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnigmaBudget.Infrastructure.Auth.Model
{
    public class Usuario
    {
        public Guid Id { get; set; }   
        public string Nombre { get; set; }
        public string Correo { get; set; }

        public short TelefonoCodigoPais { get; set; }
        public short TelefonoCodigoArea { get; set; }
        public int TelefonoNumero { get; set; }
        
        public DateTime FechaRegistro { get; set; }
    }
}
