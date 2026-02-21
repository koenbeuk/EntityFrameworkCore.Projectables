# Diagnostics Reference

The Projectables source generator emits diagnostics (warnings and errors) during compilation to help you identify and fix issues with your projectable members.

## Overview

| ID | Severity | Title |
|---|---|---|
| [EFP0001](#efp0001) | ⚠️ Warning | Block-bodied member support is experimental |
| [EFP0002](#efp0002) | ❌ Error | Null-conditional expression not configured |
| [EFP0003](#efp0003) | ⚠️ Warning | Unsupported statement in block-bodied method |
| [EFP0004](#efp0004) | ❌ Error | Statement with side effects in block-bodied method |
| [EFP0005](#efp0005) | ⚠️ Warning | Potential side effect in block-bodied method |
| [EFP0006](#efp0006) | ❌ Error | Method or property requires a body definition |

---

## EFP0001 — Block-bodied member support is experimental {#efp0001}

**Severity:** Warning  
**Category:** Design

### Message

```
Block-bodied member '{0}' is using an experimental feature. 
Set AllowBlockBody = true on the Projectable attribute to suppress this warning.
```

### Cause

A `[Projectable]` member uses a block body (`{ ... }`) instead of an expression body (`=>`), which is an experimental feature.

### Fix

Suppress the warning by setting `AllowBlockBody = true`:

```csharp
// Before (warning)
[Projectable]
public string GetCategory()
{
    if (Value > 100) return "High";
    return "Low";
}

// After (warning suppressed)
[Projectable(AllowBlockBody = true)]
public string GetCategory()
{
    if (Value > 100) return "High";
    return "Low";
}
```

Or convert to an expression-bodied member:

```csharp
[Projectable]
public string GetCategory() => Value > 100 ? "High" : "Low";
```

---

## EFP0002 — Null-conditional expression not configured {#efp0002}

**Severity:** Error  
**Category:** Design

### Message

```
'{0}' has a null-conditional expression exposed but is not configured to rewrite this 
(Consider configuring a strategy using the NullConditionalRewriteSupport property 
on the Projectable attribute)
```

### Cause

The projectable member's body contains a null-conditional operator (`?.`) but `NullConditionalRewriteSupport` is not configured (defaults to `None`).

### Fix

Configure how the `?.` operator should be handled:

```csharp
// ❌ Error
[Projectable]
public string? FullAddress => Location?.AddressLine1 + " " + Location?.City;

// ✅ Option 1: Ignore (strips the ?. — safe for SQL Server)
[Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
public string? FullAddress => Location?.AddressLine1 + " " + Location?.City;

// ✅ Option 2: Rewrite (explicit null checks)
[Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
public string? FullAddress => Location?.AddressLine1 + " " + Location?.City;

// ✅ Option 3: Rewrite the expression manually
[Projectable]
public string? FullAddress =>
    Location != null ? Location.AddressLine1 + " " + Location.City : null;
```

See [Null-Conditional Rewrite](/reference/null-conditional-rewrite) for details.

---

## EFP0003 — Unsupported statement in block-bodied method {#efp0003}

**Severity:** Warning  
**Category:** Design

### Message

```
Method '{0}' contains an unsupported statement: {1}
```

### Cause

A block-bodied `[Projectable]` member contains a statement type that cannot be converted to an expression tree (e.g., loops, try-catch, throw, new object instantiation in statement position).

### Unsupported Statements

- `while`, `for`, `foreach` loops
- `try`/`catch`/`finally` blocks
- `throw` statements
- Object instantiation as a statement (not in a `return`)

### Fix

Refactor to use only supported constructs (`if`/`else`, `switch`, local variables, `return`), or convert to an expression-bodied member:

```csharp
// ❌ Warning: loops are not supported
[Projectable(AllowBlockBody = true)]
public int SumItems()
{
    int total = 0;
    foreach (var item in Items)  // EFP0003
        total += item.Price;
    return total;
}

// ✅ Use LINQ instead
[Projectable]
public int SumItems() => Items.Sum(i => i.Price);
```

---

## EFP0004 — Statement with side effects in block-bodied method {#efp0004}

**Severity:** Error  
**Category:** Design

### Message

Context-specific — one of:

- `Property assignment '{0}' has side effects and cannot be used in projectable methods`
- `Compound assignment operator '{0}' has side effects`
- `Increment/decrement operator '{0}' has side effects`

### Cause

A block-bodied projectable member modifies state. Expression trees cannot represent side effects.

### Triggers

```csharp
// ❌ Property assignment
Bar = 10;

// ❌ Compound assignment
Bar += 10;

// ❌ Increment / Decrement
Bar++;
--Count;
```

### Fix

Remove the side-effecting statement. Projectable members must be **pure functions** — they can only read data and return a value.

```csharp
// ❌ Error: has side effects
[Projectable(AllowBlockBody = true)]
public int Foo()
{
    Bar = 10;       // EFP0004
    return Bar;
}

// ✅ Read-only computation
[Projectable]
public int Foo() => Bar + 10;
```

---

## EFP0005 — Potential side effect in block-bodied method {#efp0005}

**Severity:** Warning  
**Category:** Design

### Message

```
Method call '{0}' may have side effects and cannot be guaranteed to be safe in projectable methods
```

### Cause

A block-bodied projectable member calls a method that is **not** itself marked with `[Projectable]`. Such calls may have side effects that cannot be represented in an expression tree.

### Example

```csharp
[Projectable(AllowBlockBody = true)]
public int Foo()
{
    Console.WriteLine("test");  // ⚠️ EFP0005 — may have side effects
    return Bar;
}
```

### Fix

- Remove the method call if it is not needed in a query context.
- If the method is safe to use in queries, mark it with `[Projectable]`.

---

## EFP0006 — Method or property requires a body definition {#efp0006}

**Severity:** Error  
**Category:** Design

### Message

```
Method or property '{0}' should expose a body definition (e.g. an expression-bodied member 
or a block-bodied method) to be used as the source for the generated expression tree.
```

### Cause

A `[Projectable]` member has no body — it is abstract, an interface declaration, or an auto-property without an expression.

### Fix

Provide a body, or use [`UseMemberBody`](/reference/use-member-body) to delegate to another member:

```csharp
// ❌ Error: no body
[Projectable]
public string FullName { get; set; }

// ✅ Expression-bodied property
[Projectable]
public string FullName => FirstName + " " + LastName;

// ✅ Delegate to another member
[Projectable(UseMemberBody = nameof(ComputeFullName))]
public string FullName => ComputeFullName();
private string ComputeFullName() => FirstName + " " + LastName;
```

---

## Suppressing Diagnostics

Individual warnings can be suppressed with standard C# pragma directives:

```csharp
#pragma warning disable EFP0001
[Projectable]
public string GetValue()
{
    if (IsActive) return "Active";
    return "Inactive";
}
#pragma warning restore EFP0001
```

Or via `.editorconfig` / `Directory.Build.props`:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);EFP0001;EFP0003</NoWarn>
</PropertyGroup>
```

