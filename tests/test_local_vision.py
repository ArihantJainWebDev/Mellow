import json
import unittest
from unittest.mock import patch
from mellow.local_vision import analyze

class VisionTests(unittest.TestCase):
    @patch("mellow.local_vision._request_content")
    def test_uncertain_location_keeps_description(self, model):
        model.return_value=json.dumps({"summary":"A missing file","x":2,"y":0})
        self.assertEqual(analyze("AA==","error","local"),("A missing file",None))
        packet,port=model.call_args.args
        self.assertEqual(port,1234)
        self.assertEqual(packet["messages"][1]["content"][0]["image_url"]["url"],"data:image/png;base64,AA==")

    @patch("mellow.local_vision._request_content")
    def test_reject_unknown_action_before_inference(self, model):
        with self.assertRaises(ValueError):
            analyze("AA==","execute","local")
        model.assert_not_called()

    @patch("mellow.local_vision._request_content")
    def test_screen_question_stays_in_local_image_request(self, model):
        model.return_value = json.dumps({"summary": "BLUEBIRD"})
        result = analyze("AA==", "screen", "fixture", "What word is on the card?")
        self.assertEqual(result, ("BLUEBIRD", None))
        packet, port = model.call_args.args
        self.assertEqual(port, 1234)
        self.assertEqual(packet["messages"][1]["content"][1]["text"], "What word is on the card?")
        model.reset_mock()
        with self.assertRaises(ValueError):
            analyze("AA==", "screen", "fixture", "x" * 501)
        model.assert_not_called()
