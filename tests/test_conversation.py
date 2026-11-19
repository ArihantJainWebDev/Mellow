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

    def test_commands_stay_local_and_clear_context(self):
        done = b"data: [DONE]\n\n"
        with (
            patch("mellow.__main__.load_settings", return_value=Settings("fake", "test")),
            patch("builtins.input", side_effect=[
                "/help", "/unknown", "Remember Velmora", "/clear", "What name?", "/quit",
            ]),
            patch("mellow.providers.groq.urlopen", side_effect=[
                io.BytesIO(event("OK") + done), io.BytesIO(event("I don't know.") + done),
            ]) as send,
            redirect_stdout(io.StringIO()) as stdout,
            redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(main([]), 0)
        self.assertEqual(send.call_count, 2)
        messages = json.loads(send.call_args.args[0].data)["messages"]
        self.assertEqual([message["role"] for message in messages], ["system", "user"])
        self.assertEqual(messages[-1]["content"], "What name?")
        self.assertIn("/quit", stdout.getvalue())
        self.assertIn("Conversation cleared.", stdout.getvalue())
        self.assertIn("Unknown command", stderr.getvalue())

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
