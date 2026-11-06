namespace OISM.Domain.Common;

public sealed class InvalidStateTransitionException(string entity, string current, string next)
    : InvalidOperationException($"Không thể chuyển {entity} từ '{current}' sang '{next}'.")
{
}

internal static class StateTransition
{
    public static void Ensure(bool allowed, string entity, string current, string next)
    {
        if (!allowed)
        {
            throw new InvalidStateTransitionException(entity, current, next);
        }
    }
}
