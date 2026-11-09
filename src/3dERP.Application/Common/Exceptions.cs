namespace ThreeDErp.Application.Common;

/// <summary>
/// 业务校验失败。Errors 为字段级错误集合，API 层映射为 400 ValidationProblem。
/// </summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>
/// 目标资源不存在，API 层映射为 404。
/// </summary>
public sealed class NotFoundException(string message) : Exception(message)
{
}

/// <summary>
/// 业务冲突（编码重复、并发修改等），API 层映射为 409。
/// </summary>
public sealed class ConflictException(string message) : Exception(message)
{
}
