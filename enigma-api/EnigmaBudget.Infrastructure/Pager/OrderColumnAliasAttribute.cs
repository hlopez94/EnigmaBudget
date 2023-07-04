namespace EnigmaBudget.Infrastructure.Pager
{
    [AttributeUsage(AttributeTargets.Property,
                  AllowMultiple = false)]
    public class OrderColumnAliasAttribute : Attribute
    {
        private string ColumnAlias;

        public OrderColumnAliasAttribute(string columnAlias)
        {
            ColumnAlias = columnAlias;
        }

        public virtual string OrderAlias
        {
            get { return ColumnAlias; }
        }
    }
}