# Video guide

Each segment runs 3–5 minutes and follows the same rhythm: **problem → Before code → run → After code → run → what changed**.

Before recording:

```bash
docker compose up -d
dotnet run --project src/DemoRunner -- seed            # default sizes; use --perf for demo 04's big numbers
```

Use a terminal at least 120 columns wide, and have the editor open on `Before.cs` / `After.cs`.

---

## 01 — ReturningExtensions (≈4 min)

| Time | Show | Say |
|---|---|---|
| 0:00 | `Models.cs` | "A booking platform deletes expired bookings every night, and the auditors want a record of every one." |
| 0:30 | `Before.cs` | "The obvious approach: SELECT what's expired, ExecuteDelete, write the audit. And it's in a transaction, so it's safe, right?" Point out the `betweenSelectAndDelete` hook: "this is where another user gets lucky." |
| 1:15 | `dotnet run --project src/DemoRunner -- 1` | Stop on the BEFORE section: booking #11 audited but **still exists**, booking #1 deleted with **no audit**, and the counts match. "Your monitoring would say everything's fine." |
| 2:00 | Explain | "READ COMMITTED releases shared locks as soon as a row is read. The DELETE re-runs its WHERE clause against newer data. Two statements, two moments in time." |
| 2:30 | `After.cs`, `BookingDbContext.cs` | `.Deleted(b => new { ... })` plus `UseReturningExtensions()`. "One statement deletes and returns what it deleted." |
| 3:00 | Output: AFTER + table | Audit matches exactly; 1 SELECT/DELETE statement instead of 2. Rerun with `--sql` to show `DELETE ... OUTPUT [DELETED].[Id] ...`. "That's SQL Server's OUTPUT clause; EF Core just doesn't expose it." |
| 3:40 | Wrap-up | "The extension gives you the rows. It doesn't make the audit insert atomic — that's why the transaction is still there." Mention: "the tests hammer this with concurrent writers, and the Before version fails on the first iteration." |

## 02 — EfQueryComplexity (≈4 min)

| Time | Show | Say |
|---|---|---|
| 0:00 | Problem | "Customers build their own reports. Your API turns them into LINQ. What could go wrong?" |
| 0:20 | `Before.cs` | Three `Include`s, no `Take`. |
| 0:40 | `dotnet run --project src/DemoRunner -- 2 --sql` | BEFORE numbers: entities loaded vs **rows SQL Server returned** (≈3.5×). Show the three LEFT JOINs in the SQL panel. "Items × payments × history, per order: cartesian explosion." |
| 1:30 | AsSplitQuery line | "Split queries fix the multiplication, but nobody forces them on you, and it's still every order in the table." |
| 2:00 | `ReportingDbContext.cs` | The `QueryComplexityLimits` block: one line per rule. "Same levels for log and throw: reject, don't just warn." |
| 2:30 | `After.cs` + the table | Walk down the rows: unbounded, the Before export (three rules at once), 3 includes, Take(5000), the 40-condition dynamic filter (MaxDepth), and the paginated page that succeeds. |
| 3:20 | Exception message + RESULT | "Each violation names the rule and the numbers. And zero SQL was sent for the rejected ones." |
| 3:40 | WHAT CHANGED | "This checks the query's shape, not its cost. You still need indexes, paging, split queries and monitoring." |

## 03 — Projectables (≈3.5 min)

| Time | Show | Say |
|---|---|---|
| 0:00 | `Models.cs` → `PlainOrder.Total` | "Subtotal minus discount plus tax. Invoices use it, and reports need to filter by it." Mention the rounding comment: exact decimals, rounding only for display. |
| 0:30 | `Before.cs` | Attempt 1 throws "could not be translated". Attempt 2 copies the formula. "Now there are two copies of a financial rule." |
| 1:10 | `dotnet run --project src/DemoRunner -- 3` | BEFORE: the exception, then the workaround's count. "Change the domain formula and forget the query, and invoice and report silently disagree." |
| 1:50 | `Models.cs` → `Order` | `[Projectable]` on `Subtotal` and `Total`; `UseProjectables()` in the context. |
| 2:15 | `After.cs` + AFTER output | "The query we wanted to write in the first place." The SQL is **character-for-character identical** to the hand copy, and the table shows Before / After / C# in memory, all equal. |
| 2:50 | `--sql` | Point at the `SUM(...)` inside `WHERE` and `ORDER BY`: filtering happens in the database. |
| 3:15 | WHAT CHANGED | "A source generator writes the expression tree, and the interceptor inlines it. One formula, two runtimes." |

## 04 — Zomp Window Functions (≈4.5 min)

| Time | Show | Say |
|---|---|---|
| 0:00 | Problem + example table | "Deposit 1000, purchase 300, withdrawal 200, deposit 600: balance 1100. Easy, until there are 100,000 transactions and the user wants page 1000." |
| 0:30 | `Before.cs` | "A balance depends on every earlier row, so we load all of them, add them up, and keep 50." |
| 1:00 | `dotnet run --project src/DemoRunner -- 4 --perf` | BEFORE: **100,000 rows transferred** to display 50, plus the time. |
| 1:40 | `After.cs` | Read the `Over()` chain aloud: `PartitionBy`, `OrderBy`, `ThenBy(Id)` ("ties happen; Id makes it deterministic"), `Rows().FromUnbounded().ToCurrentRow()`. Then `Skip`/`Take`. |
| 2:30 | AFTER + page boundary table | 50 rows transferred, and the boundary check: last balance of the previous page + this amount = this balance. "Paging happens after the balance is computed." |
| 3:10 | `--sql` | `SUM([t].[Amount]) OVER(PARTITION BY ... ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)` followed by `OFFSET/FETCH`. |
| 3:40 | Compare table | Rows and median time. Be honest: "SQL Server still reads the earlier rows through the index. What we save is shipping and summing them in the app." |
| 4:10 | WHAT CHANGED | "SQL Server has had window functions since 2012. This package just lets you write them in LINQ." |

---

### Recording tips

* Use `--fast` for rehearsal takes; seeding and runs finish in seconds.
* Use `--sql` only for the segment where you discuss the SQL, so the screen stays readable.
* All numbers on screen are measured live. Re-record rather than editing them.
