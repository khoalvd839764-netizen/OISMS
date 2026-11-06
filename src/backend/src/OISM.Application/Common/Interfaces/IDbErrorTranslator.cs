namespace OISM.Application.Common.Interfaces;

public interface IDbErrorTranslator
{
    bool IsUniqueViolation(Exception exception, string? constraint = null);
    bool IsForeignKeyViolation(Exception exception);
    bool IsCheckViolation(Exception exception, string? constraint = null);
}
