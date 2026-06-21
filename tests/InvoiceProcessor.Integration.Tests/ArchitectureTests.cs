using NetArchTest.Rules;

namespace InvoiceProcessor.Integration.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_ShouldNotReference_ApplicationOrInfrastructure()
    {
        var result = Types
            .InAssembly(typeof(Domain.Error).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("InvoiceProcessor.Application", "InvoiceProcessor.Infrastructure", "InvoiceProcessor.Worker")
            .GetResult();

        Assert.True(result.IsSuccessful, "Domain should not reference Application, Infrastructure or Worker");
    }

    [Fact]
    public void Application_ShouldNotReference_InfrastructureOrWorker()
    {
        var result = Types
            .InAssembly(typeof(Application.Invoices.ProcessInvoiceService).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("InvoiceProcessor.Infrastructure", "InvoiceProcessor.Worker")
            .GetResult();

        Assert.True(result.IsSuccessful, "Application should not reference Infrastructure or Worker");
    }

    [Fact]
    public void Infrastructure_ShouldNotReference_Worker()
    {
        var result = Types
            .InAssembly(typeof(Infrastructure.Files.FileSystemDocumentReader).Assembly)
            .ShouldNot()
            .HaveDependencyOn("InvoiceProcessor.Worker")
            .GetResult();

        Assert.True(result.IsSuccessful, "Infrastructure should not reference Worker");
    }
}
