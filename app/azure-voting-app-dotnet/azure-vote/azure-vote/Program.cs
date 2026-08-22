var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<VoteService>();

// Add Redis service
var redisConnection = ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("RedisHost"));
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

// Application settings
builder.Services.Configure<VoteAppSettings>(builder.Configuration.GetSection(nameof(VoteAppSettings)));

builder.Services.AddSingleton(new ActivitySource("my-corp.azure-vote.vote-app"));
builder.Services.AddSingleton(new Meter("my-corp.azure-vote.vote-app"));

// Configure OpenTelemetry tracing and metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("vote-app")
        .AddAttributes(new[] { new KeyValuePair<string, object>("service.version", "1.0.0.0") }))
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            // Customize the traces gathered by the HTTP request handler
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                // Add metadata for the request such as the HTTP method and response length
                options.EnrichWithHttpRequest = (activity, httpRequest) =>
                {
                    activity.SetTag("requestProtocol", httpRequest.Protocol);
                    activity.SetTag("requestMethod", httpRequest.Method);
                };
                options.EnrichWithHttpResponse = (activity, httpResponse) =>
                {
                    activity.SetTag("responseLength", httpResponse.ContentLength);
                };
            })
            // Instrument Redis client
            .AddRedisInstrumentation(redisConnection, opt =>
            {
                opt.FlushInterval = TimeSpan.FromSeconds(1);
                opt.SetVerboseDatabaseStatements = true;
            })
            .AddSource("my-corp.azure-vote.vote-app")
            // Ensures that all activities are recorded and sent to exporter
            .SetSampler(new AlwaysOnSampler())
            // Displays traces on the console. Useful for debugging.
            .AddConsoleExporter();
        if (builder.Configuration.GetValue<bool>("EnableOtlpExporter"))
        {
            // Exports spans to an OTLP endpoint. Use this for exporting traces to collector or a backend that support OTLP over HTTP
            tracerProviderBuilder.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new(builder.Configuration.GetConnectionString("OTLPTracesExporterEndpoint"));
                otlpOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }
    })
    .WithMetrics(meterProviderBuilder =>
    {
        meterProviderBuilder.AddAspNetCoreInstrumentation()
            .AddMeter("my-corp.azure-vote.vote-app")
            // Displays metrics on the console. Useful for debugging.
            .AddConsoleExporter();
        if (builder.Configuration.GetValue<bool>("EnableOtlpExporter"))
        {
            // Exports metrics to an OTLP endpoint. Use this for exporting metrics to collector or a backend that support OTLP over HTTP
            meterProviderBuilder.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new(builder.Configuration.GetConnectionString("OTLPMetricsExporterEndpoint"));
                otlpOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

//app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();

app.MapFallbackToPage("/_Host");

app.MapGet("/traced-exception/", (ActivitySource activitySource) =>
    {
        using var activity = activitySource.StartActivity("Error endpoint", ActivityKind.Server);
        throw new ApplicationException("Error processing the request");
    })
    .WithName("TracedException")
    .Produces<ApplicationException>()
    .Produces(StatusCodes.Status500InternalServerError);

app.Run();