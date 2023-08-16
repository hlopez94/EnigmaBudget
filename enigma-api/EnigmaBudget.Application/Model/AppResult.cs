using System.ComponentModel.DataAnnotations;

namespace EnigmaBudget.Application.Model
{
    public class AppResult<T> : AppResult
    {
        public T Data { get; set; }
        public AppResult() : base() { }
        public AppResult(T obj) : this()
        {
            Data = obj;
        }
    }
    public class AppResult
    {
        public bool IsSuccess { get { return !Errors.Any(); } }
        public List<AppError> Errors { get; private set; }

        public AppResult()
        {
            Errors = new List<AppError>();
        }

        public string ErrorsText { get { return string.Join(". ", Errors.Select(p => p.Message)); } }

        private void AddError(string message, ErrorTypeEnum type)
        {
            Errors.Add(new AppError { Message = message, Type = type });
        }

        public void AddInputDataError(string message)
        {
            AddError(message, ErrorTypeEnum.InputDataError);
        }
        public void AddErrors(List<AppError> errors)
        {
            Errors.AddRange(errors);
        }

        public void AddBusinessError(string message)
        {
            AddError(message, ErrorTypeEnum.BusinessError);
        }

        public void AddInternalError(string message)
        {
            AddError(message, ErrorTypeEnum.InternalError);
        }
        public void AddNotFoundError(string message)
        {
            AddError(message, ErrorTypeEnum.NotFoundError);
        }

        internal void AddInputDataErrors(IEnumerable<ValidationResult> validationResults)
        {
            foreach(var dataError in validationResults)
            {
                AddInputDataError(dataError.ErrorMessage);
            }
        }
    }
}
