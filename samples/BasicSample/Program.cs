using EntityFrameworkCore.Projections;
using EntityFrameworkCore.Projections.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BasicSample
{
    public class User
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }

        public ICollection<Order> Orders { get; set; }

        [Projectable]
        public string FullName
            => $"{FirstName} {LastName}";

        [Projectable]
        public double TotalSpent => Orders.Sum(x => x.PriceSum);
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Price { get; set; }
    }

    public class Order
    {
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public ICollection<OrderItem> Items { get; set; }

        [Projectable]
        public double PriceSum => Items.Sum(x => x.TotalPrice);
    }

    public class OrderItem
    {
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public double UnitPrice { get; set; }

        public Order Order { get; set; }
        public Product Product { get; set; }

        [Projectable]
        public double TotalPrice => Quantity * UnitPrice;
    }

    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderItem>().HasKey(x => new { x.OrderId, x.ProductId });
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

            var product1 = new Product { Name = "Red pen", Price = 1.5 };
            var product2 = new Product { Name = "Blue pen", Price = 2.1 };

            var user = new User { 
                FirstName = "Jon", 
                LastName = "Doe", 
                Orders = new List<Order> {
                    new Order {
                        Items = new List<OrderItem> {
                            new OrderItem {
                                Product = product1,
                                UnitPrice = product1.Price,
                                Quantity = 1
                            },
                            new OrderItem {
                                Product = product2,
                                UnitPrice = product2.Price,
                                Quantity = 2
                            }
                        }
                    }
                }
            };

            dbContext.Users.Add(user);
            dbContext.SaveChanges();

            var query = dbContext.Users
                .Select(x => new {
                     Name = x.FullName,
                     x.TotalSpent
                 });

            var result = query.FirstOrDefault();

            Console.WriteLine($"Our user {result.Name} spent {result.TotalSpent}");
            Console.WriteLine($"We used the following query:");
            Console.WriteLine(query.ToQueryString());
        }
    }
}
