using Npgsql;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Persistence;

internal sealed class PostgresDbErrorTranslator : IDbErrorTranslator
{
    public bool IsUniqueViolation(Exception exception, string? constraint = null) =>
        Matches(exception, PostgresErrorCodes.UniqueViolation, constraint);

    public bool IsForeignKeyViolation(Exception exception) =>
        Matches(exception, PostgresErrorCodes.ForeignKeyViolation);

    public bool IsCheckViolation(Exception exception, string? constraint = null) =>
        Matches(exception, PostgresErrorCodes.CheckViolation, constraint);

    private static bool Matches(Exception exception, string sqlState, string? constraint = null)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is PostgresException postgres)
            {
                return postgres.SqlState == sqlState &&
                    (constraint is null || postgres.ConstraintName == constraint);
            }
        }

        return false;
    }
}
