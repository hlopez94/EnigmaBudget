using AutoMapper;
using EnigmaBudget.Domain.Enums;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Persistence.Repositories.MariaDB.Entities;
using System.Data.Common;

namespace EnigmaBudget.Persistence.Repositories.MariaDB.Mappers
{
    public class TypeDepositAccountMap : Profile
    {
        public TypeDepositAccountMap()
        {
            CreateMap<DbDataReader, type_deposit_account>()
                .ForMember(p => p.tda_id, opt => opt.MapFrom(q => q["tda_id"]))
                .ForMember(p => p.tda_description, opt => opt.MapFrom(q => q["tda_description"]))
                .ForMember(p => p.tda_name, opt => opt.MapFrom(q => q["tda_name"]))
                .ForMember(p => p.tda_enum_name, opt => opt.MapFrom(q => q["tda_enum_name"]));

            CreateMap<type_deposit_account, BaseType<DepositAccountTypesEnum>>()
                .ForMember(p => p.Id, opt => opt.MapFrom(q => q.tda_id))
                .ForMember(p => p.Description, opt => opt.MapFrom(q => q.tda_description))
                .ForMember(p => p.Name, opt => opt.MapFrom(q => q.tda_name))
                .ForMember(p => p.TypeEnum, opt => opt.MapFrom(q => Enum.Parse<DepositAccountTypesEnum>(q.tda_enum_name)));
        }
    }
}
