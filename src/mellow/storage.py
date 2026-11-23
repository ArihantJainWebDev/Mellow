"""SQLite storage for local conversations and reply outcomes."""

from contextlib import closing
from pathlib import Path
import json
import re
import unicodedata
import sqlite3
import time
from uuid import uuid4

from mellow.memory_policy import statement, words

SCHEMA_VERSION = 15
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
                version = 2
            if version == 2:
                db.execute("""
                    CREATE TABLE memory_candidates (
                        id INTEGER PRIMARY KEY,
                        turn_id INTEGER NOT NULL REFERENCES turns(id) ON DELETE CASCADE,
                        kind TEXT NOT NULL CHECK (kind IN ('fact', 'episode')),
                        text TEXT NOT NULL,
                        status TEXT NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending', 'approved', 'rejected')),
                        reviewed_at TEXT
                    )
                """)
                db.execute("CREATE INDEX memory_candidates_status ON memory_candidates(status, id)")
                for turn_id, result in db.execute("""
                    SELECT turn_id, result FROM memory_jobs
                    WHERE status = 'completed' AND result IS NOT NULL
                """):
                    self._insert_candidates(db, turn_id, result)
                version = 3
            if version == 3:
                db.execute("ALTER TABLE memory_jobs ADD COLUMN last_error TEXT")
                version = 4
            if version == 4:
                db.execute("""CREATE TABLE IF NOT EXISTS active_chat (
                    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
                    session_id INTEGER REFERENCES sessions(id) ON DELETE SET NULL
                )""")
                db.execute("""INSERT OR IGNORE INTO active_chat VALUES
                    (1, (SELECT session_id FROM turns ORDER BY id DESC LIMIT 1))""")
                version = 5
            if version == 5:
                db.execute("""CREATE TABLE IF NOT EXISTS automatic_memories (
                    slot TEXT PRIMARY KEY, memory_id INTEGER UNIQUE NOT NULL
                    REFERENCES memory_candidates(id) ON DELETE CASCADE)""")
                db.execute("""CREATE TABLE IF NOT EXISTS hidden_turns (
                    turn_id INTEGER PRIMARY KEY REFERENCES turns(id) ON DELETE CASCADE)""")
                db.execute("""CREATE TABLE IF NOT EXISTS automatic_jobs (
                    turn_id INTEGER PRIMARY KEY REFERENCES memory_jobs(turn_id) ON DELETE CASCADE)""")
                version = 6
            if version == 6:
                db.execute("""CREATE TABLE IF NOT EXISTS assignments (
                    id INTEGER PRIMARY KEY, title TEXT NOT NULL, title_key TEXT NOT NULL,
                    due TEXT NOT NULL, source TEXT NOT NULL, done INTEGER NOT NULL DEFAULT 0
                    CHECK(done IN (0,1)))""")
                db.execute("""CREATE TABLE IF NOT EXISTS assignment_notices (
                    fingerprint TEXT PRIMARY KEY, task_id INTEGER NOT NULL
                    REFERENCES assignments(id) ON DELETE CASCADE)""")
                version = 7
            if version == 7:
                db.execute("""CREATE TABLE IF NOT EXISTS workspace_windows (
                    position INTEGER PRIMARY KEY, app TEXT NOT NULL, title TEXT NOT NULL)""")
                version = 8
            if version == 8:
                columns = {row[1] for row in db.execute("PRAGMA table_info(workspace_windows)")}
                if "profile" not in columns:
                    db.execute("ALTER TABLE workspace_windows RENAME TO legacy_workspace_windows")
                    db.execute("""CREATE TABLE workspace_windows (
                        profile TEXT NOT NULL, position INTEGER NOT NULL, app TEXT NOT NULL,
                        title TEXT NOT NULL, PRIMARY KEY(profile,position))""")
                    db.execute("""INSERT INTO workspace_windows(profile,position,app,title)
                        SELECT 'default',position,app,title FROM legacy_workspace_windows""")
                    db.execute("DROP TABLE legacy_workspace_windows")
                db.execute("""CREATE TABLE IF NOT EXISTS workspace_links (
                    profile TEXT NOT NULL, url TEXT NOT NULL, PRIMARY KEY(profile,url))""")
                version = 9
            if version == 9:
                db.execute("""CREATE TABLE IF NOT EXISTS study_mixes (
                    id INTEGER PRIMARY KEY, label TEXT NOT NULL COLLATE NOCASE UNIQUE, url TEXT NOT NULL UNIQUE,
                    hidden INTEGER NOT NULL DEFAULT 0)""")
                db.execute("""CREATE TABLE IF NOT EXISTS study_sessions (
                    id INTEGER PRIMARY KEY, started INTEGER NOT NULL, ended INTEGER)""")
                db.execute("CREATE UNIQUE INDEX IF NOT EXISTS one_study_session ON study_sessions((1)) WHERE ended IS NULL")
                db.execute("""CREATE TABLE IF NOT EXISTS study_picks (
                    id INTEGER PRIMARY KEY, session_id INTEGER NOT NULL REFERENCES study_sessions(id) ON DELETE CASCADE,
                    mix_id INTEGER NOT NULL REFERENCES study_mixes(id), picked_at INTEGER NOT NULL,
                    liked INTEGER NOT NULL DEFAULT 0, skipped INTEGER NOT NULL DEFAULT 0)""")
                db.execute("""CREATE TABLE IF NOT EXISTS study_tracks (
                    id INTEGER PRIMARY KEY, session_id INTEGER NOT NULL REFERENCES study_sessions(id) ON DELETE CASCADE,
                    at INTEGER NOT NULL, source TEXT NOT NULL, title TEXT NOT NULL, artist TEXT NOT NULL)""")
                db.execute("""CREATE TABLE IF NOT EXISTS study_settings (
                    id INTEGER PRIMARY KEY CHECK(id=1), app TEXT NOT NULL DEFAULT '',
                    cue TEXT NOT NULL DEFAULT '',source TEXT NOT NULL DEFAULT '')""")
                db.execute("INSERT OR IGNORE INTO study_settings(id) VALUES(1)")
                version = 10
            if version == 10:
                db.execute("""CREATE TABLE IF NOT EXISTS desktop_moments (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER NOT NULL, name TEXT NOT NULL,
                    app TEXT NOT NULL, title TEXT NOT NULL, description TEXT NOT NULL,
                    track TEXT NOT NULL, artist TEXT NOT NULL, mix TEXT NOT NULL, search TEXT NOT NULL)""")
                version = 11
            if version == 11:
                if "topic" not in {r[1] for r in db.execute("PRAGMA table_info(study_sessions)")}:
                    db.execute("ALTER TABLE study_sessions ADD COLUMN topic TEXT NOT NULL DEFAULT ''")
                version = 12
            if version == 12:
                if "browser" not in {r[1] for r in db.execute("PRAGMA table_info(workspace_links)")}:
                    db.execute("ALTER TABLE workspace_links ADD COLUMN browser TEXT NOT NULL DEFAULT ''")
                version = 13
            if version == 13:
                if "auto_skip" not in {r[1] for r in db.execute("PRAGMA table_info(study_settings)")}:
                    db.execute("ALTER TABLE study_settings ADD COLUMN auto_skip INTEGER NOT NULL DEFAULT 0")
                db.execute("""CREATE TABLE IF NOT EXISTS study_song_feedback (
                    session_id INTEGER NOT NULL REFERENCES study_sessions(id) ON DELETE CASCADE,
                    source TEXT NOT NULL, song TEXT NOT NULL, artist TEXT NOT NULL,
                    vote TEXT NOT NULL CHECK(vote IN ('keep','skip')), at INTEGER NOT NULL,
                    PRIMARY KEY(session_id,source,song,artist))""")
                version = 14
            if version == 14:
                db.execute("""CREATE TABLE IF NOT EXISTS activity_corrections (
                    app TEXT NOT NULL, features TEXT NOT NULL, label TEXT NOT NULL, PRIMARY KEY(app,features))""")
                db.execute("PRAGMA user_version = 15")
            elif version != SCHEMA_VERSION:
                raise sqlite3.DatabaseError("Unsupported history schema version.")

    def _connect(self) -> sqlite3.Connection:
        db = sqlite3.connect(self.path)
        db.execute("PRAGMA foreign_keys = ON")
        return db

    def active_session(self) -> int | None:
        with closing(self._connect()) as db:
            return db.execute("SELECT session_id FROM active_chat WHERE singleton = 1").fetchone()[0]

    def select_session(self, session_id: int | None) -> None:
        with closing(self._connect()) as db, db:
            db.execute("UPDATE active_chat SET session_id = ? WHERE singleton = 1", (session_id,))

    def remember(self, session_id: int | None, text: str) -> int:
        with closing(self._connect()) as db, db:
            if session_id is None:
                session_id = db.execute("INSERT INTO sessions DEFAULT VALUES").lastrowid
            db.execute("UPDATE active_chat SET session_id=? WHERE singleton=1", (session_id,))
            turn = db.execute("""INSERT INTO turns(session_id,user_text,reply_text,status,finished_at)
                VALUES(?, ?, 'Saved at your request.', 'completed', CURRENT_TIMESTAMP)""",
                (session_id, "Remember that " + text)).lastrowid
            db.execute("""INSERT INTO memory_candidates(turn_id,kind,text,status)
                          VALUES(?, 'fact', ?, 'approved')""", (turn,text))
        return session_id

    def correct_statement(self, previous: str, replacement: str) -> bool:
        """Change one uniquely identified record without guessing between matches."""
        with closing(self._connect()) as db, db:
            db.execute("BEGIN IMMEDIATE")
            rows = db.execute("SELECT id, turn_id, text FROM memory_candidates WHERE status='approved'").fetchall()
            matches = [(mid, turn) for mid, turn, text in rows
                       if _memory_key(previous) == _memory_key(text)]
            if len(matches) != 1:
                return False
            mid, turn = matches[0]
            session = db.execute("SELECT session_id FROM turns WHERE id=?", (turn,)).fetchone()[0]
            source = db.execute("""INSERT INTO turns(session_id,user_text,reply_text,status,finished_at)
                               VALUES(?, ?, 'Correction recorded.', 'completed', CURRENT_TIMESTAMP)""",
                               (session, replacement)).lastrowid
            db.execute("UPDATE memory_candidates SET text=?,turn_id=?,reviewed_at=CURRENT_TIMESTAMP WHERE id=?",
                       (replacement, source, mid))
            db.execute("UPDATE memory_jobs SET result=NULL WHERE turn_id=?", (turn,))
            return True

    def delete_session(self, session_id: int) -> bool:
        with closing(self._connect()) as db, db:
            db.execute("DELETE FROM turns WHERE session_id = ?", (session_id,))
            return db.execute("DELETE FROM sessions WHERE id = ?", (session_id,)).rowcount == 1

    def reset(self) -> None:
        with closing(self._connect()) as db, db:
            db.execute("DELETE FROM turns")
            db.execute("DELETE FROM sessions")
            db.execute("DELETE FROM assignments")
            db.execute("DELETE FROM workspace_windows")
            db.execute("DELETE FROM workspace_links")
            db.execute("DELETE FROM activity_corrections")
            db.execute("DELETE FROM desktop_moments")
            db.execute("DELETE FROM study_sessions")
            db.execute("DELETE FROM study_mixes")
            db.execute("UPDATE study_settings SET app='',cue='',source='',auto_skip=0 WHERE id=1")

    def export(self, path: Path) -> None:
        tables = ("sessions", "turns", "active_chat", "memory_candidates", "memory_jobs",
                  "automatic_memories", "hidden_turns", "assignments", "assignment_notices", "workspace_windows", "workspace_links",
                  "study_mixes", "study_sessions", "study_picks", "study_tracks", "study_settings", "desktop_moments", "study_song_feedback", "activity_corrections")
        with closing(self._connect()) as db:
            db.execute("BEGIN")
            db.row_factory = sqlite3.Row
            snapshot = {table: [dict(row) for row in db.execute(f"SELECT * FROM {table}")]
                        for table in tables}
        content = json.dumps(snapshot, ensure_ascii=False, indent=2)
        with path.open("x", encoding="utf-8") as output:
            output.write(content)

    def forget_topic(self, phrase: str) -> int:
        terms = words(phrase) - {"a", "an", "the", "my", "about", "please", "me", "that"}
        if not terms:
            return 0
        with closing(self._connect()) as db, db:
            db.execute("BEGIN IMMEDIATE")
            # Hide subsequent context in each affected session too: replies like
            # "yes, that one" can otherwise reconstruct the forgotten detail.
            starts = {}
            for turn, session, user, reply in db.execute(
                    "SELECT id, session_id, user_text, reply_text FROM turns ORDER BY id"):
                if terms <= words(user + " " + reply):
                    starts.setdefault(session, turn)
            matched = []
            for turn, session, text, memory_id in db.execute("""SELECT t.id, t.session_id, m.text, m.id
                    FROM memory_candidates m JOIN turns t ON t.id=m.turn_id"""):
                if terms <= words(text):
                    matched.append((memory_id,))
                    starts[session] = min(turn, starts.get(session, turn))
            for session, first in starts.items():
                db.execute("""INSERT OR IGNORE INTO hidden_turns SELECT id FROM turns
                              WHERE session_id = ? AND id >= ?""", (session, first))
            if starts:
                db.execute("DELETE FROM memory_jobs WHERE turn_id IN (SELECT turn_id FROM hidden_turns)")
                db.executemany("DELETE FROM memory_candidates WHERE id=?", matched)
                # Unreviewed interpretations of hidden source text have no
                # independent authority; discard them as well.
                db.execute("""DELETE FROM memory_candidates WHERE turn_id IN
                    (SELECT id FROM turns WHERE id IN (SELECT turn_id FROM hidden_turns))
                    AND id NOT IN (SELECT memory_id FROM automatic_memories)
                    AND NOT EXISTS (SELECT 1 FROM turns t WHERE t.id=memory_candidates.turn_id
                        AND (t.user_text=memory_candidates.text OR
                             t.user_text='Remember that ' || memory_candidates.text))""")
                db.execute("""DELETE FROM memory_candidates WHERE status='pending'
                              AND turn_id IN (SELECT turn_id FROM hidden_turns)""")
            return len(starts)

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
                WHERE session_id = ? AND status = 'completed' AND id NOT IN (SELECT turn_id FROM hidden_turns)
                ORDER BY id DESC LIMIT ?
            """, (session_id, limit)).fetchall()
        return rows[::-1]

    def summary_exchanges(self, session_id: int, skip: int, limit: int = 12) -> list[tuple[int, str, str]]:
        """Return bounded older completed excerpts in chronological order."""
        with closing(self._connect()) as db:
            rows = db.execute("""
                SELECT id, SUBSTR(user_text, 1, 161), SUBSTR(reply_text, 1, 101)
                FROM turns WHERE session_id = ? AND status = 'completed' AND id NOT IN (SELECT turn_id FROM hidden_turns)
                ORDER BY id DESC LIMIT ? OFFSET ?
            """, (session_id, limit, skip)).fetchall()
        return rows[::-1]

    def start_turn(self, session_id: int | None, message: str) -> tuple[int, int]:
        with closing(self._connect()) as db, db:
            if session_id is None:
                session_id = db.execute("INSERT INTO sessions DEFAULT VALUES").lastrowid
            db.execute("UPDATE active_chat SET session_id = ? WHERE singleton = 1", (session_id,))
            turn_id = db.execute(
                "INSERT INTO turns (session_id, user_text) VALUES (?, ?)",
                (session_id, message),
            ).lastrowid
        return session_id, turn_id

    def finish_turn(self, turn_id: int, status: str, reply: str, *, enqueue: bool = False, automatic: bool = False) -> bool:
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

            visible = not db.execute("SELECT 1 FROM hidden_turns WHERE turn_id=?", (turn_id,)).fetchone()
            if enqueue and visible and status == "completed":
                message = db.execute("SELECT user_text FROM turns WHERE id = ?", (turn_id,)).fetchone()[0]
                direct = statement(message)
                if automatic and message.strip().casefold().rstrip(".!") in {"i sold it", "actually, i sold it"}:
                    session = db.execute("SELECT session_id FROM turns WHERE id = ?", (turn_id,)).fetchone()[0]
                    prior = db.execute("""SELECT m.text FROM automatic_memories a
                        JOIN memory_candidates m ON a.memory_id=m.id
                        JOIN turns t ON t.id=m.turn_id WHERE t.session_id=? AND a.slot LIKE 'ownership:%'
                        AND t.id=(SELECT MAX(id) FROM turns WHERE session_id=? AND id<? AND status='completed')
                        """, (session, session, turn_id)).fetchone()
                    if prior:
                        owned = re.fullmatch(r"(?:actually,? )?I own (.+)", prior[0], re.I)
                        if owned:
                            direct = statement("I sold " + owned[1])
                if automatic and direct:
                    old = db.execute("SELECT memory_id FROM automatic_memories WHERE slot = ?",
                                     (direct.slot,)).fetchone()
                    if old:
                        db.execute("DELETE FROM memory_candidates WHERE id = ?", old)
                    memory_id = db.execute("""INSERT INTO memory_candidates
                        (turn_id, kind, text, status) VALUES (?, ?, ?, 'approved')""",
                        (turn_id, direct.kind, direct.text)).lastrowid
                    db.execute("INSERT INTO automatic_memories VALUES (?, ?)", (direct.slot, memory_id))
                    return True
                queued = db.execute("""
                    INSERT INTO memory_jobs (turn_id)
                    SELECT ? WHERE (SELECT COUNT(*) FROM memory_jobs
                                    WHERE status IN ('queued', 'running')) < ?
                """, (turn_id, MAX_ACTIVE_JOBS)).rowcount == 1
                if automatic and queued:
                    db.execute("INSERT INTO automatic_jobs VALUES (?)", (turn_id,))
                return queued
            return False

    def claim_memory_job(self) -> tuple[int, str, str, int] | None:
        now = time.time()
        lease = uuid4().hex
        with closing(self._connect()) as db, db:
            db.execute("BEGIN IMMEDIATE")
            db.execute("""
                UPDATE memory_jobs
                SET status = CASE WHEN attempts >= ? THEN 'failed' ELSE 'queued' END,
                    lease = NULL, last_error = 'Worker stopped before recording the result.'
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

    def allows_automatic_job(self, turn_id: int) -> bool:
        with closing(self._connect()) as db:
            return db.execute("SELECT 1 FROM automatic_jobs WHERE turn_id=?", (turn_id,)).fetchone() is not None

    def extraction_context(self, turn_id: int) -> list[tuple[str, str]]:
        with closing(self._connect()) as db:
            rows = db.execute("""
                SELECT earlier.user_text, earlier.reply_text
                FROM turns AS current JOIN turns AS earlier
                  ON current.session_id = earlier.session_id
                WHERE current.id = ? AND earlier.id < current.id
                  AND earlier.status = 'completed' AND earlier.id NOT IN (SELECT turn_id FROM hidden_turns)
                ORDER BY earlier.id DESC LIMIT 2
            """, (turn_id,)).fetchall()
        return [(user[:400], reply[:400]) for user, reply in reversed(rows)]

    def finish_memory_job(self, turn_id: int, lease: str, attempts: int,
                          result: str | None, *, error: str | None = None, ordinary: bool = False) -> None:
        status = "completed" if result is not None else ("failed" if attempts >= MAX_ATTEMPTS else "queued")
        with closing(self._connect()) as db, db:
            updated = db.execute("""
                UPDATE memory_jobs SET status = ?, ready_at = ?, result = ?, lease = NULL, last_error = ?
                WHERE turn_id = ? AND status = 'running' AND lease = ?
            """, (status, time.time() + 5 * attempts, result, error, turn_id, lease))
            if updated.rowcount == 1 and result is not None:
                if ordinary and db.execute("SELECT 1 FROM automatic_jobs WHERE turn_id=?", (turn_id,)).fetchone():
                    source = db.execute("SELECT user_text FROM turns WHERE id=?", (turn_id,)).fetchone()[0]
                    items = json.loads(result)
                    if len(items) == 1:
                        self._insert_candidates(db, turn_id, json.dumps([
                            {"kind": items[0]["kind"], "text": source}]), deduplicate=True)
                        db.execute("UPDATE memory_candidates SET status='approved' WHERE turn_id=?", (turn_id,))
                        return
                self._insert_candidates(db, turn_id, result, deduplicate=True)

    @staticmethod
    def _insert_candidates(db: sqlite3.Connection, turn_id: int, result: str,
                           *, deduplicate: bool = False) -> None:
        items = json.loads(result)
        if not items:
            return
        seen = {
            (kind, _memory_key(text))
            for kind, text in db.execute("SELECT kind, text FROM memory_candidates")
        } if deduplicate else set()
        for item in items:
            kind, text = item["kind"], item["text"]
            key = kind, _memory_key(text)
            if deduplicate and key in seen:
                continue
            db.execute("INSERT INTO memory_candidates (turn_id, kind, text) VALUES (?, ?, ?)",
                       (turn_id, kind, text))
            seen.add(key)

    def list_memories(self, status: str) -> list[tuple[int, str, str, int]]:
        with closing(self._connect()) as db:
            return db.execute("""
                SELECT id, kind, text, turn_id FROM memory_candidates
                WHERE status = ? ORDER BY id
            """, (status,)).fetchall()

    def review_memory(self, candidate_id: int, *, approve: bool) -> bool:
        status = "approved" if approve else "rejected"
        with closing(self._connect()) as db, db:
            return db.execute("""
                UPDATE memory_candidates SET status = ?, reviewed_at = CURRENT_TIMESTAMP
                WHERE id = ? AND status = 'pending'
            """, (status, candidate_id)).rowcount == 1

    def memory_job_counts(self) -> dict[str, int]:
        with closing(self._connect()) as db:
            return dict(db.execute("SELECT status, COUNT(*) FROM memory_jobs GROUP BY status"))

    def failed_memory_jobs(self, limit: int = 10) -> list[tuple[int, int, str | None]]:
        with closing(self._connect()) as db:
            return db.execute("""
                SELECT turn_id, attempts, last_error FROM memory_jobs
                WHERE status = 'failed' ORDER BY turn_id DESC LIMIT ?
            """, (limit,)).fetchall()

    def retry_memory_job(self, turn_id: int) -> bool:
        with closing(self._connect()) as db, db:
            return db.execute("""
                UPDATE memory_jobs SET status = 'queued', attempts = 0, ready_at = 0,
                    lease = NULL, last_error = NULL
                WHERE turn_id = ? AND status = 'failed'
                  AND (SELECT COUNT(*) FROM memory_jobs WHERE status IN ('queued', 'running')) < ?
            """, (turn_id, MAX_ACTIVE_JOBS)).rowcount == 1

    def edit_memory(self, candidate_id: int, text: str) -> bool:
        with closing(self._connect()) as db, db:
            row = db.execute("""
                UPDATE memory_candidates SET text = ?, reviewed_at = CURRENT_TIMESTAMP
                WHERE id = ? AND status = 'approved' RETURNING turn_id
            """, (text, candidate_id)).fetchone()
            if row is None:
                return False
            db.execute("UPDATE memory_jobs SET result = NULL WHERE turn_id = ?", row)
            return True

    def forget_memory(self, candidate_id: int) -> bool:
        with closing(self._connect()) as db, db:
            row = db.execute("""
                DELETE FROM memory_candidates WHERE id = ? RETURNING turn_id
            """, (candidate_id,)).fetchone()
            if row is None:
                return False
            # Keep the exportable source, but exclude this exchange and later
            # pronoun-dependent turns from prompts and invalidate their leases.
            db.execute("""INSERT OR IGNORE INTO hidden_turns SELECT t.id FROM turns t
                          JOIN turns source ON source.session_id=t.session_id
                          WHERE source.id=? AND t.id>=source.id""", row)
            db.execute("""UPDATE memory_jobs SET result=NULL, lease=NULL, status='completed'
                          WHERE turn_id IN (SELECT turn_id FROM hidden_turns)""")
            return True


    def replace_memory(self, candidate_id: int, approved_id: int) -> bool:
        with closing(self._connect()) as db, db:
            db.execute("BEGIN IMMEDIATE")
            old = db.execute("""
                SELECT turn_id, kind FROM memory_candidates WHERE id = ? AND status = 'approved'
            """, (approved_id,)).fetchone()
            if old is None:
                return False
            updated = db.execute("""
                UPDATE memory_candidates SET status = 'approved', reviewed_at = CURRENT_TIMESTAMP
                WHERE id = ? AND kind = ? AND status = 'pending'
            """, (candidate_id, old[1]))
            if updated.rowcount != 1:
                return False
            db.execute("DELETE FROM memory_candidates WHERE id = ?", (approved_id,))
            db.execute("UPDATE memory_jobs SET result = NULL WHERE turn_id = ?", (old[0],))
            return True


def _memory_key(text: str) -> tuple[str, ...]:
    words = re.findall(r"[^\W_]+", unicodedata.normalize("NFKC", text).casefold())
    for prefix in (["the", "user"], ["user"], ["i"]):
        if words[:len(prefix)] == prefix:
            words = words[len(prefix):]
            break
    forms = {"prefers": "prefer", "likes": "like", "owns": "own", "has": "have"}
    return tuple(forms.get(word, word) for word in words)
