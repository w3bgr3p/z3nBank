using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NetDebugWrapper
{
    public class HttpDebug : IDisposable
    {
        private readonly HttpClient _client;
        private readonly HttpClient _logClient;
        private readonly string _logHost;
        private readonly string _projectName;
        private readonly string _userAgent;

        public HttpDebug(string projectName, string logHost = "http://localhost:10993/http-log", string userAgent = "Mozilla/5.0")
        {
            _projectName = projectName;
            _logHost = logHost;
            _userAgent = userAgent;

            // Основной клиент для запросов
            _client = new HttpClient(new HttpClientHandler 
            { 
                UseCookies = false, // Управляем куками вручную, как в оригинале
                AllowAutoRedirect = true 
            });

            // Клиент для дебаг-логов
            _logClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        }

        public async Task<string> SendAsync(string method, string url, string body = null, string proxy = "", Dictionary<string, string> headers = null, string cookies = null)
        {
            var startTime = DateTime.UtcNow;
            var requestMessage = new HttpRequestMessage(new HttpMethod(method), url);

            // Настройка Body
            if (!string.IsNullOrEmpty(body) && method != "GET")
            {
                requestMessage.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            // Настройка Headers
            requestMessage.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            if (headers != null)
            {
                foreach (var header in headers)
                    requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Настройка Cookies
            if (!string.IsNullOrEmpty(cookies))
            {
                requestMessage.Headers.TryAddWithoutValidation("Cookie", cookies);
            }

            HttpResponseMessage response = null;
            string responseBody = "";
            int statusCode = 0;

            try
            {
                // Примечание: В чистом HttpClient прокси задается в Handler при создании клиента.
                // Если нужно менять прокси на каждый запрос, стоит использовать IHttpClientFactory.
                response = await _client.SendAsync(requestMessage);
                statusCode = (int)response.StatusCode;
                responseBody = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                responseBody = $"Error: {ex.Message}";
                statusCode = 0;
            }
            finally
            {
                var endTime = DateTime.UtcNow;
                
                // Отправляем дебаг-лог (Fire and forget)
                _ = SendDebugLog(method, url, body, proxy, headers, cookies, statusCode, responseBody, startTime, endTime);
            }

            return responseBody;
        }

        private async Task SendDebugLog(string method, string url, string body, string proxy, 
            Dictionary<string, string> headers, string cookies, int statusCode, 
            string responseBody, DateTime start, DateTime end)
        {
            try
            {
                var durationMs = (int)(end - start).TotalMilliseconds;

                var httpLog = new
                {
                    timestamp = DateTime.UtcNow.AddHours(-5).ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    method = method,
                    url = url,
                    statusCode = statusCode,
                    durationMs = durationMs,
                    request = new
                    {
                        headers = headers?.Select(x => $"{x.Key}: {x.Value}").ToArray(),
                        cookies = cookies,
                        cookiesSource = "manual",
                        body = body,
                        proxy = MaskProxyCredentials(proxy)
                    },
                    response = new
                    {
                        body = responseBody
                    },
                    machine = Environment.MachineName,
                    project = _projectName,
                    // Поля ниже оставлены для совместимости структуры лога
                    account = "standalone",
                    session = "standalone",
                    port = "",
                    pid = System.Diagnostics.Process.GetCurrentProcess().Id.ToString()
                };

                string json = JsonConvert.SerializeObject(httpLog);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    await _logClient.PostAsync(_logHost, content);
                }
            }
            catch { /* Игнорируем ошибки логирования */ }
        }

        private string MaskProxyCredentials(string proxy)
        {
            if (string.IsNullOrEmpty(proxy)) return "";
            if (proxy.Contains("@"))
            {
                var parts = proxy.Split('@');
                if (parts.Length == 2) return $"***:***@{parts[1]}";
            }
            return proxy;
        }

        public void Dispose()
        {
            _client?.Dispose();
            _logClient?.Dispose();
        }
    }
}