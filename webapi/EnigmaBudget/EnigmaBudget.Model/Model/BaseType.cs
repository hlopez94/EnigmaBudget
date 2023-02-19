using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnigmaBudget.Model.Model
{
    public class BaseType<TEnum>
    {
        public string Name { get; set; }
        public string TypeEnum { get; set; }
        public string Description { get; set; }

    }
}
