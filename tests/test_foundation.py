"""Offline checks for configuration precedence, errors, and secret handling."""

from contextlib import redirect_stderr, redirect_stdout
from io import StringIO
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from mellow.__main__ import main
from mellow.config import ConfigurationError, load_settings


class FoundationTests(unittest.TestCase):
    def setUp(self):
        # Resolve Windows' temporary directory before isolating configuration.
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.environment = patch.dict(os.environ, {}, clear=True)
        self.environment.start()
        self.addCleanup(self.environment.stop)
        self.env_file = Path(self.directory.name) / ".env"

    def write_config(self, text):
        self.env_file.write_text(text, encoding="utf-8")

    def test_file_load_does_not_mutate_environment_or_expose_key(self):
        self.write_config("GROQ_API_KEY=fictional-secret\nGROQ_MODEL=test-model\n")
        settings = load_settings(self.env_file)
        self.assertEqual(settings.groq_model, "test-model")
        self.assertEqual(settings.groq_api_key, "fictional-secret")
        self.assertNotIn("GROQ_API_KEY", os.environ)
        self.assertNotIn("fictional-secret", repr(settings))

    def test_environment_overrides_file(self):
        self.write_config("GROQ_API_KEY=file-secret\nGROQ_MODEL=file-model\n")
        os.environ.update(GROQ_API_KEY="environment-secret", GROQ_MODEL="environment-model")
        settings = load_settings(self.env_file)
        self.assertEqual(settings.groq_api_key, "environment-secret")
        self.assertEqual(settings.groq_model, "environment-model")

    def test_environment_only_without_file(self):
        os.environ.update(GROQ_API_KEY="fictional-secret", GROQ_MODEL="test-model")
        self.assertEqual(load_settings(self.env_file).groq_model, "test-model")

    def test_empty_environment_does_not_fall_back_to_file(self):
        self.write_config("GROQ_API_KEY=file-secret\nGROQ_MODEL=test-model\n")
        os.environ["GROQ_API_KEY"] = ""
        with self.assertRaisesRegex(ConfigurationError, "Set GROQ_API_KEY"):
            load_settings(self.env_file)

    def test_missing_key_and_missing_model(self):
        for text, missing in (("", "GROQ_API_KEY"), ("GROQ_API_KEY=fictional-secret\n", "GROQ_MODEL")):
            with self.subTest(missing=missing):
                self.write_config(text)
                with self.assertRaisesRegex(ConfigurationError, missing):
                    load_settings(self.env_file)

    def test_whitespace_error_does_not_echo_value(self):
        self.write_config('GROQ_API_KEY="fictional secret"\nGROQ_MODEL=test-model\n')
        with self.assertRaises(ConfigurationError) as caught:
            load_settings(self.env_file)
        self.assertNotIn("fictional secret", str(caught.exception))

    def test_invalid_utf8_has_actionable_error_without_payload(self):
        self.env_file.write_bytes(b"\xffsecret")
        with self.assertRaisesRegex(ConfigurationError, "UTF-8"):
            load_settings(self.env_file)

    def test_cli_success_and_failure_have_distinct_exit_codes(self):
        for text, expected in (("", 2), ("GROQ_API_KEY=fictional-secret\nGROQ_MODEL=test-model\n", 0)):
            with self.subTest(expected=expected):
                self.write_config(text)
                stdout, stderr = StringIO(), StringIO()
                with redirect_stdout(stdout), redirect_stderr(stderr):
                    code = main(["--env-file", str(self.env_file), "--check-config"])
                self.assertEqual(code, expected)
                self.assertNotIn("fictional-secret", stdout.getvalue() + stderr.getvalue())
                if expected == 0:
                    self.assertIn("No API request", stdout.getvalue())
                else:
                    self.assertIn("Configuration error", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
