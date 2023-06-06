namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;

public partial class UsuarioPerfilEntity
{
    public long UspUsuId { get; set; }

    public string? UspNombre { get; set; }

    public DateOnly? UspFechaNacimiento { get; set; }

    public short? UspTelCodPais { get; set; }

    public short? UspTelCodArea { get; set; }

    public int? UspTelNro { get; set; }

    public DateTime UspFechaAlta { get; set; }

    public DateTime UspFechaModif { get; set; }

    public DateTime? UspFechaBaja { get; set; }

    public virtual UsuarioEntity UspUsu { get; set; } = null!;
}
