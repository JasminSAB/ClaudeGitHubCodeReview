# Domain-Driven Design

Use for: entities, aggregates, invariants, value objects, time and money.

- Anemic model: entities with only public setters while all rules live in services. Suggest
  behaviour methods that protect invariants (`order.Confirm(...)`, `invoice.Close(...)`).
- Aggregate boundaries and consistency: which invariants must be transactional, which entity is
  the root, are two aggregates modified in one transaction without a reason?
- Value objects for money, time ranges, enumerations with a closed set of values (e.g. order
  status, currency, product type) instead of primitives and strings.
- Invariants enforced in one place; no duplicated / denormalised state that can drift
  (e.g. a stored running total that is also recomputed elsewhere).
- Domain must not depend on UI concerns (localised user messages), the clock (`DateTime.Now`
  defaults in entities), or framework base classes, where it can be avoided.
- Time and money correctness: time zones / DST, rounding rules, decimal precision.
