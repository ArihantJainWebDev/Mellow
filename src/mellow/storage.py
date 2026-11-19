"""SQLite storage for local conversations and reply outcomes."""

from contextlib import closing
from pathlib import Path
import sqlite3

SCHEMA_VERSION = 1


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
                db.execute("PRAGMA user_version = 1")
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

    def finish_turn(self, turn_id: int, status: str, reply: str) -> None:
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
