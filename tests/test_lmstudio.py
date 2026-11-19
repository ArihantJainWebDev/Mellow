import json
import unittest
from unittest.mock import patch

from mellow.providers.lmstudio import LocalModelError, MemorySuggestion, extract_memories


class LocalModelTests(unittest.TestCase):
    def test_schema_request_and_output(self):
        with patch("mellow.providers.lmstudio.HTTPConnection") as factory:
            connection = factory.return_value
            response = connection.getresponse.return_value
            response.status = 200
            response.read.return_value = json.dumps({"choices": [{
                "finish_reason": "stop",
                "message": {"content": '{"memories":[{"kind":"fact","text":"Prefers tea"}]}'},
            }]}).encode()
            self.assertEqual(extract_memories("test", "I prefer tea"), [
                MemorySuggestion("fact", "Prefers tea"),
            ])
            factory.assert_called_once_with("127.0.0.1", 1234, timeout=30)
            request = connection.request.call_args
            self.assertEqual(request.args, ("POST", "/v1/chat/completions"))
            payload = json.loads(request.kwargs["body"])
            self.assertTrue(payload["response_format"]["json_schema"]["strict"])
            connection.close.assert_called_once()

    def test_rejects_bad_or_truncated_output(self):
        for content, finish in (
            ('{"memories":[{"kind":"fact","text":" " }]}', "stop"),
            ('{"memories":[{"kind":"other","text":"Tea"}]}', "stop"),
            ('{"memories":[]}', "length"),
            ("invalid JSON", "stop"),
        ):
            with self.subTest(content=content, finish=finish):
                with patch("mellow.providers.lmstudio.HTTPConnection") as factory:
                    connection = factory.return_value
                    response = connection.getresponse.return_value
                    response.status = 200
                    response.read.return_value = json.dumps({"choices": [{
                        "finish_reason": finish, "message": {"content": content},
                    }]}).encode()
                    with self.assertRaises(LocalModelError):
                        extract_memories("test", "I prefer tea")
                    connection.close.assert_called_once()

    def test_offline_error_omits_raw_exception(self):
        with patch("mellow.providers.lmstudio.HTTPConnection") as factory:
            factory.return_value.request.side_effect = ConnectionRefusedError("private details")
            with self.assertRaisesRegex(LocalModelError, "^Could not reach or read LM Studio"):
                extract_memories("test", "I prefer tea")
            factory.return_value.close.assert_called_once()
