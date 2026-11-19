"""Conversation context and failed-turn regression checks."""

import io
import json
import unittest
from contextlib import redirect_stdout, redirect_stderr
from unittest.mock import patch

from mellow.__main__ import main
from mellow.config import Settings
from mellow.conversation import Conversation


def event(text):
    data = {"choices": [{"delta": {"content": text}, "finish_reason": "stop"}]}
    return f"data: {json.dumps(data)}\n\n".encode()


class ConversationTests(unittest.TestCase):
    def test_bounded_complete_pairs(self):
        conversation = Conversation()
        for index in range(6):
            conversation.record(f"question {index}", f"answer {index}")
        context = conversation.context()
        self.assertEqual(context[0]["role"], "system")
        self.assertEqual([item["content"] for item in context[1:]], [
            text for index in range(2, 6)
            for text in (f"question {index}", f"answer {index}")
        ])

    def test_followup_http_payload_skips_failed_exchange(self):
        done = b"data: [DONE]\n\n"
        responses = [io.BytesIO(event("Blue.") + done),
                     io.BytesIO(event("Partial")),
                     io.BytesIO(event("Blue!") + done)]
        with (
            patch("mellow.__main__.load_settings", return_value=Settings("fake", "test")),
            patch("builtins.input", side_effect=["My color is blue.", "Fail", "Which color?", EOFError()]),
            patch("mellow.providers.groq.urlopen", side_effect=responses) as send,
            redirect_stdout(io.StringIO()),
            redirect_stderr(io.StringIO()),
        ):
            self.assertEqual(main([]), 0)
        request = json.loads(send.call_args.args[0].data)
        self.assertEqual(request["messages"][0]["role"], "system")
        self.assertEqual(request["messages"][1:], [
            {"role": "user", "content": "My color is blue."},
            {"role": "assistant", "content": "Blue."},
            {"role": "user", "content": "Which color?"},
        ])
