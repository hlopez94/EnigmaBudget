namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;

/// <summary>
/// Tabla con datos de usuarios
/// </summary>
public partial class UsuarioEntity
{
    /// <summary>
    /// ID Unico para el usuario
    /// </summary>
    public long UsuId { get; set; }

    /// <summary>
    /// Nombre de usuario
    /// </summary>
    public string UsuUsuario { get; set; } = null!;

    /// <summary>
    /// Correo asociado al Usuario
    /// </summary>
    public string UsuCorreo { get; set; } = null!;

    /// <summary>
    /// Clave hasheada del usuario
    /// </summary>
    public string? UsuPassword { get; set; }

    /// <summary>
    /// Semilla de hasheo de la clave de usuario
    /// </summary>
    public string? UsuSeed { get; set; }

    /// <summary>
    /// Fecha de alta del usuario
    /// </summary>
    public DateTime UsuFechaAlta { get; set; }

    /// <summary>
    /// Fecha de modificaciÃ³n del usuario
    /// </summary>
    public DateTime UsuFechaModif { get; set; }

    /// <summary>
    /// Fecha de baja del usuario
    /// </summary>
    public DateTime? UsuFechaBaja { get; set; }

    /// <summary>
    /// Indica si el mail brindado por el usuario fue validado
    /// </summary>
    public bool UsuCorreoValidado { get; set; }

    public virtual ICollection<DepositAccountEntity> DepositAccounts { get; set; } = new List<DepositAccountEntity>();

    public virtual UsuarioPerfilEntity? UsuarioPerfil { get; set; }

    public virtual ICollection<UsuariosValidacionEmailEntity> UsuariosValidacionEmails { get; set; } = new List<UsuariosValidacionEmailEntity>();
}
