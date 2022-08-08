namespace EnigmaBudget.WebApi.Model
{
    public class ApiResponse<T>
    {
        public bool Ok { get; set; }
        public T Result { get; set; }

        public ApiResponse(Boolean ok, T result)
        {
            Ok = ok;
            Result = result;
        }
    }
}
