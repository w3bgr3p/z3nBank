using Npgsql;

namespace z3nSafe;

public static class DatabaseErrors
{
    public static (string Code, string Message) Describe(Exception exception)
    {
        var errors = exception is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { exception }.AsEnumerable();
        foreach (var error in errors)
        {
            for (Exception? current = error; current != null; current = current.InnerException)
            {
                if (current is PostgresException pg)
                    return pg.SqlState switch {
                        "28P01" => ("invalid_credentials", "Неверный пароль или имя пользователя PostgreSQL. Проверьте данные подключения и попробуйте снова."),
                        "28000" => ("authentication_failed", "PostgreSQL отклонил вход. Проверьте пользователя и правила доступа к серверу."),
                        "3D000" => ("database_not_found", "Указанная база PostgreSQL не найдена. Проверьте название базы."),
                        "42501" => ("permission_denied", "У пользователя БД недостаточно прав для работы программы."),
                        _ => ("database_error", "Ошибка PostgreSQL при подключении. Проверьте настройки и доступность базы.")
                    };
                if (current is System.Net.Sockets.SocketException or TimeoutException)
                    return ("server_unavailable", "Не удалось связаться с сервером БД. Проверьте хост, порт и доступность сервера.");
            }
        }
        return ("connection_failed", "Не удалось подключиться к БД. Проверьте настройки подключения и доступ к базе.");
    }
}
