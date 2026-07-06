namespace WestMarch.Application.Common;

/// <summary>The caller is not permitted to perform the operation. Maps to 403 / an access-denied view.</summary>
public class ForbiddenAccessException(string message = "You do not have permission to perform this action.")
    : Exception(message);

/// <summary>The requested entity does not exist (or is invisible to the caller).</summary>
public class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.");

/// <summary>Business-rule validation failure with user-facing messages.</summary>
public class AppValidationException(params string[] errors)
    : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
