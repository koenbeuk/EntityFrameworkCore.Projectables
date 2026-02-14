# Block-Bodied Methods Support

As of this version, EntityFrameworkCore.Projectables now supports "classic" block-bodied methods decorated with `[Projectable]`, in addition to expression-bodied methods.

## What's Supported

Block-bodied methods can now be transformed into expression trees when they contain:

### 1. Simple Return Statements
```csharp
[Projectable]
public int GetConstant()
{
    return 42;
}
```

### 2. If-Else Statements (converted to ternary expressions)
```csharp
[Projectable]
public string GetCategory()
{
    if (Value > 100)
    {
        return "High";
    }
    else
    {
        return "Low";
    }
}
```

### 3. Nested If-Else Statements
```csharp
[Projectable]
public string GetLevel()
{
    if (Value > 100)
    {
        return "High";
    }
    else if (Value > 50)
    {
        return "Medium";
    }
    else
    {
        return "Low";
    }
}
```

### 4. Local Variable Declarations (inlined into the expression)
```csharp
[Projectable]
public int CalculateDouble()
{
    var doubled = Value * 2;
    return doubled + 5;
}
```

## Limitations and Warnings

The source generator will produce **warning EFP0003** when it encounters unsupported statements in block-bodied methods:

### Unsupported Statements:
- If statements without else clauses
- While, for, foreach loops  
- Switch statements (use switch expressions instead)
- Try-catch-finally blocks
- Throw statements
- New object instantiation in statement position
- Multiple statements (except local variable declarations before return)

### Example of Unsupported Pattern:
```csharp
[Projectable]
public int GetValue()
{
    if (IsActive)  // ❌ No else clause - will produce EFP0003 warning
    {
        return Value;
    }
    return 0;
}
```

Should be written as:
```csharp
[Projectable]
public int GetValue()
{
    if (IsActive)  // ✅ Has else clause
    {
        return Value;
    }
    else
    {
        return 0;
    }
}
```

Or as expression-bodied:
```csharp
[Projectable]
public int GetValue() => IsActive ? Value : 0;  // ✅ Expression-bodied
```

## How It Works

The source generator:
1. Parses block-bodied methods
2. Converts if-else statements to conditional (ternary) expressions
3. Inlines local variables into the return expression
4. Rewrites the resulting expression using the existing expression transformation pipeline
5. Generates the same output as expression-bodied methods

## Benefits

- **More readable code**: Complex logic with nested conditions is often easier to read with if-else blocks than with nested ternary operators
- **Gradual migration**: Existing code with block bodies can now be marked as `[Projectable]` without rewriting
- **Intermediate variables**: Local variables can make complex calculations more understandable

## Example Output

Given this code:
```csharp
public record Entity
{
    public int Value { get; set; }
    public bool IsActive { get; set; }

    [Projectable]
    public int GetAdjustedValue()
    {
        if (IsActive && Value > 0)
        {
            return Value * 2;
        }
        else
        {
            return 0;
        }
    }
}
```

The generated SQL will be:
```sql
SELECT CASE
    WHEN [e].[IsActive] = CAST(1 AS bit) AND [e].[Value] > 0 
    THEN [e].[Value] * 2
    ELSE 0
END
FROM [Entity] AS [e]
```
