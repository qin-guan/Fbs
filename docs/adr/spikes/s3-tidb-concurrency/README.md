# Spike S3: TiDB and check-then-write

Question from [ADR 0001](../../0001-clerk-multitenancy-tidb.md): after waiting on a row lock, does a
plain re-read see what the other transaction committed? TiDB's documentation says it may not,
because TiDB takes the transaction's snapshot at `BEGIN` while MySQL takes it at the first plain
`SELECT`.

## Result

Two overlapping bookings of one facility, five runs per mode, run against TiDB v8.5.8 (standalone,
default arguments) and MySQL 8.4:

| Overlap check | TiDB | MySQL |
|---|---|---|
| Plain `SELECT` after locking the facility row | **double-booked 5 of 5** | 0 of 5 |
| `SELECT ... FOR UPDATE` after locking the facility row | 0 of 5 | 0 of 5 |
| Plain `SELECT` under `READ COMMITTED` | 0 of 5 | not run |

So the pattern in `GeeksHacking/portal` (`LockRowAsync`, then re-read with a plain query) is sound
on MySQL and unsound on TiDB, and portal's concurrency tests run on a MySQL container while it
deploys to TiDB. The booking rule in the ADR (lock the facility rows, then run the overlap check as a
locking read) is confirmed, and the concurrency tests in this repo run against TiDB.

## Reproduce

```bash
docker run -d --name tidb -p 4000:4000 pingcap/tidb:v8.5.8        # root, no password
docker run -d --name mysql -p 3307:3306 -e MYSQL_ALLOW_EMPTY_PASSWORD=yes mysql:8.4

python -m venv .venv && .venv/bin/pip install pymysql cryptography
for mode in plain forupdate rc; do .venv/bin/python s3.py 127.0.0.1 4000 $mode; done
for mode in plain forupdate;    do .venv/bin/python s3.py 127.0.0.1 3307 $mode; done
```

The default `pingcap/tidb` image needs no arguments: it runs a single node with the `unistore`
engine in pessimistic transaction mode at `REPEATABLE-READ`, which is also how a GitHub Actions
service container runs it.
