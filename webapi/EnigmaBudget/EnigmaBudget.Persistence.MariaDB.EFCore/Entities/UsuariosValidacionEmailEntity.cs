namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;

public partial class UsuariosValidacionEmailEntity
{
    public long UveUsuId { get; set; }

    public int UveId { get; set; }

    public DateTime UveFechaAlta { get; set; }

    public DateTime UveFechaBaja { get; set; }

    public string UveSalt { get; set; } = null!;

    public bool UveValidado { get; set; }

    public string UveNuevoCorreo { get; set; } = null!;

    public virtual UsuarioEntity UveUsu { get; set; } = null!;
}
