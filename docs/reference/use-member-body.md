# Use Member Body

The `UseMemberBody` option tells the source generator to use a **different member's body** as the source expression for the generated expression tree. This is useful when the projectable member itself cannot have a body.

## Basic Usage

```csharp
[Projectable(UseMemberBody = nameof(ComputeFullName))]
public string FullName => ComputeFullName();

private string ComputeFullName() => FirstName + " " + LastName;
```

The generator reads the body of `ComputeFullName` and generates an expression tree from it, even though `FullName` is marked as the projectable.

## Use Cases

### Interface Members

Interface members cannot have bodies. Use `UseMemberBody` to delegate to a default implementation or a helper:

```csharp
public interface IOrderSummary
{
    decimal GrandTotal { get; }
}

public class Order : IOrderSummary
{
    public decimal TaxRate { get; set; }
    public ICollection<OrderItem> Items { get; set; }

    // The actual computation
    private decimal ComputeGrandTotal() =>
        Items.Sum(i => i.Price * i.Quantity) * (1 + TaxRate);

    // Marks the interface member as projectable, delegates body
    [Projectable(UseMemberBody = nameof(ComputeGrandTotal))]
    public decimal GrandTotal => ComputeGrandTotal();
}
```

### Separating Declaration from Implementation

Keep the entity class clean by delegating computation to private helpers:

```csharp
public class Customer
{
    public DateTime BirthDate { get; set; }
    public DateTime LastOrderDate { get; set; }

    [Projectable(UseMemberBody = nameof(ComputeAge))]
    public int Age => ComputeAge();

    [Projectable(UseMemberBody = nameof(ComputeDaysSinceLastOrder))]
    public int DaysSinceLastOrder => ComputeDaysSinceLastOrder();

    // Implementation details hidden from the projectable declarations
    private int ComputeAge() =>
        DateTime.Today.Year - BirthDate.Year - (DateTime.Today.DayOfYear < BirthDate.DayOfYear ? 1 : 0);

    private int ComputeDaysSinceLastOrder() =>
        (DateTime.Today - LastOrderDate).Days;
}
```

### Reusing Bodies Across Multiple Members

The same body can power multiple projectable members:

```csharp
public class Order
{
    private bool IsEligibleForDiscount() =>
        Items.Count > 5 && TotalValue > 100;

    // Both members share the same expression body
    [Projectable(UseMemberBody = nameof(IsEligibleForDiscount))]
    public bool CanApplyDiscount => IsEligibleForDiscount();

    [Projectable(UseMemberBody = nameof(IsEligibleForDiscount))]
    public bool ShowDiscountBadge => IsEligibleForDiscount();
}
```

## Rules

- The referenced member (via `UseMemberBody`) must exist in the **same class** as the projectable member.
- The referenced member must have a **compatible return type**.
- The referenced member must be an **expression-bodied method or property** (it doesn't need `[Projectable]` itself).
- The referenced member must have a **compatible parameter list** — if the projectable is a method with parameters, the referenced member must have matching parameters.

## Method with Parameters

```csharp
public class Order
{
    [Projectable(UseMemberBody = nameof(ComputeDiscountedTotal))]
    public decimal GetDiscountedTotal(decimal discountRate) => ComputeDiscountedTotal(discountRate);

    private decimal ComputeDiscountedTotal(decimal discountRate) =>
        GrandTotal * (1 - discountRate);
}
```

