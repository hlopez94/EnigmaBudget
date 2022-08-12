namespace EnigmaBudget.Model.Model
{
    public class AppServiceResponse<T>
    {

        public bool Ok { get; set; }
        public string? ErrorText { get; set; }
        public IEnumerable<AppServiceError> Errors { get; set; }
        public T Result { get; set; }

        public AppServiceResponse(string errorText, IEnumerable<AppServiceError>? errors)
        {
            Ok = false;
            Errors = errors;
            ErrorText = errorText;
        }

        public AppServiceResponse(T result)
        {
            Ok = true;
            Result = result;
        }
    }
}
