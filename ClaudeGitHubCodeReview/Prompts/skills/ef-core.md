# EF Core queries and persistence

Use for: queries, tracking, indexes, concurrency tokens, transactions.

- Over-fetching: `Include` chains that load whole entities (especially Identity users with
  password hashes / security stamps) when a projection (`Select` into a DTO) is enough.
- Client-side evaluation and in-memory aggregation (`ToList()` then `GroupBy/Sum`) for data that
  could be aggregated in SQL; unbounded result sets (no paging / date range limits).
- `AsNoTracking` for read-only queries; tracking only when you intend to save.
- N+1 queries, cartesian explosion (multiple collection `Include`s without `AsSplitQuery`).
- Missing indexes for the filters used (date ranges, `IsActive` flags), missing unique / filtered
  unique constraints for invariants such as "only one active X".
- Concurrency: lost updates on read-modify-write of counters/totals (use a concurrency token /
  `rowversion`, or an atomic `ExecuteUpdate`), races between "check then insert".
- Transactions: user-initiated transactions vs. execution strategies (retry on failure),
  `SaveChanges` called several times inside one logical operation.
- Date range filters: inclusive end bounds like `<= 23:59:59` lose sub-second values; prefer
  half-open intervals (`>= from && < to`).
- Soft delete handled consistently (global query filters vs. manual `!IsDeleted` everywhere).
- `FirstOrDefault` without `OrderBy` where more than one row can match.
