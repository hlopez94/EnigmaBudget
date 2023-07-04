using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma;

public partial class EnigmaContext : DbContext
{
    public virtual DbSet<DepositAccountEntity> DepositAccounts { get; set; }

    public virtual DbSet<TypesDepositAccountEntity> TypesDepositAccounts { get; set; }

    public virtual DbSet<UsuarioEntity> Usuarios { get; set; }

    public virtual DbSet<UsuarioPerfilEntity> UsuarioPerfiles { get; set; }

    public virtual DbSet<UsuariosValidacionEmailEntity> UsuariosValidacionEmails { get; set; }

    public virtual DbSet<DepositAccountTransactionEntity> Transactions { get; set;}
}
