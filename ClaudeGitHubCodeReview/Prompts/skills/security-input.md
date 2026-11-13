# Input and data security

Use for: injection, untrusted input, secrets, error output, CORS.

- SQL built from strings: `FromSqlRaw`, `ExecuteSqlRaw`, `SqlCommand` with string concatenation or
  interpolation of user input. Use `FromSql` / `ExecuteSql` (interpolation becomes parameters) or
  explicit `SqlParameter`s. Note that `FromSqlRaw($"...{input}")` is NOT parameterized.
- Paths from user input: `Path.Combine(root, fileName)` with `..` or an absolute path escapes the
  root. Normalize with `Path.GetFullPath` and check it still starts with the root.
- Secrets: connection strings, API keys or passwords in `appsettings.json`, source files or
  committed `.env` files; secrets written to logs or returned in error responses.
- Error output: `UseDeveloperExceptionPage` or exception messages and stack traces returned to the
  client outside Development.
- Deserialization: `TypeNameHandling.All/Auto` (Newtonsoft) or `BinaryFormatter` on untrusted data.
- CORS: `AllowAnyOrigin` together with credentials, or a wildcard origin on an API that uses cookies.
- Logging: personal data or tokens in structured logs without masking.
