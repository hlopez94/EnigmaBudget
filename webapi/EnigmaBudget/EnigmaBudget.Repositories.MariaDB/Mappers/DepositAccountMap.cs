using AutoMapper;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Domain.Enums;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Persistence.MariaDB.Entities;
using System.Data.Common;

namespace EnigmaBudget.Persistence.MariaDB.Mappers
{
    public class DepositAccountMap : Profile
    {
        public DepositAccountMap()
        {
            CreateMap<DbDataReader, deposit_account>()
                .ForMember(p => p.dea_id, opt => opt.MapFrom(q => q["dea_id"]))
                .ForMember(p => p.dea_usu_id, opt => opt.MapFrom(q => q["dea_usu_id"]))
                .ForMember(p => p.dea_tda_id, opt => opt.MapFrom(q => q["dea_tda_id"]))
                .ForMember(p => p.dea_description, opt => opt.MapFrom(q => q["dea_description"]))
                .ForMember(p => p.dea_funds, opt => opt.MapFrom(q => q["dea_funds"]))
                .ForMember(p => p.dea_country_code, opt => opt.MapFrom(q => q["dea_country_code"]))
                .ForMember(p => p.dea_currency_code, opt => opt.MapFrom(q => q["dea_currency_code"]))
                .ForMember(p => p.dea_fecha_alta, opt => opt.MapFrom(q => q["dea_fecha_alta"]))
                .ForMember(p => p.dea_fecha_modif, opt => opt.MapFrom(q => q["dea_fecha_modif"]))
                .ForMember(p => p.dea_fecha_baja, opt =>
                {
                    opt.MapFrom(q => q.IsDBNull(q.GetOrdinal("dea_fecha_baja")) ? default(DateTime?) : q["dea_fecha_baja"]);
                });

            CreateMap<deposit_account, DepositAccount>()
                .ForMember(p => p.Id, opt => opt.MapFrom(q => EncodeDecodeHelper.Encrypt(q.dea_id.ToString())))
                .ForPath(p => p.OwnerId, opt => opt.MapFrom(q => EncodeDecodeHelper.Encrypt(q.dea_usu_id.ToString())))
                .ForPath(p => p.Type.Id, opt => opt.MapFrom(q => q.dea_tda_id))
                .ForMember(p => p.Description, opt => opt.MapFrom(q => q.dea_description))
                .ForMember(p => p.Funds, opt => opt.MapFrom(q => q.dea_funds))
                .ForPath(p => p.Country.Alpha3, opt => opt.MapFrom(q => q.dea_country_code))
                .ForPath(p => p.Currency.Num, opt => opt.MapFrom(q => q.dea_currency_code))

                .ForPath(p => p.Type.Id, opt => opt.MapFrom(q => q.type_deposit_account.tda_id))
                .ForPath(p => p.Type.Description, opt => opt.MapFrom(q => q.type_deposit_account.tda_description))
                .ForPath(p => p.Type.Name, opt => opt.MapFrom(q => q.type_deposit_account.tda_name))
                .ForPath(p => p.Type.TypeEnum, opt => opt.MapFrom(q => Enum.Parse<DepositAccountTypesEnum>(q.type_deposit_account.tda_enum_name)));
                ;
        }
    }
}
