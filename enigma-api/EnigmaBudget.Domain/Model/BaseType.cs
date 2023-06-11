namespace EnigmaBudget.Domain.Model
{
    public class BaseType<TEnum>
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public TEnum TypeEnum { get; set; }
        public string Description { get; set; }

    }
}
