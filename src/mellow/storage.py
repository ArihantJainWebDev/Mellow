"""SQLite storage for local conversations and reply outcomes."""

from contextlib import closing
from pathlib import Path
import sqlite3
import time
from uuid import uuid4

SCHEMA_VERSION = 2
MAX_ACTIVE_JOBS = 100
MAX_ATTEMPTS = 3
LEASE_SECONDS = 120


class History:
    def __init__(self, path: Path) -> None:
        self.path = path
        path.parent.mkdir(parents=True, exist_ok=True)
        with closing(self._connect()) as db, db:
            db.execute("BEGIN IMMEDIATE")
            version = db.execute("PRAGMA user_version").fetchone()[0]
            if version == 0:
                db.execute("""
                    CREATE TABLE sessions (
                        id INTEGER PRIMARY KEY,
                        created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                    )
                """)
                db.execute("""
                    CREATE TABLE turns (
                        id INTEGER PRIMARY KEY,
                        session_id INTEGER NOT NULL REFERENCES sessions(id),
                        user_text TEXT NOT NULL,
                        reply_text TEXT NOT NULL DEFAULT '',
                        status TEXT NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending', 'completed', 'failed', 'interrupted')),
                        created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        finished_at TEXT
                    )
                """)
                db.execute("CREATE INDEX turns_session_id ON turns(session_id, id)")
                version = 1
            if version == 1:
                db.execute("""
                    CREATE TABLE memory_jobs (
                        turn_id INTEGER PRIMARY KEY REFERENCES turns(id) ON DELETE CASCADE,
                        status TEXT NOT NULL DEFAULT 'queued',
                        attempts INTEGER NOT NULL DEFAULT 0,
                        ready_at REAL NOT NULL DEFAULT 0,
                        lease TEXT,
                        result TEXT
                    )
                """)
                db.execute("CREATE INDEX memory_jobs_ready ON memory_jobs(status, ready_at)")
                db.execute("PRAGMA user_version = 2")
            elif version != SCHEMA_VERSION:
                raise sqlite3.DatabaseError("Unsupported history schema version.")

    def _connect(self) -> sqlite3.Connection:
        db = sqlite3.connect(self.path)
        db.execute("PRAGMA foreign_keys = ON")
        return db

    def list_sessions(self) -> list[tuple[int, str, int]]:
        with closing(self._connect()) as db:
            return db.execute("""
                SELECT sessions.id, sessions.created_at, COUNT(turns.id)
                FROM sessions LEFT JOIN turns ON turns.session_id = sessions.id
                GROUP BY sessions.id ORDER BY sessions.id DESC
            """).fetchall()

    def recent_completed(self, session_id: int, limit: int) -> list[tuple[str, str]] | None:
        with closing(self._connect()) as db:
            if db.execute("SELECT 1 FROM sessions WHERE id = ?", (session_id,)).fetchone() is None:
                return None
            rows = db.execute("""
                SELECT user_text, reply_text FROM turns
                WHERE session_id = ? AND status = 'completed'
                ORDER BY id DESC LIMIT ?
            """, (session_id, limit)).fetchall()
        return rows[::-1]

    def start_turn(self, session_id: int | None, message: str) -> tuple[int, int]:
        with closing(self._connect()) as db, db:
            if session_id is None:
                session_id = db.execute("INSERT INTO sessions DEFAULT VALUES").lastrowid
            turn_id = db.execute(
                "INSERT INTO turns (session_id, user_text) VALUES (?, ?)",
                (session_id, message),
            ).lastrowid
        return session_id, turn_id

    def finish_turn(self, turn_id: int, status: str, reply: str, *, enqueue: bool = False) -> bool:
        if status not in {"completed", "failed", "interrupted"}:
            raise ValueError("Expected a final reply status.")
        with closing(self._connect()) as db, db:
            result = db.execute(
                """UPDATE turns SET status = ?, reply_text = ?, finished_at = CURRENT_TIMESTAMP
                   WHERE id = ? AND status = 'pending'""",
                (status, reply, turn_id),
            )
            if result.rowcount != 1:
                raise sqlite3.DatabaseError("Turn is missing or already finalized.")

            if enqueue and status == "completed":
                return db.execute("""
                    INSERT INTO memory_jobs (turn_id)
                    SELECT ? WHERE (SELECT COUNT(*) FROM memory_jobs
                                    WHERE status IN ('queued', 'running')) < ?
                """, (turn_id, MAX_ACTIVE_JOBS)).rowcount == 1
            return False

    def claim_memory_job(self) -> tuple[int, str, str, int] | None:
        now = time.time()
        lease = uuid4().hex
        with closing(self._connect()) as db, db:
            db.execute("BEGIN IMMEDIATE")
            db.execute("""
                UPDATE memory_jobs
                SET status = CASE WHEN attempts >= ? THEN 'failed' ELSE 'queued' END,
                    lease = NULL
                WHERE status = 'running' AND ready_at <= ?
            """, (MAX_ATTEMPTS, now))
            row = db.execute("""
                SELECT jobs.turn_id, turns.user_text, jobs.attempts
                FROM memory_jobs AS jobs JOIN turns ON turns.id = jobs.turn_id
                WHERE jobs.status = 'queued' AND jobs.ready_at <= ?
                ORDER BY jobs.ready_at, jobs.turn_id LIMIT 1
            """, (now,)).fetchone()
            if row is None:
                return None
            turn_id, message, attempts = row
            db.execute("""
                UPDATE memory_jobs SET status = 'running', lease = ?,
                    ready_at = ?, attempts = attempts + 1 WHERE turn_id = ?
            """, (lease, now + LEASE_SECONDS, turn_id))
            return turn_id, lease, message, attempts + 1

    def finish_memory_job(self, turn_id: int, lease: str, attempts: int, result: str | None) -> None:
        status = "completed" if result is not None else ("failed" if attempts >= MAX_ATTEMPTS else "queued")
        with closing(self._connect()) as db, db:
            db.execute("""
                UPDATE memory_jobs SET status = ?, ready_at = ?, result = ?, lease = NULL
                WHERE turn_id = ? AND status = 'running' AND lease = ?
            """, (status, time.time() + 5 * attempts, result, turn_id, lease))

    def memory_job_counts(self) -> dict[str, int]:
        with closing(self._connect()) as db:
            return dict(db.execute("SELECT status, COUNT(*) FROM memory_jobs GROUP BY status"))
