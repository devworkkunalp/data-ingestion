using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using STEMwise.DataIngestion.Data;

// Register Shift-JIS encoding for Japan CSV parsing
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // Ingestion DB — orchestratorDB (raw data written by jobs)
        services.AddDbContext<IngestionDbContext>(options =>
            options.UseSqlServer(config["OrchestratorConnection"]));

        // API DB — smtpwiseDB (processed display-ready data)
        services.AddDbContext<ApiDbContext>(options =>
            options.UseSqlServer(config["DefaultConnection"]));

        // Named HttpClients for each external data source
        services.AddHttpClient("ExchangeRate", client =>
        {
            client.BaseAddress = new Uri("https://v6.exchangerate-api.com/v6/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient("BLS", client =>
        {
            client.BaseAddress = new Uri("https://api.bls.gov/publicAPI/v2/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient("CollegeScorecard", client =>
        {
            client.BaseAddress = new Uri("https://api.data.gov/ed/collegescorecard/v1/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient("ONS", client =>
        {
            client.BaseAddress = new Uri("https://api.beta.ons.gov.uk/v1/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient("Nomis", client =>
        {
            client.BaseAddress = new Uri("https://www.nomisweb.co.uk/api/v01/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient("StatCan", client =>
        {
            client.BaseAddress = new Uri("https://www150.statcan.gc.ca/t1/tbl1/en/dtbl/");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddHttpClient("ABS", client =>
        {
            client.BaseAddress = new Uri("https://www.abs.gov.au/");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddHttpClient("QILT", client =>
        {
            client.BaseAddress = new Uri("https://www.qilt.edu.au/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient("MHLW", client =>
        {
            client.BaseAddress = new Uri("https://www.mhlw.go.jp/");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddHttpClient("DOL", client =>
        {
            client.BaseAddress = new Uri("https://www.dol.gov/");
            client.Timeout = TimeSpan.FromSeconds(180);
        });
    })
    .Build();

host.Run();
