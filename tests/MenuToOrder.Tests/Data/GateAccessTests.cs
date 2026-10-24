using System.Reflection;
using System.Runtime.CompilerServices;
using MenuToOrder.Data;

namespace MenuToOrder.Tests.Data;

public class GateAccessTests
{
    [Fact]
    public void Publishing_is_not_part_of_the_public_surface() =>
        Assert.Null(typeof(MenuVersion).GetMethod(nameof(MenuVersion.Publish)));

    [Fact]
    public void Only_verification_and_tests_can_see_data_internals()
    {
        var friends = typeof(MenuVersion).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName)
            .Order();

        Assert.Equal(new[] { "MenuToOrder.Tests", "MenuToOrder.Verification" }, friends);
    }
}
