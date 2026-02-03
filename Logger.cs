using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

using System.Text;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;


namespace z3n
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Off = 99
    }
    
    public class Logger
    {
        private readonly bool _fAcc, _fPort, _fTime, _fMem, _fCaller, _fWrap, _fForce;
        private bool _logShow = false;
        private string _emoji = null;
        private readonly bool _persistent;
        private readonly Stopwatch _stopwatch;
        private int _timezone;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private string _logHost;
        private readonly bool _http;
        public string  _acc;
        
        public Logger( bool log = false, string classEmoji = null, bool persistent = true, LogLevel logLevel = LogLevel.Info, string logHost = null, bool http = true, int timezoneOffset = -5, string acc = "")
        {
            _logShow = log ;
            _emoji = classEmoji;
            _persistent = persistent;
            _stopwatch = persistent ? Stopwatch.StartNew() : null;
            _http = http;
            _logHost =  "http://localhost:5000/api/treasury/log";
            _timezone = timezoneOffset;
            _acc = acc.ToString();
            
            
            _fAcc = false;//cfg.Contains("acc");
            _fPort = false;//cfg.Contains("port");
            _fTime = false;//cfg.Contains("time");
            _fMem = false;//cfg.Contains("memory");
            _fCaller = true;//cfg.Contains("caller");
            _fWrap = true;//cfg.Contains("wrap");
            _fForce = false;//cfg.Contains("force");
        }
        
        public void Send(object toLog,string type = "INFO",
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFilePath = "",
            bool show = false, bool thrw = false, bool toZp = true,
            int cut = 0, bool wrap = true)
        {
     
            if (_fForce) { show = true; toZp = true; }
            
            if (!show && !_logShow) return;
            
            string className = Path.GetFileNameWithoutExtension(callerFilePath);
            string fullCaller = $"{className}.{callerName}";
            
            string header = string.Empty;
            string body = toLog?.ToString() ?? "null";

            if (_fWrap)
            {
                
                
                header = LogHeader(fullCaller); 
                if (cut > 0 && body.Count(c => c == '\n') > cut)
                    body = body.Replace("\r\n", " ").Replace('\n', ' ');
            
                body = $"\n          {(!string.IsNullOrEmpty(_emoji) ? $"[ {_emoji} ] " : "")}{body.Trim()}";
            }
            
            string toSend = header + body;
            
            if (_http)
            {
                string prjName =  "z3nBank";
                string acc =  _acc;
                string port =  "";
                string pid =  "";
                string sessionId =  "";
                SendToHttpLogger(body, type, fullCaller, prjName, acc, port, pid ,sessionId);
            }
            Console.WriteLine(toSend);
        }
        
        private void SendToHttpLogger(string message, string type,  string caller, string prj, string acc, string port,  string pid, string session)
        { _ = Task.Run(async () =>
            {
                try
                {
                    var logData = new
                    {
                        machine = Environment.MachineName,
                        project = prj,
                        timestamp = DateTime.UtcNow.AddHours(_timezone).ToString("yyyy-MM-dd HH:mm:ss"),
                        level = type.ToString().ToUpper(),
                        account = acc,
                        session = session,
                        port = port,
                        pid = pid,
                        caller = caller,
                        extra = new { caller },
                        message = message.Trim(),
                    };

                    string json = JsonConvert.SerializeObject(logData);
            
                    using (var cts = new System.Threading.CancellationTokenSource(1000))
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        await _httpClient.PostAsync(_logHost, content, cts.Token);
                    }
                }
                catch { }
            });
        }      

        private string LogHeader(string callerName)
        {
            var sb = new StringBuilder();
            if (_fCaller) sb.Append($"  🔲 [{callerName}]");
            return sb.ToString();
        }
        private string LogBody(string toLog, int cut)
        {
            if (string.IsNullOrEmpty(toLog)) return string.Empty;
            
            if (cut > 0)
            {
                int lineCount = toLog.Count(c => c == '\n') + 1;
                if (lineCount > cut)
                {
                    toLog = toLog.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
                }
            }
            
            if (!string.IsNullOrEmpty(_emoji))
            {
                toLog = $"[ {_emoji} ] {toLog}";
            }
            return $"\n          {toLog.Trim()}";
        }
        private string GetFullCallerName(string methodName)
        {
            try
            {
                var stackTrace = new StackTrace();
                var frame = stackTrace.GetFrame(2); // Пропускаем Send() и GetFullCallerName()
                var method = frame?.GetMethod();
        
                if (method != null)
                {
                    string className = method.DeclaringType?.Name ?? "Unknown";
                    return $"{className}.{methodName}";
                }
            }
            catch { }
    
            return methodName;
        }

    }
}

