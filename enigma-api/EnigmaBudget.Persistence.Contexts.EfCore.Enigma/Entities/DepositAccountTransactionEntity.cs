namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities
{
    public partial class DepositAccountTransactionEntity
    {
        public long DatId { get; set; }
        public string DatDescription { get; set; }
        public string DatDetails { get; set; }
        public decimal DatAmmount { get; set; }
        public DateTime DatTransactionDate { get; set; }

        public DateTime DatFechaAlta { get; set; }
        public DateTime? DatFechaBaja { get; set; }
        public DateTime DatFechaModif { get; set; }
        public string DatCurrencyCode { get; set; }

        public long DatDeaId { get; set; }
        public virtual DepositAccountEntity DatDea { get; set; } = null!;

        public long DatUsuId { get; set; }
        public virtual UsuarioEntity DatUsu { get; set; } = null!;
    }
}
