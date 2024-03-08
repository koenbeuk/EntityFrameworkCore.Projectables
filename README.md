# EntitiyFrameworkCore.Projectables
Flexible projection magic for EF Core

[![NuGet version (EntityFrameworkCore.Projectables)](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/)
[![.NET](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/actions/workflows/build.yml/badge.svg)](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/actions/workflows/build.yml)

## NuGet packages
- EntityFrameworkCore.Projectables.Abstractions [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/) [![NuGet](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/)
- EntityFrameworkCore.Projectables [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/) [![NuGet](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projectables.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/)

> Starting with V2 of this project we're binding against **EF Core 6**. If you're targeting **EF Core 5** or **EF Core 3.1** then you can use the latest v1 release. These are functionally equivalent.


## Getting started
1. Install the package from [NuGet](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/)
2. Enable Projectables in your DbContext by adding: `dbContextOptions.UseProjectables()`
3. Implement projectable properties and methods, marking them with the `[Projectable]` attribute.
4. Explore our [samples](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/tree/master/samples) and checkout our [Blog Post](https://onthedrift.com/posts/efcore-projectables/) for further guidance.

### Example
Assuming this sample:

```csharp
class Order {
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedDate { get; set; }

    public decimal TaxRate { get; set; }
    
    public User User { get; set; } 
    public ICollection<OrderItem> Items { get; set; }

    [Projectable] public decimal Subtotal => Items.Sum(item => item.Product.ListPrice * item.Quantity);
    [Projectable] public decimal Tax => Subtotal * TaxRate;
    [Projectable] public decimal GrandTotal => Subtotal + Tax;
}

public static class UserExtensions {
    [Projectable]
    public static Order GetMostRecentOrderForUser(this User user, DateTime? cutoffDate) => 
        user.Orders
            .Where(x => cutoffDate == null || x.CreatedDate >= cutoffDate)
            .OrderByDescending(x => x.CreatedDate)
            .FirstOrDefault();
}

var result = _dbContext.Users
    .Where(x => x.UserName == "Jon")
    .Select(x => new {
        x.GetMostRecentOrderForUser(DateTime.UtcNow.AddDays(-30)).GrandTotal
    });
    .FirstOrDefault();
```

The following query gets generated (assuming SQL Server as a database provider)
```sql
DECLARE @__sampleUser_UserName_0 nvarchar(4000) = N'Jon';

SELECT (
    SELECT COALESCE(SUM([p].[ListPrice] * CAST([o].[Quantity] AS decimal(18,2))), 0.0)
    FROM [OrderItem] AS [o]
    INNER JOIN [Products] AS [p] ON [o].[ProductId] = [p].[Id]
    WHERE (
        SELECT TOP(1) [o0].[Id]
        FROM [Orders] AS [o0]
        WHERE [u].[Id] = [o0].[UserId] AND [o0].[FulfilledDate] IS NOT NULL
        ORDER BY [o0].[CreatedDate] DESC) IS NOT NULL AND (
        SELECT TOP(1) [o1].[Id]
        FROM [Orders] AS [o1]
        WHERE [u].[Id] = [o1].[UserId] AND [o1].[FulfilledDate] IS NOT NULL
        ORDER BY [o1].[CreatedDate] DESC) = [o].[OrderId]) * (
    SELECT TOP(1) [o2].[TaxRate]
    FROM [Orders] AS [o2]
    WHERE [u].[Id] = [o2].[UserId] AND [o2].[FulfilledDate] IS NOT NULL
    ORDER BY [o2].[CreatedDate] DESC) AS [GrandTotal]
FROM [Users] AS [u]
WHERE [u].[UserName] = @__sampleUser_UserName_0
```

Projectable properties and methods have been inlined! the generated SQL could be improved but this is what EF Core (v8) gives us.

### How it works
Essentially, there are two components: We have a source generator that can write companion expressions for properties and methods marked with the Projectable attribute. Then, we have a runtime component that intercepts any query and translates any call to a property or method marked with the Projectable attribute, translating the query to use the generated expression instead.

### FAQ

#### Are there currently any known limitations?
Currently, there is no support for overloaded methods. Each method name needs to be unique within a given type.

#### Is this specific to a database provider?
No, the runtime component injects itself into the EFCore query compilation pipeline, thus having no impact on the database provider used. Of course, you're still limited to whatever your database provider can do.

#### Are there performance implications that I should be aware of?
There are two compatibility modes: Limited and Full (Default). Most of the time, limited compatibility mode is sufficient. However, if you are running into issues with failed query compilation, then you may want to stick with Full compatibility mode. With Full compatibility mode, each query will first be expanded (any calls to Projectable properties and methods will be replaced by their respective expression) before being handed off to EFCore. (This is similar to how LinqKit/LinqExpander/Expressionify works.) Because of this additional step, there is a small performance impact. Limited compatibility mode is smart about things and only expands the query after it has been accepted by EF. The expanded query will then be stored in the Query Cache. With Limited compatibility, you will likely see increased performance over EFCore without projectables.

#### Can I call additional properties and methods from my Projectable properties and methods?
Yes, you can! Any projectable property/method can call into other properties and methods as long as those properties/methods are native to EFCore or marked with a Projectable attribute.

#### Can I use projectable extensions methods on non-entity types?
Yes you can. It's perfectly acceptable to have the following code:
```csharp
[Projectable]
public static int Squared(this int i) => i * i;
```
Any call to squared given any int will perfectly translate to SQL.

#### How do I deal with nullable properties
Expressions and Lamdas are different and not equal. Expressions can only express a subset of valid CSharp statements that are allowed in lambda's and arrow functions. One obvious limitation is the null-conditional operator. Consider the following example:
```csharp
[Projectable]
public static string? GetFullAddress(this User? user) => user?.Location?.AddressLine1 + " " + user?.Location.AddressLine2;
```
This is a perfectly valid arrow function but it can't be translated directly to an expression tree. This Project will generate an error by default and suggest 2 solutions: Either you rewrite the function to explicitly check for nullables or you let the generator do that for you!

Starting from the official release of V2, we can now hint the generator in how to translate this arrow function to an expression tree. We can say:
```csharp
[Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
``` 
which will simply generate an expression tree that ignores the null-conditional operator. This generates: 
```csharp
user.Location.AddressLine1 + " " + user.Location.AddressLine2
```
This is perfect for a database like SQL Server where nullability is implicit and if any of the arguments were to be null, the resulting value will be null. If you are dealing with CosmosDB (which may result to client-side evaluation) or want to be explicit about things. You can configure your projectable as such: 
```csharp
[Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
```
This will rewrite your expression to explicitly check for nullables. In the former example, this will be rewritten to: 
```csharp 
(user != null ? user.Location != null ? user.Location?.AddressLine 1 + (user != null ? user.Location != null ? user.Location.AddressLine2 : null) : null)
```
Note that using rewrite (not ignore) may increase the actual SQL query complexity being generated with some database providers such as SQL Server

#### Can I use projectables in any part of my query?
Certainly, consider the following example: 
```csharp
public class User
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }

    [Projectable]
    public string FullName => FirstName + " " + LastName;
}

var query = dbContext.Users
    .Where(x => x.FullName.Contains("Jon"))
    .GroupBy(x => x.FullName)
    .OrderBy(x => x.Key)
    .Select(x => x.Key);
```
Which generates the following SQL (SQLite syntax)
```sql
SELECT (COALESCE("u"."FirstName", '') || ' ') || COALESCE("u"."LastName", '')
FROM "Users" AS "u"
WHERE ('Jon' = '') OR (instr((COALESCE("u"."FirstName", '') || ' ') || COALESCE("u"."LastName", ''), 'Jon') > 0)
GROUP BY (COALESCE("u"."FirstName", '') || ' ') || COALESCE("u"."LastName", '')
ORDER BY (COALESCE("u"."FirstName", '') || ' ') || COALESCE("u"."LastName", '')
```

#### How does this relate to [Expressionify](https://github.com/ClaveConsulting/Expressionify)?
Expressionify is a project that was launched before this project. It has some overlapping features and uses similar approaches. When I first published this project, I was not aware of its existence, so shame on me. Currently, Expressionify targets a more focused scope of what this project is doing, and thereby it seems to be more limiting in its capabilities. Check them out though!

#### How does this relate to LinqKit/LinqExpander/...?
There are a few projects like [LinqKit](https://github.com/scottksmith95/LINQKit) that were created before we had source generators in .NET. These are great options if you're stuck with classical EF or don't want to rely on code generation. Otherwise, I would suggest that EntityFrameworkCore.Projectables and Expressionify are superior approaches as they can rely on SourceGenerators to do most of the hard work.

#### Is the available for EFCore 3.1, 5 and 6?
V1 is targeting EF Core 5 and 3.1. V2 and V3 are targeting EF Core 6 and are compatible with EF Core 7. You can upgrade/downgrade between these versions based on your EF Core version requirements.

#### What is next for this project?
TBD... However, one thing I'd like to improve is our expression generation logic as it's currently making a few assumptions (have yet to experience it breaking). Community contributions are very welcome!