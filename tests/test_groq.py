"""Offline HTTP-contract and CLI checks; no actual Groq calls."""

from contextlib import redirect_stderr, redirect_stdout
from io import BytesIO, StringIO
import json
import unittest
from unittest.mock import patch
from urllib.error import HTTPError, URLError

from mellow.__main__ import main
from mellow.config import Settings
from mellow.providers.groq import ENDPOINT, ProviderError, reply


class GroqTests(unittest.TestCase):
    def setUp(self):
        self.settings = Settings("fictional-secret", "test-model")

    def test_request_contract_and_response(self):
        response = BytesIO(json.dumps({"choices": [{"message": {"content": " Hello! "}}]}).encode())
        with patch("mellow.providers.groq.urlopen", return_value=response) as send:
            self.assertEqual(reply(self.settings, "Hi"), "Hello!")
        request = send.call_args.args[0]
        self.assertEqual(request.full_url, ENDPOINT)
        self.assertEqual(request.get_method(), "POST")
        self.assertEqual(request.get_header("Authorization"), "Bearer fictional-secret")
        payload = json.loads(request.data)
        self.assertEqual(payload["messages"], [{"role": "user", "content": "Hi"}])
        self.assertEqual(payload["model"], "test-model")
        self.assertEqual(payload["max_completion_tokens"], 256)
        self.assertFalse(payload["stream"])
        self.assertEqual(send.call_args.kwargs["timeout"], 30)

    def test_http_failures_do_not_expose_body_or_secret(self):
        for code, expected in ((401,"API key"),(403,"denied"),(429,"limit"),(400,"model"),(404,"model"),(422,"model"),(503,"unavailable"),(418,"418")):
            with self.subTest(code=code):
                body = BytesIO(b"fictional-secret private-provider-body")
                error = HTTPError(ENDPOINT, code, "private-provider-body", {}, body)
                with patch("mellow.providers.groq.urlopen", side_effect=error) as send:
                    with self.assertRaises(ProviderError) as caught:
                        reply(self.settings, "Hi")
                self.assertIn(expected.lower(), str(caught.exception).lower())
                self.assertNotIn("fictional-secret", str(caught.exception))
                self.assertNotIn("private-provider-body", str(caught.exception))
                self.assertTrue(body.closed)
                send.assert_called_once()

    def test_network_and_timeout(self):
        for error, expected in ((URLError("private details"), "network"), (TimeoutError(), "timed out")):
            with self.subTest(error=type(error).__name__):
                with patch("mellow.providers.groq.urlopen", side_effect=error):
                    with self.assertRaisesRegex(ProviderError, expected):
                        reply(self.settings, "Hi")

    def test_invalid_responses(self):
        for data in (b"invalid-json", b"null", b"{}", b'{"choices": []}', b'{"choices": [{"message": {"content": null}}]}', b'{"choices": [{"message": {"content": " "}}]}'):
            with self.subTest(data=data):
                with patch("mellow.providers.groq.urlopen", return_value=BytesIO(data)):
                    with self.assertRaises(ProviderError):
                        reply(self.settings, "Hi")

    def test_blank_input_never_sends(self):
        with patch("mellow.providers.groq.urlopen") as send:
            with self.assertRaises(ProviderError):
                reply(self.settings, "  ")
            send.assert_not_called()

    def test_cli_interaction(self):
        for input_value, error, expected in (("Hi",None,0),(" ",None,2),(EOFError(),None,130),(KeyboardInterrupt(),None,130),("Hi",ProviderError("Offline"),1)):
            with self.subTest(expected=expected, error=error):
                stdout, stderr = StringIO(), StringIO()
                input_patch = patch("builtins.input", side_effect=input_value) if isinstance(input_value, BaseException) else patch("builtins.input", return_value=input_value)
                with patch("mellow.__main__.load_settings", return_value=self.settings), input_patch, patch("mellow.__main__.stream_reply", return_value=(part for part in ["Hello"]), side_effect=error) as provider, redirect_stdout(stdout), redirect_stderr(stderr):
                    code = main(["--once"])
                self.assertEqual(code, expected)
                if expected == 0:
                    provider.assert_called_once_with(self.settings, "Hi")
                    self.assertIn("Mellow: Hello", stdout.getvalue())
                elif expected in (2,130):
                    provider.assert_not_called()
                else:
                    self.assertIn("Provider error: Offline", stderr.getvalue())
                self.assertNotIn("fictional-secret", stdout.getvalue()+stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
