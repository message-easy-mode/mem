namespace Shared.Exceptions;

public static class MemProblemCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string BadRequest = "bad_request";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string AccessDenied = "access_denied";
    public const string RequestTimeout = "request_timeout";
    public const string DockerOperationFailed = "docker_operation_failed";
    public const string DatabaseOperationFailed = "database_operation_failed";
    public const string DependencyRequestFailed = "dependency_request_failed";
    public const string InternalFailure = "internal_failure";
    public const string UnexpectedFailure = "unexpected_failure";
    public const string ClientClosedRequest = "client_closed_request";
}
