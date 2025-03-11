using eShop.WebApp.Components;
using eShop.ServiceDefaults;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("WebService"))
        .AddAspNetCoreInstrumentation() // Traces incoming HTTP requests
        .AddGrpcClientInstrumentation() // Traces outgoing gRPC requests
        .AddHttpClientInstrumentation() // Traces outgoing HTTP requests
        .AddSource("BasketService")
        .AddOtlpExporter(o =>{
            o.Endpoint = new Uri("http://localhost:4317");
            o.Protocol = OtlpExportProtocol.Grpc;
        })
    ).WithMetrics(met =>{
        met.AddAspNetCoreInstrumentation().AddMeter("WebService")
        .AddOtlpExporter(opt =>{
            opt.Endpoint = new Uri("http://localhost:4316");
            opt.Protocol = OtlpExportProtocol.Grpc;
        });
    });

builder.AddServiceDefaults();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.AddApplicationServices();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseAntiforgery();

app.UseHttpsRedirection();

app.UseStaticFiles();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.MapForwarder("/product-images/{id}", "http://catalog-api", "/api/catalog/items/{id}/pic");

app.Run();
