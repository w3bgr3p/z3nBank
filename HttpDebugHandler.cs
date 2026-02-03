using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;

public class HttpDebugHandler : DelegatingHandler
{
    private readonly string _projectName;
    private readonly string _logHost;
    private static readonly HttpClient _logClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

    public HttpDebugHandler(string projectName, string logHost = "http://localhost:10993/http-log")
    {
        _projectName = projectName;
        _logHost = logHost;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        // Читаем тело запроса (если есть)
        string requestBody = request.Content != null 
            ? await request.Content.ReadAsStringAsync() 
            : null;

        // Выполняем сам запрос
        var response = await base.SendAsync(request, cancellationToken);
        
        var endTime = DateTime.UtcNow;

        // Читаем тело ответа
        // Используем LoadIntoBufferAsync, чтобы основной код тоже мог прочитать поток
        await response.Content.LoadIntoBufferAsync();
        string responseBody = await response.Content.ReadAsStringAsync();

        // Отправляем лог асинхронно (Fire and Forget)
        _ = Task.Run(() => SendDebugLog(request, requestBody, response, responseBody, startTime, endTime));

        return response;
    }

    private async Task SendDebugLog(HttpRequestMessage req, string reqBody, HttpResponseMessage res, string resBody, DateTime start, DateTime end)
    {
        try
        {
            Func<HttpRequestMessage, HttpResponseMessage, object> extractHeaders = (request, response) => {
                var reqH = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>());
                var resH = response.Headers.Concat(response.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>());
            
                return new {
                    request = reqH.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}").ToArray(),
                    response = resH.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}").ToArray()
                };
            };

            var allHeaders = extractHeaders(req, res);
            
            
            var httpLog = new
            {
                timestamp = DateTime.UtcNow.AddHours(-5).ToString("yyyy-MM-dd HH:mm:ss.fff"),
                method = req.Method.ToString(),
                url = req.RequestUri.ToString(),
                statusCode = (int)res.StatusCode,
                durationMs = (int)(end - start).TotalMilliseconds,
                request = new
                {
                    headers = allHeaders.GetType().GetProperty("request").GetValue(allHeaders),
                    body = reqBody,
                    proxy = "Handled by HttpClientHandler" 
                },
                response = new { body = resBody },
                machine = Environment.MachineName,
                project = _projectName
            };

            var content = new StringContent(JsonConvert.SerializeObject(httpLog), Encoding.UTF8, "application/json");
            await _logClient.PostAsync(_logHost, content);
        }
        catch { /* Ошибки логера не должны ломать основное приложение */ }
    }
}