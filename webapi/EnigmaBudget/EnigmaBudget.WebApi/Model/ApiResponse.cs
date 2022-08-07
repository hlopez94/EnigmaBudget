namespace EnigmaBudget.WebApi.Model
{
    public class ApiResponse<T>
    {
        public bool Ok { get; set; }
        public T Result { get; set; }
    }
}
