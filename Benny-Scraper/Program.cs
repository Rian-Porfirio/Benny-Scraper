﻿using Benny_Scraper.BusinessLogic.Interfaces;
using Benny_Scraper.DataAccess.DbInitializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

//[assembly: log4net.Config.XmlConfigurator(ConfigFile = "log4net.config", Watch = true)]


namespace Benny_Scraper
{
    internal class Program
    {

        // Added Task to Main in order to avoid "Program does not contain a static 'Main method suitable for an entry point"
        static async Task Main(string[] args)
        {
            // Database Injections https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-usage
            using IHost host = Host.CreateDefaultBuilder(args)
               .ConfigureServices(services =>
                   // Services here
                   new Startup().ConfigureServices(services)
               ).Build();

            IDbInitializer dbInitializer = host.Services.GetRequiredService<IDbInitializer>();
            dbInitializer.Initialize();

            INovelProcessor novelProcessor = host.Services.GetRequiredService<INovelProcessor>();

            // Uri help https://www.dotnetperls.com/uri#:~:text=URI%20stands%20for%20Universal%20Resource,strings%20starting%20with%20%22http.%22
            Uri novelTableOfContentUri = new Uri("https:/s/novelfull.com/paragon-of-sin.html");

            await novelProcessor.ProcessNovelAsync(novelTableOfContentUri);
        }
    }
}