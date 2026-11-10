using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using TiaPortalMcpServer.Services;
using TiaPortalMcpServer.Extensions;
using Siemens.Collaboration.Net;
using Serilog;
using Serilog.Events;

namespace TiaPortalMcpServer
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                RegisterSiemensAssembly();

                if (Api.Global.Openness().IsUserInGroup())
                {
                    await RunStdioHost(args);
                }
                else
                {
                    Console.Error.WriteLine("Current user does not have permissions to run this application. Please run as a user in the 'TIA Portal Openness Users' group.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error in Main: {ex}");
                throw;
            }
        }

        static void RegisterSiemensAssembly()
        {
            // Must match the major version of the Siemens.Collaboration.Net.TiaPortal.Packages.Openness package in the csproj
            var tiaMajorVersion = 21;
            Api.Global.Openness().Initialize(tiaMajorVersion: tiaMajorVersion);
        }

        static async Task RunStdioHost(string[] args)
        {
            try
            {
                // Read appsettings.json next to the exe: MCP clients start the server in their own working directory
                var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    Args = args,
                    ContentRootPath = AppContext.BaseDirectory
                });

                // Serilog from appsettings.json (stderr), plus a log file next to the exe, so a log survives a TIA Portal
                // crash and does not land in the client's working directory
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(builder.Configuration)
                    .WriteTo.File(
                        System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "tiaportal-mcp.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                // Use Serilog for all logging
                builder.Services.AddSerilog();

                // Register TIA Portal services
                builder.Services.AddSingleton<TiaPortalService>();
                builder.Services.AddSingleton<TiaPortalSessionManager>();
                builder.Services.AddSingleton<BlocksAdapter>();
                builder.Services.AddSingleton<HmiTargetAdapter>();

                // Register file handling services
                builder.Services.AddSingleton<Services.FileAdapter>();

                // Register MCP tool classes into DI so their constructors receive injected services
                builder.Services.AddMcpToolTypesFromAssembly();

                // Configure MCP server and register discovered tools/prompts
                builder.Services
                    .AddMcpServer()
                    .WithStdioServerTransport()
                    .WithToolsFromAssembly()
                    .WithPromptsFromAssembly();

                var host = builder.Build();

                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
