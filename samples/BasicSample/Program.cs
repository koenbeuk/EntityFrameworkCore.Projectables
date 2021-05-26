using EntityFrameworkCore.Projections;
using EntityFrameworkCore.Projections.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace BasicSample
{
    public partial class User
    {
        public int Id { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        [Projectable]
        public string FullName
            => FirstName + " " + LastName;

        [Projectable]
        public string FullNameFunc()
            => FirstName + " " + LastName;
    }

    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
    }


    class Program
    {
        static void Main(string[] args)
        {
            using var dbConnection = new SqliteConnection("Filename=:memory:");
            dbConnection.Open();

            using var serviceProvider = new ServiceCollection()
                .AddDbContext<ApplicationDbContext>(options => {
                    options
                        .UseSqlite(dbConnection)
                        .UseProjections();
                })
                .BuildServiceProvider();

            var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Database.EnsureCreated();

            var user = new User { FirstName = "Jon", LastName = "Doe" };

            dbContext.Users.Add(user);

            dbContext.SaveChanges();

            var query = dbContext.Users
                .Select(x => new {
                     Foo = x.FullNameFunc()
                 });

            Console.WriteLine(query.ToQueryString());

            var r1 = query.ToArray();
        }
    }
}
