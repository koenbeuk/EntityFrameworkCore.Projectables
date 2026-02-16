# Side Effect Detection in Block-Bodied Methods

This document describes the improved error reporting for side effects in block-bodied projectable methods.

## Overview

When using block-bodied methods with the `[Projectable]` attribute, the source generator now provides specific error messages that point to the exact line where side effects occur, making it much easier to identify and fix issues.

## Detected Side Effects

### 1. Property Assignments (EFP0004 - Error)

**Code:**
```csharp
[Projectable]
public int Foo()
{
    Bar = 10;  // ❌ Error on this line
    return Bar;
}
```

**Error Message:**
```
(11,13): error EFP0004: Assignment operation has side effects and cannot be used in projectable methods
```

### 2. Compound Assignments (EFP0004 - Error)

**Code:**
```csharp
[Projectable]
public int Foo()
{
    Bar += 10;  // ❌ Error on this line
    return Bar;
}
```

**Error Message:**
```
(11,13): error EFP0004: Compound assignment operator '+=' has side effects and cannot be used in projectable methods
```

### 3. Increment/Decrement Operators (EFP0004 - Error)

**Code:**
```csharp
[Projectable]
public int Foo()
{
    var x = 5;
    x++;  // ❌ Error on this line
    return x;
}
```

**Error Message:**
```
(12,13): error EFP0004: Increment/decrement operator '++' has side effects and cannot be used in projectable methods
```

### 4. Non-Projectable Method Calls (EFP0005 - Warning)

**Code:**
```csharp
[Projectable]
public int Foo()
{
    Console.WriteLine("test");  // ⚠️ Warning on this line
    return Bar;
}
```

**Warning Message:**
```
(11,13): warning EFP0005: Method call 'WriteLine' may have side effects. Only calls to methods marked with [Projectable] are guaranteed to be safe in projectable methods
```

## Before vs After

### Before
Generic error message at the beginning of the method:
```
warning EFP0003: Method 'Foo' contains an unsupported statement: Expression statements are not supported
```

### After
Specific error message pointing to the exact problematic line:
```
error EFP0004: Property assignment 'Bar' has side effects and cannot be used in projectable methods
```

## Benefits

1. **Precise Location**: Error messages now point to the exact line containing the side effect
2. **Specific Messages**: Clear explanation of what kind of side effect was detected
3. **Better Developer Experience**: Easier to identify and fix issues
4. **Severity Levels**: Errors for definite side effects, warnings for potential ones
5. **Actionable Guidance**: Messages explain why the code is problematic

## Diagnostic Codes

- **EFP0004**: Statement with side effects in block-bodied method (Error)
- **EFP0005**: Potential side effect in block-bodied method (Warning)

These are in addition to the existing:
- **EFP0001**: Method or property should expose an expression body definition (Error)
- **EFP0002**: Method or property is not configured to support null-conditional expressions (Error)
- **EFP0003**: Unsupported statement in block-bodied method (Warning)
