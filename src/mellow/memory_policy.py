"""Conservative, local rules for unambiguous first-person memory statements."""

import re
from dataclasses import dataclass


@dataclass(frozen=True)
class Statement:
    kind: str
    slot: str
    text: str


def words(text: str) -> set[str]:
    tokens = re.findall(r"[a-z0-9]+", text.lower())
    return {"bicycle" if word in {"bike", "bikes", "bicycles"} else word for word in tokens}


def statement(message: str) -> Statement | None:
    text = message.strip().rstrip(".")
    if len(text) > 160 or not re.fullmatch(r"[\w ,'-]+", text):
        return None
    if re.search(r"\b(if|perhaps|maybe|fictional|suppose|probably|pretend|wish|and|but|or|"
                 r"password|pin|key|secret|token|medical|diagnosed|bank|account|address|"
                 r"think|might|would|could)\b|\d", text, re.I):
        return None
    # Narrow auto-save vocabulary. Other topics stay in the local review queue.
    match = re.fullmatch(r"(?:actually,? )?I (own|sold|ride) (?:a |an |the |my )?"
                         r"((?:(?:red|blue|green|black|white|yellow) )?(?:bike|bicycle|car|scooter))", text, re.I)
    if match:
        verb, item = (part.lower() for part in match.groups())
        noun = item.split()[-1].replace("bike", "bicycle")
        slot = ("ride" if verb == "ride" else "ownership") + ":" + noun
        return Statement("episode" if verb == "sold" else "fact", slot, text)
    match = re.fullmatch(r"(?:actually,? )?I (?:prefer|like) (short|long|brief|detailed|concise) "
                         r"(answers|replies|responses)", text, re.I)
    if match:
        return Statement("fact", "preference:reply-length", text)
    # Recognized low-risk activities and projects; unrestricted personal data
    # and model-generated paraphrases require review.
    activity = re.fullmatch(r"I (like|love|enjoy|prefer) (reading|drawing|cycling|hiking|"
                            r"coding|swimming|anime|tea|coffee|chess|painting|music)", text, re.I)
    if activity:
        return Statement("fact", "interest:" + activity[2].lower(), text)
    project = re.fullmatch(r"(?:yesterday |today )?I (?:started|finished|completed) "
                           r"(?:a |the |my )?(?:robotics|coding|drawing|music|college) "
                           r"(?:workshop|project|course|assignment)", text, re.I)
    if project:
        return Statement("episode", "event:" + text.casefold(), text)
    # Preserve a literal assertion for common low-risk subjects. Restrict the
    # subject, exclude embedded clauses, and never auto-promote a model guess.
    if re.search(r"\b(?:allerg\w*|therapy|medic\w*|prescription|diabet\w*|cancer|"
                 r"depress\w*|religio\w*|politic\w*|income|salary|credit|debit|"
                 r"address|private|diagnosis|passcode|account|secret|email)\b", text, re.I):
        return None
    favorite = re.fullmatch(r"(?:actually,? )?my (?:favorite|favourite) "
                            r"(game|movie|book|color|song|sport|programming language) is "
                            r"([a-z][a-z '-]{0,50})", text, re.I)
    if favorite:
        return Statement("fact", "favorite:" + favorite[1].lower(), text)
    learning = re.fullmatch(r"I (?:am learning|enjoy learning) "
                            r"(python|guitar|drawing|chess|japanese|spanish|rust|java|piano)", text, re.I)
    if learning:
        return Statement("fact", "learning:" + learning[1].lower(), text)
    return None
