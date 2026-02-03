using Microsoft.Web.WebView2.WinForms;
using Microsoft.Extensions.FileProviders;
using Microsoft.Web.WebView2.Core;

namespace z3nSafe;

public class MainForm : Form
{
    private WebView2 webView;
    private IHost? host;

    public MainForm()
    {
        Text = "z3nBank";
        Size = new Size(1600, 900);
        StartPosition = FormStartPosition.CenterScreen;

        webView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(webView);

        Load += MainForm_Load;
        FormClosing += MainForm_FormClosing;
        FormClosed += MainForm_FormClosed;
    }

    private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        Environment.Exit(0);
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        SetFormIcon();
    
        await StartWebServer();
    
        // Указываем папку для данных WebView2 в AppData пользователя
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "z3nBank",
            "WebView2"
        );
    
        // Создаём папку если её нет
        Directory.CreateDirectory(userDataFolder);
    
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder
        );
    
        await webView.EnsureCoreWebView2Async(environment);
    
        SetFormIcon();
    
        webView.Source = new Uri("http://127.0.0.1:5000");
    }

    private void SetFormIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
            {
                this.Icon = new Icon(iconPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load icon: {ex.Message}");
        }
    }

    private async Task StartWebServer()
    {
        var baseDir = AppContext.BaseDirectory;
        var wwwrootPath = Path.Combine(baseDir, "wwwroot");

        Console.WriteLine($"Base Directory: {baseDir}");
        Console.WriteLine($"WWWRoot Path: {wwwrootPath}");
        Console.WriteLine($"WWWRoot Exists: {Directory.Exists(wwwrootPath)}");
        
        if (Directory.Exists(wwwrootPath))
        {
            Console.WriteLine("Files in wwwroot:");
            foreach (var file in Directory.GetFiles(wwwrootPath))
            {
                Console.WriteLine($"  - {Path.GetFileName(file)}");
            }
        }

        var options = new WebApplicationOptions
        {
            ContentRootPath = baseDir,
            WebRootPath = wwwrootPath,
            Args = Array.Empty<string>()
        };

        var builder = WebApplication.CreateBuilder(options);

        builder.Services.AddControllers();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        builder.Services.AddSingleton<DbConnectionService>();
        builder.Services.AddSingleton<LogService>();

        builder.WebHost.UseUrls("http://127.0.0.1:5000");

        var app = builder.Build();
        
        app.Use(async (context, next) =>
        {
            Console.WriteLine($"Request: {context.Request.Method} {context.Request.Path}");
            await next();
            Console.WriteLine($"Response: {context.Response.StatusCode}");
        });
        
        app.UseCors();
        
        var staticFileOptions = new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(wwwrootPath),
            RequestPath = ""
        };
        app.UseStaticFiles(staticFileOptions);
        
        app.MapControllers();
        
        app.MapFallback(async context =>
        {
            var indexPath = Path.Combine(wwwrootPath, "index.html");
            if (File.Exists(indexPath))
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(indexPath);
            }
            else
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync($"index.html not found at: {indexPath}");
            }
        });

        host = app;
        await host.StartAsync();
        
        Console.WriteLine("Server started on http://127.0.0.1:5000");
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            host?.StopAsync().Wait(TimeSpan.FromSeconds(2));
            host?.Dispose();
            webView?.Dispose();
            Environment.Exit(0);
        }
        catch
        {
            Environment.Exit(0);
        }
    }
}