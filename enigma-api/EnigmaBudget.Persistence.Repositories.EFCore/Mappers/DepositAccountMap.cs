using AutoMapper;
using EnigmaBudget.Domain.Enums;
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
                .ForPath(p => p.OwnerId, opt => opt.MapFrom(q => EncodeDecodeHelper.Encrypt(q.DeaUsuId)))
                .ForPath(p => p.Type.Id, opt => opt.MapFrom(q => q.DeaTdaId))
                .ForMember(p => p.Description, opt => opt.MapFrom(q => q.DeaDescription))
                .ForMember(p => p.Funds, opt => opt.MapFrom(q => q.DeaFunds))
                .ForPath(p => p.Country.Alpha3, opt => opt.MapFrom(q => q.DeaCountryCode))
                .ForPath(p => p.Currency.Num, opt => opt.MapFrom(q => q.DeaCurrencyCode))

                .ForPath(p => p.Type.Id, opt => opt.MapFrom(q => q.DeaTda.TdaId))
                .ForPath(p => p.Type.Description, opt => opt.MapFrom(q => q.DeaTda.TdaDescription))
                .ForPath(p => p.Type.Name, opt => opt.MapFrom(q => q.DeaTda.TdaName))
                .ForPath(p => p.Type.TypeEnum, opt => opt.MapFrom(q => Enum.Parse<DepositAccountTypesEnum>(q.DeaTda.TdaEnumName)));
            ;
        }
    }
}
