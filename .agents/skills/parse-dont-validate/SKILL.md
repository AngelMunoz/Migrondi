---
name: parse-dont-validate
description: Preserve invariants in types when changing domain models, input boundaries, or state transitions.
---

# Parse, Don't Validate

Parse raw input into domain values before effects depend on valid input.
Make downstream functions consume those values instead of repeating checks on primitives.

## Model the invariant

- Identify the invariant that the consumer requires.
- Choose a representation that preserves the invariant.
- Trace callers to the input boundary.
- Refine a value when a constraint applies only to one branch.
- Introduce a type only when it prevents a concrete error or expresses a domain distinction.
- Name each type for the guarantee that it establishes.

In F#, use records for values that must exist together. Use discriminated unions for alternatives or state-specific data.

Use distinct types when exchanging two primitive values would cause an error. Reuse suitable domain types.

Use constrained types for range or format guarantees. Enforce smart constructors through private representations or `.fsi` signatures.

Return `Result<DomainValue, ParseError>` when callers need failure details. Use `option` when absence gives sufficient information.

Represent a non-empty collection with a required head and a tail. Use units of measure for dimensions, not range constraints.

## Preserve the guarantee

- Keep domain values intact through domain operations.
- Extract primitives only for serialization, display, or operations that require them.
- Check construction, update, deserialization, persistence-loading, and conversion paths.
- Make each operation preserve the invariant or return an explicit failure.
- Separate raw transport data from domain values when serializers bypass constructors.
- Preserve existing storage and wire contracts unless the user requests changes.

Reject invalid input according to the domain rules. Normalize, truncate, deduplicate, or substitute defaults only when the contract permits it.

Handle each state explicitly when a handler accepts all states. Consider accepting a state-specific type when an operation requires one state.

Keep runtime checks for permissions, concurrency, availability, and other changing external facts. A parsed value cannot permanently prove these facts.

A formatted email address does not prove ownership or deliverability.

## Verify the result

- Verify that invalid input produces the required failure.
- Verify that successful parsing produces the type required by consumers.
- Check that supported APIs cannot bypass construction.
- Check that updates preserve the invariant.
- Remove repeated checks only when the type guarantees the checked property.
- Report remaining runtime checks and construction paths that bypass the guarantee.

Sources: [Parse, Don't Validate](https://lexi-lambda.github.io/blog/2019/11/05/parse-don-t-validate/)
and [Designing with Types](https://fsharpforfunandprofit.com/series/designing-with-types/).
