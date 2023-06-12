using AutoMapper;
using EnigmaBudget.Domain.Enums;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;

namespace EnigmaBudget.Persistence.Repositories.EFCore.Mappers
{
    public class DepositAccountTypeMap : Profile
    {
        public DepositAccountTypeMap()
        {
            CreateMap<TypesDepositAccountEntity, DepositAccountType>()
                .ForMember(p => p.Id, opt => opt.MapFrom(q => EncodeDecodeHelper.Encrypt(q.TdaId)))
                .ForMember(p => p.TypeEnum, opt => opt.MapFrom(q => Enum.Parse<DepositAccountTypesEnum>(q.TdaEnumName)))
                .ForMember(p => p.Name, opt => opt.MapFrom(q => q.TdaName))
                .ForMember(p => p.Description, opt => opt.MapFrom(q => q.TdaDescription))
            ;
        }
    }
}
