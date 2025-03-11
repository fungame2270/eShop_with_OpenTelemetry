using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;
using OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("BasketService"))
        .AddAspNetCoreInstrumentation() // Traces incoming HTTP requests
        .AddGrpcClientInstrumentation() // Traces outgoing gRPC requests
        .AddHttpClientInstrumentation() // Traces outgoing HTTP requests
        .AddSource("BasketService")
        .AddOtlpExporter(o =>{
            o.Endpoint = new Uri("http://localhost:4317");
            o.Protocol = OtlpExportProtocol.Grpc;
        })
    ).WithMetrics(metrics =>{
        metrics.AddAspNetCoreInstrumentation().AddMeter("BasketService")
        .AddOtlpExporter(opt =>{
            opt.Endpoint = new Uri("http://localhost:4316");
            opt.Protocol = OtlpExportProtocol.Grpc;
        });
    });


builder.AddBasicServiceDefaults();
builder.AddApplicationServices();

builder.Services.AddGrpc();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<BasketService>();

app.Run();
