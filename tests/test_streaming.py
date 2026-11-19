"""Offline streaming contract, completion, cleanup, and CLI checks."""

from contextlib import redirect_stdout, redirect_stderr
from io import BytesIO, StringIO
import json
import unittest
from unittest.mock import patch
from urllib.error import HTTPError

from mellow.config import Settings
from mellow.providers.groq import ProviderError, stream_reply
from mellow.__main__ import main


def event(content=None, finish=None, **extra):
    return ("data: " + json.dumps({"choices": [{"delta": {"content": content, **extra}, "finish_reason": finish}]}) + "\n\n").encode()


class StreamingTests(unittest.TestCase):
    settings = Settings("fictional-key", "test-model")

    def test_incremental_utf8_output_and_request(self):
        response = BytesIO(b": heartbeat\n\n" + event(role="assistant") + event("Hello ") + event("world 🌍") + event(finish="stop") + b'data: {"choices": []}\n\ndata: [DONE]\n\n')
        with patch("mellow.providers.groq.urlopen",return_value=response) as send:
            chunks = stream_reply(self.settings, "Hi")
            self.assertEqual(next(chunks), "Hello ")
            self.assertFalse(response.closed)
            self.assertEqual(list(chunks), ["world 🌍"])
        self.assertTrue(response.closed)
        payload = json.loads(send.call_args.args[0].data)
        self.assertTrue(payload["stream"])
        self.assertEqual(payload["messages"], [{"role":"user","content":"Hi"}])
        send.assert_called_once()

    def test_multiline_sse_data(self):
        response = BytesIO(b'data: {"choices":\ndata: [{"delta":{"content":"Hi"},"finish_reason":"stop"}]}\n\ndata: [DONE]\n\n')
        with patch("mellow.providers.groq.urlopen",return_value=response):
            self.assertEqual(list(stream_reply(self.settings,"Hi")),["Hi"])

    def test_generator_close_releases_response(self):
        response=BytesIO(event("first")+event("second"))
        with patch("mellow.providers.groq.urlopen",return_value=response):
            chunks=stream_reply(self.settings,"Hi")
            self.assertEqual(next(chunks),"first")
            chunks.close()
        self.assertTrue(response.closed)

    def test_missing_done_rejects_partial_output(self):
        response=BytesIO(event("partial"))
        with patch("mellow.providers.groq.urlopen",return_value=response):
            chunks=stream_reply(self.settings,"Hi")
            self.assertEqual(next(chunks),"partial")
            with self.assertRaisesRegex(ProviderError,"unexpectedly"):
                next(chunks)
        self.assertTrue(response.closed)

    def test_bad_events_empty_and_nonstandard_completion(self):
        cases=[b'data: not-json\n\n',b'data: {"error":{"message":"fictional-key"}}\n\n',event(12),event()+b'data: [DONE]\n\n',event(finish="stop")+b'data: [DONE]\n\n',event("partial",finish="length")+b'data: [DONE]\n\n']
        for wire in cases:
            with self.subTest(wire=wire):
                response=BytesIO(wire)
                with patch("mellow.providers.groq.urlopen",return_value=response):
                    with self.assertRaises(ProviderError) as caught:
                        list(stream_reply(self.settings,"Hi"))
                self.assertNotIn("fictional-key",str(caught.exception))
                self.assertTrue(response.closed)

    def test_http_errors_are_safe_and_not_retried(self):
        for code in (400,401,403,404,422,429,503):
            with self.subTest(code=code):
                error=HTTPError("https://example.invalid",code,"fictional-key",{},BytesIO())
                with patch("mellow.providers.groq.urlopen",side_effect=error) as send:
                    with self.assertRaises(ProviderError) as caught:
                        list(stream_reply(self.settings,"Hi"))
                self.assertNotIn("fictional-key",str(caught.exception))
                send.assert_called_once()

    def test_read_timeout_after_first_chunk(self):
        class TimedOut(BytesIO):
            def __iter__(self):
                yield event("partial").splitlines(keepends=True)[0]
                yield b"\n"
                raise TimeoutError()
        response=TimedOut()
        with patch("mellow.providers.groq.urlopen",return_value=response):
            chunks=stream_reply(self.settings,"Hi")
            self.assertEqual(next(chunks),"partial")
            with self.assertRaisesRegex(ProviderError,"timed out"):
                next(chunks)
        self.assertTrue(response.closed)

    def test_cli_labels_interrupted_and_failed_partial_text(self):
        for failure, expected in ((KeyboardInterrupt(),130),(ProviderError("Disconnected"),1)):
            with self.subTest(expected=expected):
                closed=[]
                def chunks():
                    try:
                        yield "partial"
                        raise failure
                    finally:
                        closed.append(True)
                stdout,stderr=StringIO(),StringIO()
                with patch("mellow.__main__.load_settings",return_value=self.settings),patch("builtins.input",return_value="Hi"),patch("mellow.__main__.stream_reply",return_value=chunks()),redirect_stdout(stdout),redirect_stderr(stderr):
                    self.assertEqual(main([]),expected)
                self.assertIn("partial",stdout.getvalue())
                self.assertIn("incomplete",stdout.getvalue())
                self.assertTrue(closed)

    def test_blank_input_does_not_send(self):
        with patch("mellow.providers.groq.urlopen") as send:
            with self.assertRaises(ProviderError):
                list(stream_reply(self.settings," "))
            send.assert_not_called()
