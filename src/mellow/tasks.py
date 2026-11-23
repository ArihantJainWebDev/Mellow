"""Local assignment commands shared by the desktop composer and CLI."""
from contextlib import closing
from datetime import date, datetime, timedelta
import calendar
import hashlib
import re
import sqlite3

HELP = ("Add assignment Physics lab due tomorrow\n"
        "Move Physics lab to 2026-12-04\nWhat's due this week?\n"
        "Mark Physics lab done\n/tasks done\n"
        "/notice Physics lab due 2026-12-04 (one dated item per line)")
DAYS = {name.casefold(): i for i, name in enumerate(calendar.day_name)}
DATE = r"(?:\d{4}-\d{2}-\d{2}|\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4}|today|tomorrow|" + "|".join(DAYS) + ")"
FLAGS = re.IGNORECASE


def deadline(text, today):
    key = text.strip().lower()
    if key in ("today", "tomorrow"):
        return today + timedelta(days=int(key == "tomorrow"))
    if key in DAYS:
        return today + timedelta(days=(DAYS[key] - today.weekday()) % 7)
    if re.fullmatch(r"\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4}", key):
        for pattern in ("%d %B %Y", "%d %b %Y"):
            try:
                return datetime.strptime(key, pattern).date()
            except ValueError:
                pass
    return date.fromisoformat(key)


def _new(db, title, due, source):
    title = " ".join(title.strip(" \t:•-").split())
    if not 1 <= len(title) <= 200:
        raise ValueError("Use a title between 1 and 200 characters.")
    key = title.casefold()
    row = db.execute("SELECT id FROM assignments WHERE title_key=? AND due=? AND done=0",
                     (key, due)).fetchone()
    if row:
        return row[0], False
    result = db.execute("INSERT INTO assignments(title,title_key,due,source) VALUES(?,?,?,?)",
                        (title, key, due, source))
    return result.lastrowid, True


def _notice(db, value, today):
    lines = [line.strip() for line in value.splitlines() if line.strip()]
    # Common two-line notice: subject/title followed by a separate Due: line.
    if len(lines) == 2 and re.match(r"^(?:due|deadline)\b", lines[1], FLAGS):
        lines = [re.sub(r"^subject:\s*", "", lines[0], flags=FLAGS) + " " + lines[1]]
    result = []
    for number, line in enumerate(lines, 1):
        revision = re.fullmatch(r"(.+?)\s+(?:deadline|due date)\s+(?:changed|moved|extended)\s+to\s+(" + DATE + r")[.!]?", line, FLAGS)
        match = revision or re.fullmatch(r"(.+?)\s*(?:\bdue\b|\bdeadline\b)\s*(?:on\s+|by\s+|:\s*)?(" + DATE + r")[.!]?", line, FLAGS)
        if not match:
            result.append(f"Line {number} skipped. Use Title due YYYY-MM-DD.")
            continue
        fingerprint = hashlib.sha256(" ".join(line.casefold().split()).encode()).hexdigest()
        row = db.execute("SELECT task_id FROM assignment_notices WHERE fingerprint=?", (fingerprint,)).fetchone()
        if row:
            result.append(f"Line {number} already imported as #{row[0]}.")
            continue
        try:
            due = deadline(match[2], today)
            if revision:
                key = " ".join(match[1].strip(" •-").casefold().split())
                matches = db.execute("SELECT id FROM assignments WHERE title_key=? AND done=0", (key,)).fetchall()
                if len(matches) != 1:
                    result.append(f"Line {number} skipped. Use /tasks and move a specific #ID.")
                    continue
                task = matches[0][0]
                db.execute("UPDATE assignments SET due=? WHERE id=?", (due.isoformat(), task))
                action = "Moved"
            else:
                task, created = _new(db, match[1], due.isoformat(), "notice")
                action = "Added" if created else "Matched"
        except ValueError:
            result.append(f"Line {number} skipped. Check the title and date.")
            continue
        db.execute("INSERT INTO assignment_notices VALUES(?,?)", (fingerprint, task))
        result.append(f"{action} #{task} {match[1]} · {due}")
    return "\n".join(result) or "Paste the notice after /notice. Use Title due YYYY-MM-DD."


def task_command(message, history, today=None):
    """Return None only when the message belongs to the ordinary chat."""
    today = today or date.today()
    text = message.strip()
    notice = re.fullmatch(r"/notice(?:\s+(.*))?", text, FLAGS | re.DOTALL)
    listing = re.fullmatch(r"(?:/due|/tasks|what(?:['’]s| is) due)(?:\s+(today|this week|week|done|all))?[?.!]?", text, FLAGS)
    add = re.fullmatch(r"(?:/task add|add (?:an? )?(?:assignment|task))(?:\s+(?:called\s+)?(.+?)\s+due\s+(.+?))[.!]?", text, FLAGS)
    move = re.fullmatch(r"(?:/task move|move|reschedule|change (?:the )?deadline (?:for|of))\s+(.+?)\s+to\s+(" + DATE + r")[.!]?", text, FLAGS)
    done = re.fullmatch(r"(?:mark\s+(.+?)\s+(?:as )?done|/task done\s+(.+?))[.!]?", text, FLAGS)
    if not any((notice, listing, add, move, done)):
        if re.match(r"^(?:/tasks?\b|/due\b|/notice\b|add (?:an? )?assignment\b)", text, FLAGS):
            return HELP
        return None
    if history is None:
        return "Assignments are unavailable in private chat."
    if len(text) > 16000:
        return "Please split the notice into batches under 16,000 characters."
    with closing(sqlite3.connect(history.path)) as db, db:
        if listing:
            scope = (listing[1] or "").lower()
            until = date.max
            if scope == "today":
                until = today
            elif scope in ("week", "this week"):
                until = today + timedelta(days=6-today.weekday())
            rows = db.execute("""SELECT id,title,due,done FROM assignments
                WHERE (?='all' OR done=?) AND due<=? ORDER BY done,due,id""",
                (scope, int(scope == "done"), until.isoformat())).fetchall()
            return "\n".join(f"#{i} {title} · {due}" + (" · done" if closed else
                             " · overdue" if due < today.isoformat() else "")
                             for i, title, due, closed in rows) or "No matching assignments."
        db.execute("BEGIN IMMEDIATE")
        if notice:
            return _notice(db, notice[1] or "", today)
        if add:
            try:
                due = deadline(add[2], today)
                task, created = _new(db, add[1], due.isoformat(), "manual")
            except ValueError:
                return "Check the title and deadline. Use YYYY-MM-DD, a weekday, today or tomorrow."
            return f"{'Added' if created else 'Already saved'} #{task} {add[1]} · {due}"
        ref = (move[1] if move else done[1] or done[2]).strip()
        if re.fullmatch(r"#?\d+", ref):
            rows = db.execute("SELECT id,title,done FROM assignments WHERE CAST(id AS TEXT)=?",
                              (ref.lstrip("#"),)).fetchall()
        else:
            rows = db.execute("SELECT id,title,done FROM assignments WHERE title_key=? AND done=0",
                              (ref.casefold(),)).fetchall()
        if len(rows) != 1:
            return "No unique open assignment. Use /tasks and enter its #ID."
        task, title, closed = rows[0]
        if closed:
            return f"#{task} {title} is already done."
        if move:
            try:
                due = deadline(move[2], today)
            except ValueError:
                return "Invalid deadline. Use YYYY-MM-DD."
            db.execute("UPDATE assignments SET due=? WHERE id=?", (due.isoformat(), task))
            return f"Moved #{task} {title} to {due}."
        db.execute("UPDATE assignments SET done=1 WHERE id=?", (task,))
        return f"Completed #{task} {title}."
