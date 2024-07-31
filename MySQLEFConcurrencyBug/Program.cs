// See https://aka.ms/new-console-template for more information

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySql.EntityFrameworkCore.Infrastructure.Internal;


public static class Variables
{
    public static string ConnectionString { get; set; }
}
public class ConcurrencyTestsContext : DbContext
{
    private static readonly LoggerFactory LoggerFactory = new LoggerFactory(new[] {
        new Microsoft.Extensions.Logging.Debug.DebugLoggerProvider()
    });
    
    
    public DbSet<Person> People { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>()
            .Property(p => p.SocialSecurityNumber)
            .IsConcurrencyToken();
    }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // local debugging setup
        optionsBuilder
            .UseMySQL(
                Variables.ConnectionString
            )
            .UseLoggerFactory(LoggerFactory)
            .EnableSensitiveDataLogging()
            .EnableDetailedErrors();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new())
    {
        ConcurrencyUpdates();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ConcurrencyUpdates()
    {
        var concurrencyTokens = ChangeTracker.Entries<Person>();

        foreach (var entry in concurrencyTokens.Where(t => t.State != EntityState.Unchanged))
        {
            // set new version for concurrency checks
            entry.Entity.RowVersion = Guid.NewGuid();
        }
    }
}

public class Person
{
    public int PersonId { get; set; }
    
    [ConcurrencyCheck]
    public string? SocialSecurityNumber { get; set; }
    
    public string? PhoneNumber { get; set; }
    
    [ConcurrencyCheck]
    public string? Name { get; set; }
    
    [ConcurrencyCheck]
    public Guid RowVersion { get; set; }
}


public static class Program
{

    public static async Task Main(string[] args)
    {
        Variables.ConnectionString = args.FirstOrDefault() ?? throw new Exception("Please provide a connection string");
        
        await using var originalContext = new ConcurrencyTestsContext();

        SetupContext(originalContext);
        await originalContext.Database.EnsureDeletedAsync();
        await originalContext.Database.EnsureCreatedAsync();
        
        var ogPerson = new Person
        {
            Name = "John Doe",
            PhoneNumber = "555-555-5555",
            SocialSecurityNumber = "123-45-6789",
            RowVersion = Guid.NewGuid()
        };
        originalContext.Add(ogPerson);
        await originalContext.SaveChangesAsync();
        
        await using var context1 = new ConcurrencyTestsContext();
        await using var context2 = new ConcurrencyTestsContext();
        
        var person1 = await context1.People.FindAsync(ogPerson.PersonId) ?? throw new Exception("Person not found");
        var person2 = await context2.People.FindAsync(ogPerson.PersonId) ?? throw new Exception("Person not found");
        
        var originalRowVersion = ogPerson.RowVersion;
        person1.PhoneNumber = "555-555-5556";
        
        await context1.SaveChangesAsync();
        
        person2.RowVersion = originalRowVersion;
        person2.PhoneNumber = "555-555-5557";

        await context2.SaveChangesAsync();
    }

    private static void SetupContext(ConcurrencyTestsContext context)
    {
        // turn off lazy loading
        context.ChangeTracker.LazyLoadingEnabled = false;

        // set auto entity change tracking
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        // set query tracking on all
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

        // set timeout
        context.Database.SetCommandTimeout(120);

        // auto transaction behaviour
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
    }
}