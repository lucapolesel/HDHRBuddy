using HDHRBuddy.Services;

namespace HDHRBuddy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var options = GuideServiceOptions.FromConfiguration(builder.Configuration);

            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<TunerClient>();
            builder.Services.AddSingleton<GuideCache>();
            builder.Services.AddSingleton<GuideRefreshWorker>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<GuideRefreshWorker>());

            var app = builder.Build();

            app.Logger.LogInformation("HDHRBuddy coming to the rescue..");

            // Path for the combined guide
            app.MapGet("/xmltv.xml", (GuideCache cache) =>
            {
                var bytes = cache.Get();
                
                return bytes is not null
                    ? Results.Bytes(bytes, "application/xml")
                    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            });

            app.Run();
        }
    }
}