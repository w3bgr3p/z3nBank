using Npgsql;

namespace z3nSafe;

public sealed record BalanceUpdateFailure(int AccountId, string Stage, string Code, string Reason, string Details, bool StopsBatch)
{
    public static BalanceUpdateFailure Describe(int accountId, string stage, Exception exception)
    {
        var pending = new Queue<Exception>(); pending.Enqueue(exception);
        PostgresException? postgres = null; var connectionFailure = false;
        while (pending.TryDequeue(out var current)) {
            if (current is PostgresException pg) postgres ??= pg;
            if (current is NpgsqlException or System.Data.Odbc.OdbcException) connectionFailure = true;
            if (current is AggregateException aggregate) foreach (var inner in aggregate.InnerExceptions) pending.Enqueue(inner);
            else if (current.InnerException != null) pending.Enqueue(current.InnerException);
        }
        var databaseStage = stage is "Read wallet address" or "Read saved balance snapshot" or "Save balance snapshot";
        var recovery = postgres?.SqlState == "57P03";
        var reason = recovery ? "PostgreSQL восстанавливается и временно не принимает подключения. Дождитесь готовности БД и повторите обновление. Причину восстановления смотрите в журнале PostgreSQL." :
            databaseStage && connectionFailure ? "Ошибка БД: обновление остановлено. Проверьте доступность сервера и права подключения." :
            stage == "Fetch chain metadata" ? "Не удалось загрузить список сетей LI.FI. Обновление остановлено; повторите после восстановления сервиса." :
            stage == "Fetch wallet balances" ? "Не удалось получить баланс от LI.FI." :
            stage.StartsWith("Verify RPC balances", StringComparison.Ordinal) ? "Не удалось проверить количество токенов через RPC. Предыдущие балансы сохранены; подробности сети и RPC ниже." : "Не удалось выполнить этап обновления.";
        return new(accountId, stage, postgres?.SqlState ?? exception.GetType().Name, reason,
            postgres?.MessageText ?? SwapExecution.ErrorDetails(exception), databaseStage && connectionFailure || stage == "Fetch chain metadata");
    }
}
