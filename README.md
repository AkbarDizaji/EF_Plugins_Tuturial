# EF Core Extensions Demo

Four real production problems, each solved twice: once with plain EF Core 10 (**Before**) and once with a third-party extension (**After**). Built for a video walkthrough, so each demo is small enough to explain in 3–5 minutes.

| # | Problem | Extension | Version | License |
|---|---------|-----------|---------|---------|
| 01 | Audit exactly which expired bookings were deleted | [EFCore.ReturningExtensions.SqlServer](https://github.com/dsgordeev/EFCore.ReturningExtensions) | 10.2.3 | MIT |
| 02 | Stop a dynamic reporting API from running expensive queries | [EfQueryComplexity](https://github.com/SimonCropp/EfQueryComplexity) | 0.8.0 | MIT |
| 03 | One order-total formula for both C# and SQL | [EntityFrameworkCore.Projectables](https://github.com/EFNext/EntityFrameworkCore.Projectables) | 6.0.6 | MIT |
| 04 | Running balances on a paged account statement | [Zomp.EFCore.WindowFunctions.SqlServer](https://github.com/zompinc/efcore-extensions) | 10.1.20 | MIT |

All four packages target `net10.0` and EF Core 10. This was checked against the NuGet package metadata, and the APIs were checked by reflection against the published assemblies. All four run in one .NET 10 solution with EF Core 10.0.14 and SQL Server 2022. No isolated projects on older frameworks were needed.

## Prerequisites

* .NET 10 SDK (`dotnet --version` → 10.0.x)
* Docker (on Apple Silicon, the SQL Server image runs under amd64 emulation, which works fine)

## Quick start

```bash
cd EfCoreExtensionsDemo
docker compose up -d                                   # SQL Server 2022 on localhost:14333
dotnet run --project src/DemoRunner -- seed            # create + seed all four databases
dotnet run --project src/DemoRunner                    # interactive menu
```

SQL Server listens on host port **14333**, so it won't clash with a local SQL Server on 1433. To use a different server, set `EFDEMO_SQL` to a full connection string *without* `Database=`:

```bash
export EFDEMO_SQL="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True"
```

## Commands

Everything goes through the runner:

```bash
dotnet run --project src/DemoRunner                    # menu: pick a demo, run all, seed, reset, toggle SQL
dotnet run --project src/DemoRunner -- all             # run all four demos
dotnet run --project src/DemoRunner -- 1               # run one demo (1, 2, 3 or 4)
dotnet run --project src/DemoRunner -- seed            # drop + re-seed every demo database
dotnet run --project src/DemoRunner -- reset           # drop every demo database
```

Each demo can also run on its own:

```bash
dotnet run --project src/01-Returning
dotnet run --project src/02-QueryComplexity
dotnet run --project src/03-Projectables
dotnet run --project src/04-WindowFunctions -- --perf --sql
dotnet run --project src/03-Projectables -- seed       # or: reset
```

### Flags (work with every command)

| Flag | Effect |
|------|--------|
| `--sql` | Print the key generated SQL statements in panels |
| `--log-sql` | Stream **every** SQL command through EF Core's `LogTo` (noisy) |
| `--fast` | Small datasets, for quick takes |
| `--perf` | Large datasets, to make the differences measurable |

### Data sizes

| | `--fast` | default | `--perf` |
|---|---|---|---|
| Bookings (01) | 1,000 | 10,000 | 100,000 |
| Orders (02), each with 3–8 items, 1–3 payments, 2–6 history rows | 1,000 | 10,000 | 50,000 |
| Orders (03), each with 1–6 items | 1,000 | 10,000 | 100,000 |
| Transactions (04), 10 accounts | 10,000 | 100,000 | 1,000,000 |

Seeds are fixed (`new Random(42)` and so on) and Ids are assigned explicitly, so every run produces the same data and the same results. A demo re-seeds itself automatically when its row count doesn't match the selected size. Seeding uses `SqlBulkCopy`, so even `--perf` takes only seconds.

Indexes created for the main query patterns:
* `Bookings(EndsAt)`
* FK indexes on every child table
* `Orders(CustomerId)` and `Orders(CreatedAt)`
* `Transactions(AccountId, CreatedAt, Id) INCLUDE (Kind, Amount)`, which covers the window-function query

## Project layout

```text
EfCoreExtensionsDemo/
├── docker-compose.yml
├── global.json                 # .NET 10 SDK + Microsoft.Testing.Platform for `dotnet test`
├── nuget.config                # nuget.org only, so restore never depends on private feeds
├── src/
│   ├── Common/                 # settings, console UI, SQL-capturing interceptor, bulk seeding, timing
│   ├── 01-Returning/           # Models.cs, BookingDbContext.cs, Before.cs, After.cs, Seed.cs, Demo.cs, Program.cs
│   ├── 02-QueryComplexity/     # Models.cs, ReportingDbContext.cs, Before.cs, After.cs, ...
│   ├── 03-Projectables/        # Models.cs, ShopDbContext.cs, Before.cs, After.cs, ...
│   ├── 04-WindowFunctions/     # Models.cs, BankDbContext.cs, Before.cs, After.cs, ...
│   └── DemoRunner/             # menu / run all / seed / reset
└── tests/EfCoreExtensionsDemo.Tests/
```

In every demo, `Before.cs` and `After.cs` hold the code to show on screen. `Demo.cs` holds the narration: it runs both versions, verifies the results and prints the output. In each DbContext, the extension is enabled by one `if` in `OnConfiguring`, so that one line is the only difference between Before and After.

## The demos

### 01 — ReturningExtensions: audit what was *really* deleted

* **Before:** `SELECT` the expired bookings, run `ExecuteDeleteAsync`, then write audit rows from the `SELECT` result, all inside a READ COMMITTED transaction. The demo injects two concurrent changes between the `SELECT` and the `DELETE` from a second connection: one expired booking is extended, and one active booking is ended early. The audit then claims a booking was deleted that still exists, and misses one that was deleted. The **counts still match**, so a row-count check wouldn't catch it.
* **After:** `.Deleted(b => new { b.Id, b.UserId, b.EndsAt })` generates `DELETE ... OUTPUT [DELETED].[Id], [DELETED].[UserId], [DELETED].[EndsAt]`, so the audit is built from the exact rows the statement removed.
* **Shown:** round trips (counted by a `DbCommandInterceptor`), exact deleted and audited sets (verified against the database), and the SQL.
* **Not claimed:** the extension does not make the audit insert atomic with the delete. That still comes from the explicit transaction.

### 02 — EfQueryComplexity: guardrails for dynamic reports

* **Before:** `Include(Items).Include(Payments).Include(History).ToListAsync()`. The demo runs the exact SQL EF generated and counts the rows SQL Server returns: about 3.5× more rows than entities, because the three collections are LEFT JOINed side by side (cartesian explosion). It also shows that `AsSplitQuery()` fixes the multiplication but not the unbounded result.
* **After:** `UseQueryComplexity(logAt: limits, throwAt: limits)` with these limits:
  * `MaxTake` 1000
  * `MaxIncludes` 2
  * `MaxDepth` 25
  * `MaxSingleQueryCollections` 1
  * `RejectUnbounded`

  The demo runs six report queries. Five are rejected with `QueryComplexityException`, and the table shows each violated rule with its actual and maximum values. The paginated, split query succeeds.
* **Not claimed:** the library analyses query *shape*, not SQL cost. Indexes, pagination, `AsSplitQuery` and database monitoring solve related but different problems. The library does have an optional `sqlServerCostLimit`, which uses SQL Server's query governor; it isn't used here.

### 03 — Projectables: one formula for C# and SQL

* **Before:** `PlainOrder.Total` is an ordinary property. `Where(o => o.Total > 1000)` throws "could not be translated", so the workaround copies the formula into the LINQ query.
* **After:** `[Projectable] Order.Total` plus `UseProjectables()`. The query you wanted to write now works.
* **Shown:** the generated SQL is **character-for-character identical** to the hand-copied version, filtering and sorting happen in SQL, and the results match exactly: same orders, same order, same decimals. They also match evaluating `Order.Total` in C# over every order.
* **Rounding assumptions:** prices are `decimal(18,2)` and rates are `decimal(4,2)`. `Total` is therefore exact at 6 decimal places, both in C# and in SQL Server, whose minimum decimal scale is 6. Values are rounded to cents (`MidpointRounding.AwayFromZero`) only for display.

### 04 — Zomp Window Functions: running balances with server-side paging

* **Before:** load the whole account history ordered by `CreatedAt, Id`, add it up in C#, then keep one page.
* **After:** `EF.Functions.Sum(t.Amount, EF.Functions.Over().PartitionBy(t.AccountId).OrderBy(t.CreatedAt).ThenBy(t.Id).Rows().FromUnbounded().ToCurrentRow())`, followed by `Skip` / `Take`. It translates to exactly:

  ```sql
  SUM([t].[Amount]) OVER(PARTITION BY [t].[AccountId] ORDER BY [t].[CreatedAt], [t].[Id]
                         ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
  ... ORDER BY [t].[CreatedAt], [t].[Id] OFFSET @p ROWS FETCH NEXT @p4 ROWS ONLY
  ```
* **Shown:** rows transferred, median time, identical lines, and the balance carrying across a page boundary.
* **Ordering:** about 15% of transactions share a timestamp and the Ids are shuffled, so `Id` really is needed as the tie-breaker.
* **Not claimed:** SQL Server has had window functions natively since 2012. The extension makes them available from LINQ. The database still reads the earlier rows to compute a balance; what goes away is transferring them and summing them on the app server.

## Tests

```bash
docker compose up -d
dotnet test
```

The tests run against the Docker SQL Server, using separate `*_Tests` and `*_Smoke` databases so they never touch the data you recorded with. They check:

* **Returning:**
  * The Before race reproduces deterministically: the extended booking is audited but not deleted, and the ended-early booking is deleted but not audited.
  * After runs the same scenario consistently.
  * After sends exactly one `DELETE ... OUTPUT` and no `SELECT`.
  * **Real concurrency:** three writer connections keep moving bookings across the cutoff while After runs, over 5 iterations. The audit must equal the set of rows that disappeared. If SQL Server picks the cleanup as a deadlock victim, which does happen under this contention, the test checks that the transaction left no trace (nothing deleted, nothing audited) and retries. Pointed at Before (with a 30 ms gap), the same test fails on the first iteration.
* **QueryComplexity:**
  * Each rejection reports the expected rule with its actual and maximum values.
  * The safe page returns rows 301–400 with their Includes loaded.
  * Rejected queries send zero SQL commands.
  * The unprotected context really does produce 3 LEFT JOINs.
* **Projectables:**
  * The plain property is not translatable.
  * After equals both the duplicated formula and evaluation in memory.
  * The SQL filters and sorts on the `SUM`, and is identical to the hand-written version.
  * The property still works in memory.
* **Window functions:**
  * Every page of two accounts equals the C# running balance.
  * The balance carries across **every** page boundary.
  * The opening rows match the example statement (1000 / 700 / 500 / 1100).
  * The exact `OVER(...)` clause and `OFFSET/FETCH` appear in the SQL.
  * The data really contains timestamp ties.
* **Smoke:** every demo runs end to end.

## About the measurements

* Round trips and SQL are **captured**, not inferred: the `SqlCapture` interceptor records each command EF Core sends.
* Row counts are **measured**. For example, Demo 02 runs the generated SQL and counts the reader rows.
* Times are a **median of 5 runs after one warm-up** (`Measure.MedianAsync`). Demo 02's large loads are timed once and labelled "single run". All times are approximate and depend on your machine; nothing in the output is hard-coded.
* BenchmarkDotNet was left out on purpose. Every measured operation is dominated by a database round trip, and the point is the *order of magnitude* (rows transferred, statements sent), which the demo measures directly. Run `--perf` for the clearest differences.

## Limitations and compatibility notes

* **ReturningExtensions 10.2.3** describes itself as "research-driven", and its README says the returned `IQueryable` should only be enumerated. `Deleted(...)` works with an anonymous-type or entity projection, but **projecting into a record constructor throws** `ArgumentOutOfRangeException` inside EF's `ApplyProjection`. `After.cs` uses an anonymous type and maps it afterwards.
* **EfQueryComplexity 0.8.0** registers a replacement `IQueryCompiler` (an internal EF API, EF1001) whenever `throwAt` or `MaxTake` is used, so it's tied to the EF Core major version it was built for and conflicts with other libraries that replace `IQueryCompiler`. It also requires `throwAt ≥ logAt` for every level. This demo passes the same limits for both.
* **Projectables 6.0.6** also hooks the query pipeline. Each demo has its own DbContext, so the extensions never meet. In a real application, test such combinations before relying on them.
* **Zomp** `AsSubQuery()` is `[Experimental]` and isn't used here. Note that a filter added after the window function (for example "only October") would shrink the window and change the opening balance. Zomp's README explains when it pushes the query down automatically.

## Troubleshooting

* **`Cannot reach SQL Server`:** run `docker compose up -d` and wait for `docker compose ps` to report `healthy`, which takes about 20 seconds on the first start.
* **NuGet 401 from a private feed:** the repo's `nuget.config` clears all sources except nuget.org. Run commands from the repo root.
* **`dotnet test` complains about VSTest:** `global.json` opts into Microsoft.Testing.Platform. Run `dotnet test` from the repo root.
