using System.Collections.ObjectModel;

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;

/// <summary>
/// Tabla con datos de cuentas depÃ³sito
/// </summary>
public partial class DepositAccountEntity
{
    /// <summary>
    /// ID Unico para cuenta depÃ³sito
    /// </summary>
    public long DeaId { get; set; }

    /// <summary>
    /// ID usuario duenio cuenta depÃ³sito
    /// </summary>
    public long DeaUsuId { get; set; }

    /// <summary>
    /// Tipo cuenta depÃ³sito ID
    /// </summary>
    public long DeaTdaId { get; set; }

    /// <summary>
    /// Nombre cuenta deposito
    /// </summary>
    public string DeaName { get; set; } = null!;

    /// <summary>
    /// Nombre descriptivo cuenta depÃ³sito
    /// </summary>
    public string DeaDescription { get; set; } = null!;

    /// <summary>
    /// Fondos actuales
    /// </summary>
    public decimal DeaFunds { get; set; }

    /// <summary>
    /// Codigo numerico ISO-4217 para moneda de cuenta depÃ³sito
    /// </summary>
    public string DeaCountryCode { get; set; } = null!;

    /// <summary>
    /// Codigo numerico ISO-4217 para pais de cuenta depÃ³sito
    /// </summary>
    public string DeaCurrencyCode { get; set; } = null!;

    /// <summary>
    /// Fecha de alta de nta deposito
    /// </summary>
    public DateTime DeaFechaAlta { get; set; }

    /// <summary>
    /// Fecha de modificaciÃ³n de cuenta deposito
    /// </summary>
    public DateTime DeaFechaModif { get; set; }

    /// <summary>
    /// Fecha de baja de cuenta depÃ³sito
    /// </summary>
    public DateTime? DeaFechaBaja { get; set; }

    public virtual TypesDepositAccountEntity DeaTda { get; set; } = null!;

    public virtual UsuarioEntity DeaUsu { get; set; } = null!;
    public virtual Collection<DepositAccountTransactionEntity> DeaTrd { get; set; } = null!;
}
