using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MenuToOrder.Tests.Api;

public class DeployTests
{
    [Fact]
    public void Outside_development_the_app_refuses_to_start_without_a_gemini_key()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseEnvironment("Production")
            .UseSetting("Gemini:ApiKey", ""));
        var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            Assert.ThrowsAny<Exception>(() => app.CreateClient());
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("Gemini:ApiKey is not set", stderr.ToString());
    }
}
