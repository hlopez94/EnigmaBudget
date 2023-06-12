namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;

public partial class TypesDepositAccountEntity
{
    public long TdaId { get; set; }

    public string TdaDescription { get; set; } = null!;

    public string TdaName { get; set; } = null!;

    public string TdaEnumName { get; set; } = null!;

    public DateOnly TdaFechaAlta { get; set; }

    public DateOnly TdaFechaModif { get; set; }

    public DateOnly? TdaFechaBaja { get; set; }
    public string TdaIcon { get; set; }

    public virtual ICollection<DepositAccountEntity> DepositAccounts { get; set; } = new List<DepositAccountEntity>();
}
