"""Spike S3 (ADR 0001): does "lock the facility row, then re-read" prevent double booking?

Two transactions try to book overlapping slots of the same facility. Each one first locks the
facility row (SELECT ... FOR UPDATE), then checks for overlapping bookings, then inserts. The
winner holds the lock for a second so the other has to queue behind it.

    python s3.py <host> <port> <mode> [password]

Modes, for how the overlap check reads:
    plain      a plain SELECT. Sound on MySQL, but on TiDB it reads the snapshot taken at BEGIN.
    forupdate  SELECT ... FOR UPDATE, a current read.
    rc         a plain SELECT under READ COMMITTED.

Needs `pip install pymysql cryptography`. See README.md for how to start the databases.
"""
import sys, threading, time, pymysql

host, port = sys.argv[1], int(sys.argv[2])
mode = sys.argv[3]  # plain | forupdate | rc

def conn(autocommit=True):
    return pymysql.connect(host=host, port=port, user="root", password=sys.argv[4] if len(sys.argv) > 4 else "", autocommit=autocommit)

def setup():
    c = conn(); cur = c.cursor()
    cur.execute("DROP DATABASE IF EXISTS spike"); cur.execute("CREATE DATABASE spike"); cur.execute("USE spike")
    cur.execute("CREATE TABLE Facility (Id INT PRIMARY KEY)")
    cur.execute("CREATE TABLE Booking (Id INT AUTO_INCREMENT PRIMARY KEY, FacilityId INT, StartH INT, EndH INT, Who VARCHAR(10), KEY (FacilityId, StartH))")
    cur.execute("INSERT INTO Facility VALUES (1)")
    c.close()

def book(who, start, end, results, gate, hold):
    c = conn(autocommit=False); cur = c.cursor(); cur.execute("USE spike")
    if mode == "rc":
        cur.execute("SET TRANSACTION ISOLATION LEVEL READ COMMITTED")
    cur.execute("BEGIN")
    gate.wait()                                    # both transactions have begun before either locks
    cur.execute("SELECT Id FROM Facility WHERE Id = 1 FOR UPDATE")   # serialise on the facility row
    suffix = " FOR UPDATE" if mode == "forupdate" else ""
    cur.execute(f"SELECT COUNT(*) FROM Booking WHERE FacilityId=1 AND StartH < %s AND EndH > %s{suffix}", (end, start))
    overlapping = cur.fetchone()[0]
    if hold: time.sleep(hold)                      # the winner holds the lock long enough for the other to queue
    if overlapping == 0:
        cur.execute("INSERT INTO Booking (FacilityId, StartH, EndH, Who) VALUES (1,%s,%s,%s)", (start, end, who))
        results[who] = "booked"
    else:
        results[who] = "rejected"
    c.commit(); c.close()

def trial():
    setup()
    results = {}; gate = threading.Barrier(2)
    t1 = threading.Thread(target=book, args=("A", 9, 11, results, gate, 1.0))
    t2 = threading.Thread(target=book, args=("B", 10, 12, results, gate, 0))   # overlaps A
    t1.start(); time.sleep(0.3); t2.start(); t1.join(); t2.join()
    c = conn(); cur = c.cursor(); cur.execute("USE spike"); cur.execute("SELECT COUNT(*) FROM Booking"); n = cur.fetchone()[0]
    return results, n

runs = 5
bad = 0
for i in range(runs):
    results, n = trial()
    if n > 1: bad += 1
    print(f"  run {i+1}: {results} rows={n} {'DOUBLE BOOKED' if n > 1 else 'ok'}")
print(f"RESULT mode={mode}: double-booked in {bad}/{runs} runs")
