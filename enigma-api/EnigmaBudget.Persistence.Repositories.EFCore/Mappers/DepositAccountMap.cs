using AutoMapper;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;

namespace EnigmaBudget.Persistence.Repositories.EFCore.Mappers
{
    public class DepositAccountMap : Profile
    {
        public DepositAccountMap()
        {
            CreateMap<DepositAccountEntity, DepositAccount>()
                .ForMember(p => p.Id, opt => opt.MapFrom(q => EncodeDecodeHelper.Encrypt(q.DeaId)))
                .ForMember(p => p.Name, opt => opt.MapFrom(q => q.DeaName))
                .ForPath(p => p.OwnerId, opt => opt.MapFrom(q => EncodeDecodeHelper.Encrypt(q.DeaUsuId)))
                .ForMember(p => p.Description, opt => opt.MapFrom(q => q.DeaDescription))
                .ForMember(p => p.Funds, opt => opt.MapFrom(q => q.DeaFunds))
                .ForPath(p => p.Country.Alpha3, opt => opt.MapFrom(q => q.DeaCountryCode))
                .ForPath(p => p.Currency.Num, opt => opt.MapFrom(q => q.DeaCurrencyCode))
                .AfterMap((src, dest, ctx) =>
                {
                    dest.Type = ctx.Mapper.Map<TypesDepositAccountEntity, DepositAccountType>(src.DeaTda);
                })
            ;
        }
    }
}
