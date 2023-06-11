using System.ComponentModel.DataAnnotations;

namespace EnigmaBudget.Infrastructure.Auth.Model
{
    public class AuthResult<T> : AuthResult
    {
        public T Data { get; set; }
        public AuthResult() : base() { }
        public AuthResult(T obj) : this()
        {
            Data = obj;
        }
    }
    public class AuthResult
    {
        public bool IsSuccess { get { return !Errors.Any(); } }
        public List<AuthError> Errors { get; private set; }

        public AuthResult()
        {
            Errors = new List<AuthError>();
        }

        public string ErrorsText { get { return string.Join(". ", Errors.Select(p => p.Message)); } }

        private void AddError(string message, AuthErrorTypeEnum type)
        {
            Errors.Add(new AuthError { Message = message, Type = type });
        }

        public void AddInputDataError(string message)
        {
            AddError(message, AuthErrorTypeEnum.InputDataError);
        }

        public void AddBusinessError(string message)
        {
            AddError(message, AuthErrorTypeEnum.BusinessError);
        }

        public void AddInternalError(string message)
        {
            AddError(message, AuthErrorTypeEnum.InternalError);
        }
        public void AddNotFoundError(string message)
        {
            AddError(message, AuthErrorTypeEnum.NotFoundError);
        }

        internal void AddInputDataErrors(IEnumerable<ValidationResult> validationResults)
        {
            foreach (var dataError in validationResults)
            {
                AddInputDataError(dataError.ErrorMessage);
            }
        }
    }
}
