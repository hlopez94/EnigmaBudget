namespace EnigmaBudget.Domain.Model
{
    public class Currency
    {
        public string Code { get; set; }

        public string Num { get; set; }

        public string Name { get; set; }

        public Country Country { get; set; }

        public string Exponent { get; set; }
    }
}

