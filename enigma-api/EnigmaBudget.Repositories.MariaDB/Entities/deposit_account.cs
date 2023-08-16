namespace EnigmaBudget.Persistence.Repositories.MariaDB.Entities
{
    internal class deposit_account
    {
        internal long dea_id { get; set; }
        internal string dea_description { get; set; }
        internal decimal dea_funds { get; set; }
        internal string dea_country_code { get; set; }
        internal string dea_currency_code { get; set; }

        internal DateTime dea_fecha_alta { get; set; }
        internal DateTime dea_fecha_modif { get; set; }
        internal DateTime? dea_fecha_baja { get; set; }

        internal long dea_usu_id { get; set; }
        internal long dea_tda_id { get; set; }
        internal type_deposit_account type_deposit_account { get; set; }
    }
}