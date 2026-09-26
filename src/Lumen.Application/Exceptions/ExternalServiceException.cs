namespace Lumen.Application.Exceptions;

public class ExternalServiceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
