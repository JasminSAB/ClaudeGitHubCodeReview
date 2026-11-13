# Resource management

Use for: HttpClient, disposables, lifetimes, memory, connections.

- `new HttpClient()` per call or per request: sockets stay in `TIME_WAIT` and run out under load,
  and a long-lived static client ignores DNS changes. Use `IHttpClientFactory` or typed clients.
- `IDisposable` / `IAsyncDisposable` not disposed: streams, `SqlConnection`, `HttpResponseMessage`,
  timers, `CancellationTokenSource`. Look for missing `using` on every exit path, including
  exceptions.
- Service lifetimes: `DbContext` (scoped) used from a singleton or a `BackgroundService` without
  `IServiceScopeFactory.CreateScope()`.
- Unbounded growth: static lists or dictionaries used as caches with no eviction, `MemoryCache`
  without `SizeLimit` when the key space comes from user input, event handlers that are never
  unsubscribed.
- Whole-file and whole-table reads: `File.ReadAllBytes` / `ReadAsStringAsync` on uploads of
  unknown size, `ToList()` on a table before filtering. Stream, page, or limit the size.
- Connection and thread usage: long synchronous work on request threads, one database round trip
  per item in a loop.
